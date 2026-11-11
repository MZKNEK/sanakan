#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Sanakan.Config.Model;
using Sanakan.Services;
using Sanakan.ShindenCompat.Models;
using ShindenAPI;
using ApiClient = ShindenAPI.Shinden;

namespace Sanakan.ShindenCompat
{
    public class Response<T>
    {
        public Response(HttpStatusCode code, T body = default, TimeSpan? retryAfter = null)
        {
            Code = code;
            Body = body;
            RetryAfter = retryAfter;
        }

        public T Body { get; }
        public HttpStatusCode Code { get; }
        // how long Shinden asks to wait (429, 503), also when the library held the request back itself
        public TimeSpan? RetryAfter { get; }

        public bool IsSuccessStatusCode() => (int)Code >= 200 && (int)Code <= 299;
    }

    public class ShindenException : Exception
    {
        public ShindenException(Error error) : base($"{error.Type} ({error.Code}): {error.Message}")
        {
            Error = error;
        }

        public Error Error { get; }
    }

    // Keeps the call surface of Shinden.NET 0.3 on top of the 0.7 client.
    public class ShindenClient
    {
        private const string DefaultUri = "https://shinden.pl/";
        private const uint SearchLimit = 20;

        private readonly ApiClient _api;

        public ShindenClient(ApiClient api)
        {
            _api = api;
            Title = new TitleModule(api);
            Search = new SearchModule(api);
            User = new UserModule(api);
            Ex = new ExperimentalModule(api);
        }

        public TitleModule Title { get; }
        public SearchModule Search { get; }
        public UserModule User { get; }
        public ExperimentalModule Ex { get; }

        public static ShindenClient Create(ConfigShinden config, ILogger<ApiClient> logger, HttpMessageHandler handler, TimeSpan timeout)
            => new ShindenClient(new ApiClient(builder => Build(builder.WithRequestTimeout(timeout), config), logger, handler));

        // throws and keeps the current settings when the new ones are invalid; requests in progress finish with the old ones
        public void Apply(ConfigShinden config) => _api.Reconfigure(builder => Build(builder, config));

        public ShindenAPI.Config Active => _api.Config;

        public const int RequestsPerMinute = 290;
        public static readonly TimeSpan RateLimitMaxWait = TimeSpan.FromSeconds(10);

        private static ShindenAPI.Config Build(ConfigBuilder builder, ConfigShinden config)
        {
            // without a new token the PHP key also works on compat routes
            var token = string.IsNullOrWhiteSpace(config.ApiToken) ? config.Token : config.ApiToken;

            // set even when equal to the main uri, a reconfigured client would keep the previous one
            // the bot and the site's card sync share one token (300 requests a minute): stay under it, let a request
            // wait a little for a pause instead of failing, and repeat a refused GET once
            return builder.WithRateLimit(RequestsPerMinute, TimeSpan.FromMinutes(1))
                .WithRateLimitMaxWait(RateLimitMaxWait)
                .WithRateLimitRetries(1)
                .WithUri(GetUri(config))
                .WithLegacyUri(GetLegacyUri(config))
                .WithAuth(auth => auth.WithToken(token).WithUserAgent(config.UserAgent).Build())
                .WithLegacyAuth(auth => auth.WithToken(config.Token).WithUserAgent(config.UserAgent)
                    .WithMarmolade(config.Marmolade).Build())
                .Build();
        }

        public static string GetUri(ConfigShinden config)
            => string.IsNullOrWhiteSpace(config.BaseUri) ? DefaultUri : WithoutLegacyPath(config.BaseUri);

        public static string GetLegacyUri(ConfigShinden config)
            => string.IsNullOrWhiteSpace(config.BaseUri) ? DefaultUri : config.BaseUri;

        private static string WithoutLegacyPath(string uri)
        {
            var parsed = new Uri(uri);
            var path = parsed.AbsolutePath.TrimEnd('/');
            if (!path.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
                return uri;

            return new UriBuilder(parsed) { Path = path.Substring(0, path.Length - 4) + "/" }.Uri.ToString();
        }

        public async Task<Response<ICharacterInfo>> GetCharacterInfoAsync(ulong id)
        {
            if (CustomCharacters.IsCustom(id))
                return new Response<ICharacterInfo>(HttpStatusCode.NotFound);

            return Wrap(await _api.Characters.InfoAsync(id).ConfigureAwait(false), x => x.ToModel());
        }

        public async Task<Response<string>> GetHealthAsync()
            => Wrap(await _api.HealthAsync().ConfigureAwait(false), x => x.Response);

        public async Task<Response<List<INewEpisode>>> GetNewEpisodesAsync()
            => Wrap(await _api.Episodes.NewAsync().ConfigureAwait(false), x => x.Select(e => e.ToModel()).ToList());

        internal static Response<TOut> Wrap<TIn, TOut>(ErrorOr<TIn> result, Func<TIn, TOut> map) where TIn : class
        {
            if (result.TryGet(out var value))
                return new Response<TOut>(HttpStatusCode.OK, map(value));

            var error = result.Error;
            switch (error.Type)
            {
                case ErrorType.Http:
                    return new Response<TOut>((HttpStatusCode)error.Code, retryAfter: error.RetryAfter);

                // the library did not send the request, the token is paused after a 429
                case ErrorType.RateLimit:
                    return new Response<TOut>(HttpStatusCode.TooManyRequests, retryAfter: error.RetryAfter);

                case ErrorType.Parsing:
                    ShindenActivity.Default.ParsingFailed();
                    return new Response<TOut>((HttpStatusCode)error.Code);

                default:
                    throw new ShindenException(error);
            }
        }

        public class TitleModule
        {
            private readonly ApiClient _api;

            internal TitleModule(ApiClient api) => _api = api;

            public async Task<Response<ITitleInfo>> GetInfoAsync(ulong id)
                => Wrap(await _api.Titles.InfoAsync(id).ConfigureAwait(false), x => x.ToModel());

            public async Task<Response<List<IRelation>>> GetCharactersAsync(ulong id)
                => Wrap(await _api.Titles.CharactersAsync(id).ConfigureAwait(false), x => x.Relations.ToRelations());
        }

        public class SearchModule
        {
            private readonly ApiClient _api;

            internal SearchModule(ApiClient api) => _api = api;

            public async Task<Response<List<IUserSearch>>> UserAsync(string nick)
                => Wrap(await _api.Search.UsersAsync(nick, SearchLimit).ConfigureAwait(false), x => x.Select(u => u.ToModel()).ToList());

            public async Task<Response<List<IPersonSearch>>> CharacterAsync(string name)
                => Wrap(await _api.Search.CharactersAsync(name, SearchLimit).ConfigureAwait(false), x => x.Select(c => c.ToModel()).ToList());

            public async Task<Response<List<IQuickSearch>>> QuickSearchAsync(string search, QuickSearchType type)
            {
                var searchType = type == QuickSearchType.Anime
                    ? ShindenAPI.Queries.Search.TitleQuick.SearchType.Anime
                    : ShindenAPI.Queries.Search.TitleQuick.SearchType.Manga;

                return Wrap(await _api.Search.TitlesAsync(search, searchType, SearchLimit).ConfigureAwait(false),
                    x => x.Select(t => t.ToModel()).ToList());
            }
        }

        public class UserModule
        {
            private readonly ApiClient _api;

            internal UserModule(ApiClient api) => _api = api;

            public async Task<Response<IUserInfo>> GetAsync(ulong id)
                => Wrap(await _api.Users.InfoAsync(id).ConfigureAwait(false), x => x.ToModel());

            public async Task<Response<List<ICharacterInfoShort>>> GetFavCharactersAsync(ulong userId)
                => Wrap(await _api.Users.FavouriteCharactersAsync(userId).ConfigureAwait(false), x => x.Select(c => c.ToModel()).ToList());

            public async Task<Response<List<ILastWatched>>> GetLastWatchedAsync(ulong userId, uint limit = 5)
                => Wrap(await _api.Users.LastWatchedAsync(userId, limit).ConfigureAwait(false), x => x.Select(e => e.ToModel()).ToList());

            public async Task<Response<List<ILastReaded>>> GetLastReadedAsync(ulong userId, uint limit = 5)
                => Wrap(await _api.Users.LastReadAsync(userId, limit).ConfigureAwait(false), x => x.Select(e => e.ToModel()).ToList());
        }

        public class ExperimentalModule
        {
            private readonly ApiClient _api;

            internal ExperimentalModule(ApiClient api) => _api = api;

            public async Task<Response<List<ICharacterInfoTitle>>> GetAllCharactersFatAsync()
                => Wrap(await _api.Characters.FlatListAsync().ConfigureAwait(false), ToPool);

            public async Task<Response<List<ICharacterInfoTitle>>> GetAllCharactersFromAnimeFatAsync()
                => Wrap(await _api.Characters.AnimeFlatListAsync().ConfigureAwait(false), ToPool);

            public async Task<Response<List<ICharacterInfoTitle>>> GetAllCharactersFromMangaFatAsync()
                => Wrap(await _api.Characters.MangaFlatListAsync().ConfigureAwait(false), ToPool);

            public async Task<Response<Models.Entities.CurrentSeason>> GetCurrentSeasonAsync()
                => Wrap(await _api.Episodes.CurrentSeasonAsync().ConfigureAwait(false), ToSeason);

            public async Task<Response<Models.Entities.CurrentSeason>> GetSeasonAsync(int year, AnimeSeason season)
                => Wrap(await _api.Episodes.CurrentSeasonAsync(year, ToApi(season)).ConfigureAwait(false), ToSeason);

            private static Models.Entities.CurrentSeason ToSeason(ShindenAPI.Models.SeasonalAnimeInfo x)
            {
                var season = FromApi(x.Season?.Value);
                return new Models.Entities.CurrentSeason
                {
                    Titles = x.TitleIds ?? new List<ulong>(),
                    Characters = x.CharacterIds ?? new List<ulong>(),
                    Year = season.HasValue && x.Season.Year > 0 ? (int)x.Season.Year : null,
                    Season = season.HasValue && x.Season.Year > 0 ? season : null,
                    UpdatedAt = x.UpdatedAt,
                    CharacterTitles = x.CharacterTitles?.Where(c => c != null)
                        .Select(c => new Models.Entities.SeasonCharacterTitle { CharacterId = c.CharacterId, TitleId = c.TitleId }).ToList()
                        ?? new List<Models.Entities.SeasonCharacterTitle>(),
                };
            }

            private static AnimeSeason? FromApi(ShindenAPI.Queries.V1.Season? season) => season switch
            {
                ShindenAPI.Queries.V1.Season.Winter => AnimeSeason.Winter,
                ShindenAPI.Queries.V1.Season.Spring => AnimeSeason.Spring,
                ShindenAPI.Queries.V1.Season.Summer => AnimeSeason.Summer,
                ShindenAPI.Queries.V1.Season.Fall => AnimeSeason.Autumn,
                _ => null,
            };

            private static ShindenAPI.Queries.V1.Season ToApi(AnimeSeason season) => season switch
            {
                AnimeSeason.Winter => ShindenAPI.Queries.V1.Season.Winter,
                AnimeSeason.Spring => ShindenAPI.Queries.V1.Season.Spring,
                AnimeSeason.Summer => ShindenAPI.Queries.V1.Season.Summer,
                _ => ShindenAPI.Queries.V1.Season.Fall,
            };

            private static List<ICharacterInfoTitle> ToPool(List<ShindenAPI.Models.CharacterFlat> list)
                => list.Where(x => x != null).Select(x => x.ToModel()).ToList();
        }
    }
}
