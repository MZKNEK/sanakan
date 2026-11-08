#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Discord.Commands;
using Discord.WebSocket;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services.Cache;
using Sanakan.Services.PocketWaifu;
using Sanakan.Services.Session;
using Sanakan.Services.Session.Models;
using Sanakan.Services.Time;
using Sanakan.ShindenCompat;
using Sanakan.ShindenCompat.Models;

namespace Sanakan.Services
{
    public enum UrlParsingError
    {
        None, InvalidUrl, InvalidUrlForum
    }

    public class Shinden
    {
        private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan InfoMaxAge = TimeSpan.FromHours(8);
        private static readonly TimeSpan InfoKeepFor = TimeSpan.FromDays(1);
        private static readonly TimeSpan PoolMaxAge = TimeSpan.FromDays(1);
        private static readonly TimeSpan UserNameMaxAge = TimeSpan.FromHours(24);
        private static readonly TimeSpan UserNameKeepFor = TimeSpan.FromDays(30);

        private readonly KeyedCache<ulong, ICharacterInfo> _characters;
        private readonly KeyedCache<ulong, List<IRelation>> _titleCharacters;
        private readonly KeyedCache<ulong, ITitleInfo> _titles;
        private readonly KeyedCache<ulong, string> _userNames;
        private readonly CachedValue<List<PoolCharacter>> _animePool;
        private readonly CachedValue<List<PoolCharacter>> _mangaPool;
        private readonly CachedValue<List<PoolCharacter>> _allPool;
        private readonly SeasonStore _seasons;
        private Timer _seasonTimer;

        private Dictionary<string, string> _customNames = new Dictionary<string, string>
        {
            {"fate/loli",   "Fate/kaleid Liner Prisma Illya"},
            {"fateloli",    "Fate/kaleid Liner Prisma Illya"},
            {"milfsekai",   "Tsuujou Kougeki ga Zentai Kougeki de Ni-kai"},
            {"milfisekai",  "Tsuujou Kougeki ga Zentai Kougeki de Ni-kai"},
        };

        private ShindenClient _shClient;
        private SessionManager _session;
        private ImageProcessing _img;
        private ISystemTime _time;
        private ILogger _logger;

        // without cacheDir nothing is kept between restarts
        public Shinden(ShindenClient client, SessionManager session, ImageProcessing img, ILogger logger, ISystemTime time, string cacheDir = null)
        {
            _logger = logger;
            _shClient = client;
            _session = session;
            _time = time;
            _img = img;

            string PathOf(string name) => cacheDir == null ? null : Path.Combine(cacheDir, name);

            _characters = new KeyedCache<ulong, ICharacterInfo>(InfoMaxAge, RetryAfter, InfoKeepFor);
            _titleCharacters = new KeyedCache<ulong, List<IRelation>>(InfoMaxAge, RetryAfter, InfoKeepFor);
            _titles = new KeyedCache<ulong, ITitleInfo>(InfoMaxAge, RetryAfter, InfoKeepFor);
            _userNames = new KeyedCache<ulong, string>(UserNameMaxAge, RetryAfter, UserNameKeepFor, PathOf("usernames.json"), logger);
            _animePool = new CachedValue<List<PoolCharacter>>(PoolMaxAge, RetryAfter, PathOf("pool-anime.json"), logger);
            _mangaPool = new CachedValue<List<PoolCharacter>>(PoolMaxAge, RetryAfter, PathOf("pool-manga.json"), logger);
            _allPool = new CachedValue<List<PoolCharacter>>(PoolMaxAge, RetryAfter, PathOf("pool-all.json"), logger);
            _seasons = new SeasonStore(PoolMaxAge, RetryAfter, PathOf("seasons"), logger);
        }

        // the store refreshes at most once per PoolMaxAge, the timer only makes sure that happens even without players
        public void StartSeasonRefresh(TimeSpan interval)
        {
            _seasonTimer = new Timer(_ => _ = RefreshSeasonAsync(), null, TimeSpan.FromMinutes(1), interval);
        }

        private async Task RefreshSeasonAsync()
        {
            try
            {
                await GetCurrentSeasonAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Shinden: season refresh: {ex.Message}");
            }
        }

        public Task SaveCacheAsync() => _userNames.SaveAsync(_time.Now());

        public async Task<ICharacterInfo> GetCharacterInfoAsync(ulong characterId)
        {
            try
            {
                return await _characters.GetAsync(characterId, async () =>
                {
                    var res = await _shClient.GetCharacterInfoAsync(characterId);
                    return res.IsSuccessStatusCode() ? res.Body : null;
                }, _time.Now());
            }
            catch (Exception ex)
            {
                _logger.LogError($"Shinden: GetCharacterInfo {characterId}: {ex.Message}");
                return null;
            }
        }

        public async Task<List<IRelation>> GetCharactersFromTitleAsync(ulong titleId)
        {
            try
            {
                return await _titleCharacters.GetAsync(titleId, async () =>
                {
                    var res = await _shClient.Title.GetCharactersAsync(titleId);
                    return res.IsSuccessStatusCode() ? res.Body : null;
                }, _time.Now());
            }
            catch (Exception ex)
            {
                _logger.LogError($"Shinden: GetCharactersFromTitle {titleId}: {ex.Message}");
                return null;
            }
        }

        public async Task<ITitleInfo> GetInfoFromTitleAsync(ulong titleId)
        {
            try
            {
                return await _titles.GetAsync(titleId, async () =>
                {
                    var res = await _shClient.Title.GetInfoAsync(titleId);
                    return res.IsSuccessStatusCode() ? res.Body : null;
                }, _time.Now());
            }
            catch (Exception ex)
            {
                _logger.LogError($"Shinden: GetInfoFromTitle {titleId}: {ex.Message}");
                return null;
            }
        }

        // null when the user does not exist, throws when shinden is unreachable and the name was never seen
        public Task<string> GetUserNameAsync(ulong shindenId)
        {
            return _userNames.GetAsync(shindenId, async () =>
            {
                var res = await _shClient.User.GetAsync(shindenId);
                return res.IsSuccessStatusCode() ? res.Body.Name : null;
            }, _time.Now());
        }

        public string GetCachedUserName(ulong shindenId) => _userNames.Peek(shindenId);

        private CachedValue<List<PoolCharacter>> GetPool(CharacterPoolType type) => type switch
        {
            CharacterPoolType.Anime => _animePool,
            CharacterPoolType.Manga => _mangaPool,
            _ => _allPool,
        };

        private Task<List<PoolCharacter>> GetPoolCharactersAsync(CharacterPoolType type)
        {
            return GetPool(type).GetAsync(async () =>
            {
                var res = type switch
                {
                    CharacterPoolType.Anime => await _shClient.Ex.GetAllCharactersFromAnimeFatAsync(),
                    CharacterPoolType.Manga => await _shClient.Ex.GetAllCharactersFromMangaFatAsync(),
                    _ => await _shClient.Ex.GetAllCharactersFatAsync(),
                };

                if (!res.IsSuccessStatusCode())
                    throw new Exception(res.Code.ToString());

                return res.Body.IsNullOrEmpty() ? null : res.Body.Select(x => new PoolCharacter(x)).ToList();
            }, _time.Now());
        }

        public async Task<(ICharacterInfoTitle CharInfo, string Code)> GetRandomCharacterAsync(CharacterPoolType type)
        {
            var characters = await GetPoolCharactersAsync(type);
            if (characters.IsNullOrEmpty())
                return (null, GetPool(type).LastError);

            return (Fun.GetOneRandomFrom(characters), HttpStatusCode.OK.ToString());
        }

        public async Task<ICharacterInfoTitle> FindInPoolAsync(CharacterPoolType type, ulong id)
        {
            var characters = await GetPoolCharactersAsync(type);
            return characters?.FirstOrDefault(x => x.Id == id);
        }

        public Task<SeasonSnapshot> GetCurrentSeasonAsync()
            => _seasons.GetCurrentAsync(FetchSeasonAsync, _time.Now());

        public SeasonSnapshot GetSeason(string key) => _seasons.Get(key);

        public SeasonStore Seasons => _seasons;

        public Task<SeasonSnapshot> RefreshSeasonNowAsync()
            => _seasons.GetCurrentAsync(FetchSeasonAsync, _time.Now(), force: true);

        public Task<(SeasonSnapshot Season, string Error)> FillSeasonAsync(int year, AnimeSeason season)
        {
            return _seasons.FillAsync(async () =>
            {
                var res = await _shClient.Ex.GetSeasonAsync(year, season);
                if (!res.IsSuccessStatusCode())
                    throw new Exception(res.Code.ToString());

                var value = res.Body;
                if (!value.Year.HasValue || !value.Season.HasValue)
                {
                    value.Year = year;
                    value.Season = season;
                }
                else if (value.Year != year || value.Season != season)
                {
                    throw new Exception($"Shinden returned {SeasonSnapshot.KeyOf(value.Year.Value, value.Season.Value)}");
                }
                return value;
            }, _time.Now());
        }

        private async Task<ShindenCompat.Models.Entities.CurrentSeason> FetchSeasonAsync()
        {
            var res = await _shClient.Ex.GetCurrentSeasonAsync();
            if (!res.IsSuccessStatusCode())
                throw new Exception(res.Code.ToString());

            return res.Body;
        }

        public UrlParsingError ParseUrlToShindenId(string url, out ulong shindenId)
        {
            shindenId = 0;
            var splited = url.Split('/');
            bool http = splited[0].Equals("https:") || splited[0].Equals("http:");
            int toChek = http ? 2 : 0;

            if (splited.Length < (toChek == 2 ? 5 : 3))
                return UrlParsingError.InvalidUrl;

            if (splited[toChek].Equals("shinden.pl") || splited[toChek].Equals("www.shinden.pl"))
            {
                if(splited[++toChek].Equals("user") || splited[toChek].Equals("animelist") || splited[toChek].Equals("mangalist"))
                {
                    var data = splited[++toChek].Split('-');
                    if (ulong.TryParse(data[0], out shindenId))
                        return UrlParsingError.None;
                }
            }

            if (splited[toChek].Equals("forum.shinden.pl") || splited[toChek].Equals("www.forum.shinden.pl"))
                return UrlParsingError.InvalidUrlForum;

            return UrlParsingError.InvalidUrl;
        }

        public async Task<List<CharacterWithTitle>> GetTitlesForCharactersAsync(IEnumerable<IPersonSearch> characters)
        {
            // ograniczamy równoległość, aby nie zalać API Shindena
            using (var gate = new SemaphoreSlim(5))
            {
                var tasks = characters.Select(async ch =>
                {
                    await gate.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        var entity = new CharacterWithTitle
                        {
                            Character = ch,
                            Title = string.Empty
                        };

                        var info = await GetCharacterInfoAsync(ch.Id).ConfigureAwait(false);
                        if (info != null)
                        {
                            entity.Title = info?.Relations?.OrderBy(x => x.Id)?.FirstOrDefault()?.Title ?? string.Empty;
                        }

                        return entity;
                    }
                    finally
                    {
                        gate.Release();
                    }
                }).ToList();

                return new List<CharacterWithTitle>(await Task.WhenAll(tasks).ConfigureAwait(false));
            }
        }

        public string[] GetSearchResponse(IEnumerable<object> list, string title)
        {
            string temp = "";
            int messageNr = 0;
            var toSend = new List<string> { $"{title}\n```ini\n" };
            int i = 0;

            foreach (var item in list)
            {
                temp += $"[{++i}] {item}\n";
                if (temp.Length > 1800)
                {
                    toSend[messageNr] += "\n```";
                    toSend.Add($"```ini\n[{i}] {item}\n");
                    messageNr++;
                    temp = "";
                }
                else toSend[messageNr] += $"[{i}] {item}\n";
            }
            toSend[messageNr] += "```\nNapisz `koniec`, aby zamknąć menu.";

            return toSend.ToArray();
        }

        public async Task SendSearchInfoAsync(SocketCommandContext context, string title, QuickSearchType type)
        {
            var customTitle = title.ToLower().Replace(" ", "");
            if (_customNames.ContainsKey(customTitle))
                title = _customNames[customTitle];

            var session = new SearchSession(context.User, _shClient, this);
            if (_session.SessionExist(session)) return;

            var res = await QuickSearchAsync(title, type);
            if (!res.Item1)
            {
                await context.Channel.SendMessageAsync("", false, GetResponseFromSearchCode(res.Item2).ToEmbedMessage(EMType.Error).Build());
                return;
            }

            var list = res.Item3;
            var toSend = GetSearchResponse(list, "Wybierz tytuł, który chcesz wyświetlić poprzez wpisanie numeru odpowiadającemu mu na liście.");

            if (list.Count == 1)
            {
                var info = await GetInfoFromTitleAsync(list.First().Id);
                await context.Channel.SendMessageAsync("", false, info.ToEmbed());
            }
            else
            {
                session.SList = list;
                await SendSearchResponseAsync(context, toSend, session);
            }
        }

        public async Task SendSearchResponseAsync(SocketCommandContext context, string[] toSend, SearchSession session)
        {
            var msg = new List<Discord.Rest.RestUserMessage>();
            for (int index = 0; index < toSend.Length; index++)
                if (toSend[index] != null) msg.Add(await context.Channel.SendMessageAsync(toSend[index]));

            session.Messages = msg.ToArray();
            await _session.TryAddSession(session);
        }

        public string GetResponseFromSearchCode(HttpStatusCode code)
        {
            switch (code)
            {
                case HttpStatusCode.NotFound:
                    return "Brak wyników!";

                default:
                    return "Brak połączenia z Shindenem! Spróbuj ponownie za chwilę.";
            }
        }

        public async Task<Stream> GetSiteStatisticAsync(ulong shindenId, SocketGuildUser user)
        {
            var (info, resLR, resLW) = await GetSiteStatisticDataAsync(shindenId);

            using (var image = await _img.GetSiteStatisticAsync(info, GetSiteStatisticColor(user), resLR, resLW))
            {
                return image.ToWebpStream();
            }
        }

        public async Task<List<(string Name, Stream Image)>> GetSiteStatisticVariantsAsync(ulong shindenId, SocketGuildUser user)
        {
            var (info, resLR, resLW) = await GetSiteStatisticDataAsync(shindenId);
            var color = GetSiteStatisticColor(user);

            var variants = new[] { ("pelny", true, true), ("anime", true, false), ("manga", false, true), ("pusty", false, false) };
            var images = new List<(string Name, Stream Image)>();
            foreach (var (name, anime, manga) in variants)
            {
                using var image = await _img.GetSiteStatisticAsync(new PartialUserInfo(info, anime, manga),
                    color, manga ? resLR : null, anime ? resLW : null);

                images.Add((name, image.ToWebpStream()));
            }
            return images;
        }

        internal async Task<(IUserInfo Info, List<ILastReaded> LastReaded, List<ILastWatched> LastWatched)> GetSiteStatisticDataAsync(ulong shindenId)
        {
            // the requests do not depend on each other and none of them throws, failures are logged and give null
            var info = GetUserInfoAsync(shindenId);
            var lastReaded = GetUserLastReadedAsync(shindenId);
            var lastWatched = GetUserLastWatchedAsync(shindenId);
            await Task.WhenAll(info, lastReaded, lastWatched);

            var userInfo = await info;
            if (userInfo.Info is null) throw new Exception(userInfo.Code.ToString());

            return (userInfo.Info, await lastReaded, await lastWatched);
        }

        private static Discord.Color GetSiteStatisticColor(SocketGuildUser user)
            => user.Roles.OrderByDescending(x => x.Position).FirstOrDefault()?.Color ?? Discord.Color.DarkerGrey;

        private sealed class PartialUserInfo : IUserInfo
        {
            private readonly IUserInfo _info;

            public PartialUserInfo(IUserInfo info, bool anime, bool manga)
            {
                _info = info;
                var stats = info.ListStats;
                ListStats = new ShindenCompat.Models.Entities.ListStats
                {
                    AnimeStatus = anime ? stats?.AnimeStatus : null,
                    AnimeTime = anime ? stats?.AnimeTime : null,
                    AnimeMeanScore = anime ? stats?.AnimeMeanScore : null,
                    EpisodesCount = anime ? stats?.EpisodesCount : null,
                    MangaStatus = manga ? stats?.MangaStatus : null,
                    MangaTime = manga ? stats?.MangaTime : null,
                    MangaMeanScore = manga ? stats?.MangaMeanScore : null,
                    ChaptersCount = manga ? stats?.ChaptersCount : null,
                };
            }

            public ulong Id => _info.Id;
            public string Name => _info.Name;
            public string AvatarUrl => _info.AvatarUrl;
            public string Rank => _info.Rank;
            public ulong? ForumId => _info.ForumId;
            public IListStats ListStats { get; }
        }

        private async Task<List<ILastWatched>> GetUserLastWatchedAsync(ulong shindenId)
        {
            List<ILastWatched> lw = null;
            try
            {
                var response = await _shClient.User.GetLastWatchedAsync(shindenId, 50);
                if (response.IsSuccessStatusCode()) lw = response.Body;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Shinden: GetLastWatched {shindenId}: {ex.Message}");
            }
            return lw;
        }

        private async Task<List<ILastReaded>> GetUserLastReadedAsync(ulong shindenId)
        {
            List<ILastReaded> lr = null;
            try
            {
                var response = await _shClient.User.GetLastReadedAsync(shindenId, 50);
                if (response.IsSuccessStatusCode()) lr = response.Body;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Shinden: GetLastReaded {shindenId}: {ex.Message}");
            }
            return lr;
        }

        private async Task<(IUserInfo Info, HttpStatusCode Code)> GetUserInfoAsync(ulong shindenId)
        {
            IUserInfo uInfo = null;
            var code = HttpStatusCode.InternalServerError;
            try
            {
                var response = await _shClient.User.GetAsync(shindenId);
                code = response.Code;
                if (response.IsSuccessStatusCode())
                {
                    uInfo = response.Body;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Shinden: GetUserInfo {shindenId}: {ex.Message}");
            }
            return (uInfo, code);
        }

        private async Task<(bool, HttpStatusCode, List<IQuickSearch>)> QuickSearchAsync(string title, QuickSearchType type)
        {
            try
            {
                var res = await _shClient.Search.QuickSearchAsync(title, type);
                return (res.IsSuccessStatusCode(), res.Code, res.Body);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Shinden: QuickSearch '{title}': {ex.Message}");
            }
            return (false, HttpStatusCode.RequestTimeout, null);
        }
    }
}