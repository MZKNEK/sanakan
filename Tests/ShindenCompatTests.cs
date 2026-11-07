using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Sanakan.Config.Model;
using Sanakan.ShindenCompat;
using Sanakan.ShindenCompat.Models;
using Xunit;

namespace Artifacts
{
    public class ShindenCompatTests
    {
        private class StubHandler : HttpMessageHandler
        {
            public List<HttpRequestMessage> Requests { get; } = new List<HttpRequestMessage>();
            public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; }

            public static HttpResponseMessage Json(string json, HttpStatusCode code = HttpStatusCode.OK)
                => new HttpResponseMessage(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Requests.Add(request);
                return Task.FromResult(Respond(request));
            }
        }

        private readonly StubHandler _stub = new StubHandler();

        private ShindenClient Create(string apiToken = "shk_new", string baseUri = null) => ShindenClient.Create(new ConfigShinden
        {
            Token = "old-php-key",
            ApiToken = apiToken,
            UserAgent = "tests",
            Marmolade = "X-Marmolade",
            BaseUri = baseUri,
        }, null, _stub, TimeSpan.FromSeconds(5));

        private void Respond(string json, HttpStatusCode code = HttpStatusCode.OK)
            => _stub.Respond = _ => StubHandler.Json(json, code);

        private const string Character = @"{
            ""character_id"": ""15"", ""first_name"": ""Lelouch"", ""last_name"": ""vi Britannia &amp; Lamperouge"",
            ""is_real"": ""1"", ""gender"": ""m"", ""age"": ""18"", ""height"": ""178 cm"", ""weight"": """", ""bloodtype"": ""A"",
            ""picture_artifact_id"": ""1234"",
            ""bio"": { ""character_biography_id"": ""3"", ""character_id"": ""15"", ""biography"": ""[b]Książę[/b] Brytanii"", ""lang"": ""pl"" },
            ""points"": [ { ""user_id"": ""2"", ""name"": ""Sniku"", ""points"": ""12.5"" } ],
            ""relations"": [
                { ""many_id"": ""9"", ""title_id"": ""100"", ""character_id"": ""15"", ""title"": ""Code Geass: Hangyaku no Lelouch R2"", ""role"": ""main"", ""seiyuu_lang"": ""jp"" },
                { ""many_id"": ""7"", ""title_id"": ""99"", ""character_id"": ""15"", ""title"": ""Code Geass"", ""role"": ""main"" }
            ],
            ""pictures"": [
                { ""artifact_type"": ""image_picture"", ""artifact_id"": ""501"", ""is_18plus"": ""0"" },
                { ""artifact_type"": ""image_picture"", ""artifact_id"": ""502"", ""is_18plus"": ""1"" },
                { ""artifact_type"": ""video"", ""artifact_id"": ""503"", ""is_18plus"": ""0"" }
            ]
        }";

        [Fact]
        public async Task CharacterInfoUsesCompatRouteWithNewToken()
        {
            Respond(Character);

            var res = await Create().GetCharacterInfoAsync(15);

            Assert.True(res.IsSuccessStatusCode());
            var request = _stub.Requests.Single();
            Assert.Equal("/compat/php-api/character/15", request.RequestUri.AbsolutePath);
            Assert.Contains("relations=1", request.RequestUri.Query);
            Assert.Contains("pictures=1", request.RequestUri.Query);
            Assert.DoesNotContain("api_key", request.RequestUri.Query);
            Assert.Equal("Bearer", request.Headers.Authorization.Scheme);
            Assert.Equal("shk_new", request.Headers.Authorization.Parameter);
            Assert.StartsWith("tests", request.Headers.UserAgent.ToString());
        }

        [Fact]
        public async Task CharacterInfoIsMappedLikeOldLibrary()
        {
            Respond(Character);

            var info = (await Create().GetCharacterInfoAsync(15)).Body;

            Assert.Equal(15ul, info.Id);
            Assert.Equal("Lelouch vi Britannia & Lamperouge", info.ToString());
            Assert.Equal(Sex.Male, info.Gender);
            Assert.True(info.IsReal);
            Assert.Equal("178 cm", info.Height);
            Assert.Equal("", info.Weight);
            Assert.True(info.HasImage);
            Assert.Equal("http://cdn.shinden.eu/cdn1/images/genuine/1234.jpg", info.PictureUrl);
            Assert.Equal("https://shinden.pl/character/15", info.CharacterUrl);
            Assert.Equal("Książę Brytanii", info.Biography.Content);
            Assert.Equal("Sniku", info.Points.Single().Name);
            Assert.Equal(12.5, info.Points.Single().Points);
            Assert.Equal("Code Geass", info.Relations.OrderBy(x => x.Id).First().Title);
            Assert.Equal(15ul, info.Relations.First().CharacterId);
            Assert.Equal(Language.Japanese, info.Relations.First().Language);
            Assert.Equal(new[] { 501ul, 502ul }, info.Pictures.Select(x => x.PictureId));
            Assert.Equal(new[] { false, true }, info.Pictures.Select(x => x.Is18Plus));
        }

        [Fact]
        public async Task CharacterWithoutPictureUsesPlaceholder()
        {
            Respond(@"{ ""character_id"": ""16"", ""first_name"": ""C.C."", ""last_name"": null, ""picture_artifact_id"": null }");

            var info = (await Create().GetCharacterInfoAsync(16)).Body;

            Assert.False(info.HasImage);
            Assert.Equal("", info.LastName);
            Assert.Empty(info.Relations);
            Assert.Null(info.Biography);
        }

        [Fact]
        public async Task OldKeyIsUsedForNewApiWhenNoTokenIsConfigured()
        {
            Respond(Character);

            await Create(apiToken: null).GetCharacterInfoAsync(15);

            Assert.Equal("old-php-key", _stub.Requests.Single().Headers.Authorization.Parameter);
        }

        [Fact]
        public async Task AnimeTitleInfoIsMapped()
        {
            Respond(@"{ ""title"": {
                ""title_id"": ""100"", ""type"": ""Anime"", ""title"": ""Code Geass &amp; R2"", ""title_status"": ""Finished Airing"",
                ""cover_artifact_id"": ""777"", ""rating_total_sum"": ""90"", ""rating_total_cnt"": ""10"",
                ""premiere_date"": ""1175385600"", ""premiere_precision"": ""3"", ""finish_date"": null,
                ""description"": { ""description_id"": ""1"", ""description"": ""Opis [i]serii[/i]"", ""lang_code"": ""pl"" },
                ""anime"": { ""anime_type"": ""TV"", ""episodes"": ""25"", ""episode_time"": ""24"", ""rating_music_sum"": ""1"", ""rating_music_cnt"": ""1"" },
                ""title_other"": [ { ""title"": ""Code Geass R2"", ""lang"": ""en"", ""title_type"": ""official"" } ],
                ""tags"": {
                    ""genre"": { ""name"": ""Gatunki"", ""items"": [ { ""tag_id"": ""1"", ""tag_name"": ""Mecha"", ""national_name"": ""Mecha PL"" } ] },
                    ""studio"": { ""name"": ""Studio"", ""items"": [ { ""tag_id"": ""2"", ""tag_name"": ""Sunrise"", ""national_name"": null } ] }
                }
            } }");

            var res = await Create().Title.GetInfoAsync(100);

            Assert.Equal("/compat/php-api/title/100/info", _stub.Requests.Single().RequestUri.AbsolutePath);
            var info = Assert.IsAssignableFrom<IAnimeTitleInfo>(res.Body);
            Assert.Equal("Code Geass & R2", info.Title);
            Assert.Equal(AnimeType.Tv, info.Type);
            Assert.Equal(AnimeStatus.FinishedAiring, info.Status);
            Assert.Equal(25ul, info.EpisodesCount);
            Assert.Equal(9.0, info.TotalRating);
            Assert.Equal("Opis serii", info.Description.Content);
            Assert.Equal("http://cdn.shinden.eu/cdn1/images/genuine/777.jpg", info.CoverUrl);
            Assert.Equal("https://shinden.pl/titles/100", info.AnimeUrl);
            Assert.Equal("Code Geass R2", string.Join(", ", info.AlternativeTitles));
            Assert.Equal(new[] { "Studio", "Gatunki" }, info.TagCategories.Select(x => x.Name));
            Assert.Equal("Sunrise", string.Join(", ", info.TagCategories[0].Tags));
            Assert.Equal("Mecha PL", string.Join(", ", info.TagCategories[1].Tags));
            Assert.True(info.StartDate.IsSpecified);
            Assert.Equal(new DateTime(2007, 4, 1), info.StartDate.Date.Date);
            Assert.False(info.FinishDate.IsSpecified);
        }

        [Fact]
        public async Task MangaTitleInfoIsMapped()
        {
            Respond(@"{ ""title"": { ""title_id"": ""5"", ""type"": ""Manhwa"", ""title"": ""Solo"", ""title_status"": ""Publishing"",
                ""manga"": { ""chapters"": ""200"", ""volumes"": null }, ""tags"": [] } }");

            var info = Assert.IsAssignableFrom<IMangaTitleInfo>((await Create().Title.GetInfoAsync(5)).Body);

            Assert.Equal(MangaType.Manhwa, info.Type);
            Assert.Equal(MangaStatus.Publishing, info.Status);
            Assert.Equal(200ul, info.ChaptersCount);
            Assert.Equal("https://shinden.pl/manga/5", info.MangaUrl);
            Assert.Empty(info.TagCategories);
            Assert.Null(info.Description);
        }

        [Fact]
        public async Task TitleCharactersAreRelations()
        {
            Respond(@"{ ""relations"": [ { ""many_id"": ""1"", ""character_id"": ""15"", ""first_name"": ""Lelouch"", ""title"": ""Code Geass"" },
                { ""many_id"": ""2"", ""character_id"": null, ""staff_id"": ""3"", ""title"": ""Code Geass"" } ] }");

            var res = await Create().Title.GetCharactersAsync(99);

            Assert.Equal("/compat/php-api/title/99/characters", _stub.Requests.Single().RequestUri.AbsolutePath);
            Assert.Equal(new ulong?[] { 15, 0 }, res.Body.Select(x => x.CharacterId));
        }

        [Fact]
        public async Task UserInfoListStatsAreMapped()
        {
            Respond(@"{ ""user_id"": ""42"", ""name"": ""Sniku"", ""rank"": ""Admin"", ""vb_id"": ""7"", ""avatar"": ""3"",
                ""watched_cnt"": ""1200"", ""chapters_cnt"": ""340"",
                ""watched_status"": { ""in progress"": ""2"", ""completed"": ""50"", ""dropped"": ""1"", ""skip"": ""0"", ""plan"": ""10"", ""hold"": ""3"", ""_total"": ""66"" },
                ""readed_status"": { ""completed"": ""5"" },
                ""watch_time"": { ""y"": ""0"", ""n"": ""2"", ""d"": ""3"", ""h"": ""4"", ""m"": ""5"" },
                ""mean_anime_score"": { ""mean_score"": ""7.5"", ""scores_cnt"": ""40"" } }");

            var res = await Create().User.GetAsync(42);

            Assert.Equal("/compat/php-api/user/42/info", _stub.Requests.Single().RequestUri.AbsolutePath);
            var user = res.Body;
            Assert.Equal("Sniku", user.Name);
            Assert.Equal("Admin", user.Rank);
            Assert.Equal(7ul, user.ForumId);
            Assert.Equal("http://cdn.shinden.eu/cdn1/avatars/225x350/42.jpg?v3", user.AvatarUrl);
            Assert.Equal(1200ul, user.ListStats.EpisodesCount);
            Assert.Equal(340ul, user.ListStats.ChaptersCount);
            Assert.Equal(66ul, user.ListStats.AnimeStatus.Total);
            Assert.Equal(2ul, user.ListStats.AnimeStatus.InProgress);
            Assert.Equal(5ul, user.ListStats.MangaStatus.Total);
            Assert.Equal(5ul + 4 * 60 + 3 * 24 * 60 + 2 * 31 * 24 * 60, user.ListStats.AnimeTime.TotalMinutes);
            Assert.Null(user.ListStats.MangaTime);
            Assert.Equal(7.5, user.ListStats.AnimeMeanScore.Rating);
            Assert.Equal(40ul, user.ListStats.AnimeMeanScore.Count);
            Assert.Equal(0ul, user.ListStats.MangaMeanScore.Count);
        }

        [Fact]
        public async Task HistoryIsMapped()
        {
            Respond(@"[ { ""title_id"": ""100"", ""episode_id"": ""8"", ""episode_no"": ""3"", ""episodes"": ""25"", ""title"": ""Code Geass"", ""cover_artifact_id"": ""777"" } ]");
            var watched = (await Create().User.GetLastWatchedAsync(42)).Body.Single();

            Assert.Equal("/compat/php-api/user/42/last_view", _stub.Requests.Last().RequestUri.AbsolutePath);
            Assert.Contains("limit=5", _stub.Requests.Last().RequestUri.Query);
            Assert.Equal("Code Geass: 3/25", watched.ToString());
            Assert.Equal("http://cdn.shinden.eu/cdn1/images/genuine/777.jpg", watched.AnimeCoverUrl);

            Respond(@"[ { ""title_id"": ""5"", ""chapter_id"": ""9"", ""chapter_no"": ""12"", ""chapters"": ""200"", ""title"": ""Solo"" } ]");
            var read = (await Create().User.GetLastReadedAsync(42)).Body.Single();

            Assert.Equal("/compat/php-api/user/42/last_read", _stub.Requests.Last().RequestUri.AbsolutePath);
            Assert.Equal("Solo: 12/200", read.ToString());
        }

        [Fact]
        public async Task SearchesUseNewApi()
        {
            var client = Create();

            Respond(@"[ { ""user_id"": 42, ""name"": ""Sniku"", ""rank"": ""Admin"", ""avatar"": true } ]");
            var users = await client.Search.UserAsync("Sniku");
            Assert.Equal("/v1/search/users", _stub.Requests.Last().RequestUri.AbsolutePath);
            Assert.Equal(42ul, users.Body.Single().Id);
            Assert.Equal("http://cdn.shinden.eu/cdn1/avatars/225x350/42.jpg", users.Body.Single().AvatarUrl);

            Respond(@"[ { ""user_id"": 43, ""name"": ""Bez"", ""rank"": ""User"", ""avatar"": false } ]");
            Assert.Equal("http://cdn.shinden.eu/cdn1/other/placeholders/user/100x100.jpg", (await client.Search.UserAsync("Bez")).Body.Single().AvatarUrl);

            Respond(@"[ { ""character_id"": 15, ""first_name"": ""Lelouch"", ""last_name"": ""Lamperouge"", ""picture"": 1234 } ]");
            var characters = await client.Search.CharacterAsync("Lelouch");
            Assert.Equal("/v1/search/characters", _stub.Requests.Last().RequestUri.AbsolutePath);
            Assert.Equal("Lelouch Lamperouge", characters.Body.Single().ToString());
            Assert.Equal("http://cdn.shinden.eu/cdn1/images/genuine/1234.jpg", characters.Body.Single().PictureUrl);

            Respond(@"[ { ""title_id"": 100, ""title"": ""Code Geass &amp; R2"", ""type"": ""TV"", ""cover"": 777 } ]");
            var titles = await client.Search.QuickSearchAsync("Code Geass", QuickSearchType.Anime);
            Assert.Equal("/v1/search/titles", _stub.Requests.Last().RequestUri.AbsolutePath);
            Assert.Contains("types=Anime", _stub.Requests.Last().RequestUri.Query);
            Assert.Equal("Code Geass & R2", titles.Body.Single().Title);
            Assert.Equal("http://cdn.shinden.eu/cdn1/images/genuine/777.jpg", titles.Body.Single().CoverUrl);
        }

        [Fact]
        public async Task LegacyOperationsUseOldKeyAndMarmolade()
        {
            Respond(@"[ { ""character_id"": ""15"", ""first_name"": ""Lelouch"", ""last_name"": ""Lamperouge"", ""user_id"": ""42"" } ]");

            var res = await Create().User.GetFavCharactersAsync(42);

            var request = _stub.Requests.Single();
            Assert.Equal("/api/user/42/fav-chars", request.RequestUri.AbsolutePath);
            Assert.Contains("api_key=old-php-key", request.RequestUri.Query);
            Assert.Null(request.Headers.Authorization);
            Assert.Equal("marmolada", request.Headers.GetValues("X-Marmolade").Single());
            Assert.Equal(15ul, res.Body.Single().Id);
        }

        [Fact]
        public async Task CharacterPoolsAreMapped()
        {
            Respond(@"[ { ""c"": 15, ""p"": 1234, ""f"": ""Lelouch"", ""l"": ""Lamperouge"", ""t"": ""Code Geass &amp; R2"", ""n"": 1 },
                { ""c"": 16, ""p"": 0, ""f"": ""C.C."", ""l"": """", ""t"": null, ""n"": 2 } ]");
            var client = Create();

            var all = await client.Ex.GetAllCharactersFatAsync();
            Assert.Equal("/compat/php-api/character/flat-list", _stub.Requests.Last().RequestUri.AbsolutePath);
            await client.Ex.GetAllCharactersFromAnimeFatAsync();
            Assert.Equal("/compat/php-api/character/flat-list-anime", _stub.Requests.Last().RequestUri.AbsolutePath);
            await client.Ex.GetAllCharactersFromMangaFatAsync();
            Assert.Equal("/api/character/flat-list-book", _stub.Requests.Last().RequestUri.AbsolutePath);

            Assert.Equal("Code Geass & R2", all.Body[0].TitleName);
            Assert.True(all.Body[0].HasImage);
            Assert.Equal("????", all.Body[1].TitleName);
            Assert.False(all.Body[1].HasImage);
        }

        [Fact]
        public async Task CurrentSeasonReturnsIds()
        {
            Respond(@"{ ""titles"": [ 1, 2 ], ""characters"": [ ""3"" ] }");
            var client = Create();

            var season = (await client.Ex.GetCurrentSeasonAsync()).Body;

            Assert.Equal(new ulong[] { 1, 2 }, season.Titles);
            Assert.Equal(new ulong[] { 3 }, season.Characters);
            Assert.Single(_stub.Requests);
            Assert.Equal("/compat/php-api/anime/current-season", _stub.Requests.Last().RequestUri.AbsolutePath);
            Assert.Null(season.Year);
            Assert.Null(season.Season);
            Assert.Null(season.UpdatedAt);
            Assert.Empty(season.CharacterTitles);
        }

        [Fact]
        public async Task Apply_ChangesAddressAndTokenForNextRequests()
        {
            Respond(Character);
            var client = Create();
            await client.GetCharacterInfoAsync(15);

            client.Apply(new ConfigShinden { Token = "old-php-key", ApiToken = "shk_changed", UserAgent = "tests", Marmolade = "X-Marmolade", BaseUri = "https://proxy.test/shinden/" });
            await client.GetCharacterInfoAsync(15);

            var before = _stub.Requests.First();
            var after = _stub.Requests.Last();
            Assert.Equal("shinden.pl", before.RequestUri.Host);
            Assert.Equal("shk_new", before.Headers.Authorization.Parameter);
            Assert.Equal("proxy.test", after.RequestUri.Host);
            Assert.StartsWith("/shinden/compat/php-api/character/15", after.RequestUri.AbsolutePath);
            Assert.Equal("shk_changed", after.Headers.Authorization.Parameter);
            Assert.Equal("https://proxy.test/shinden/", client.Active.Uri.ToString());
        }

        [Fact]
        public async Task Apply_InvalidConfig_KeepsWorkingWithThePreviousOne()
        {
            Respond(Character);
            var client = Create();

            Assert.ThrowsAny<Exception>(() => client.Apply(new ConfigShinden { Token = "old-php-key", ApiToken = "shk_x", UserAgent = null }));
            await client.GetCharacterInfoAsync(15);

            var request = _stub.Requests.Single();
            Assert.Equal("shinden.pl", request.RequestUri.Host);
            Assert.Equal("shk_new", request.Headers.Authorization.Parameter);
        }

        [Fact]
        public void Apply_LegacyAddressFollowsTheNewBaseUri()
        {
            var client = Create(baseUri: "https://old.test/api");

            client.Apply(new ConfigShinden { Token = "old-php-key", UserAgent = "tests", BaseUri = "https://new.test/" });

            Assert.Equal("new.test", client.Active.Uri.Host);
            Assert.Equal("new.test", client.Active.LegacyUri.Host);
        }

        [Fact]
        public async Task CurrentSeasonTagIsMappedAndFallBecomesAutumn()
        {
            Respond(@"{ ""season"": { ""year"": 2026, ""season"": ""fall"" }, ""updated_at"": ""2026-11-07 12:00:00"",
                ""titles"": [ 1 ], ""characters"": [ 3, 4 ], ""character_titles"": [ { ""character_id"": 3, ""title_id"": 1 } ] }");

            var season = (await Create().Ex.GetCurrentSeasonAsync()).Body;

            Assert.Equal(2026, season.Year);
            Assert.Equal(AnimeSeason.Autumn, season.Season);
            Assert.NotNull(season.UpdatedAt);
            var link = Assert.Single(season.CharacterTitles);
            Assert.Equal(3ul, link.CharacterId);
            Assert.Equal(1ul, link.TitleId);
        }

        [Fact]
        public async Task CurrentSeasonWithUnknownSeasonName_HasNoTag()
        {
            Respond(@"{ ""season"": { ""year"": 2026, ""season"": ""monsoon"" }, ""titles"": [ 1 ], ""characters"": [ 3 ] }");

            var season = (await Create().Ex.GetCurrentSeasonAsync()).Body;

            Assert.Null(season.Year);
            Assert.Null(season.Season);
            Assert.Equal(new ulong[] { 1 }, season.Titles);
        }

        [Theory]
        [InlineData(AnimeSeason.Summer, "summer")]
        [InlineData(AnimeSeason.Autumn, "fall")]
        public async Task SeasonByNameAsksForThatSeason(AnimeSeason requested, string name)
        {
            Respond($@"{{ ""season"": {{ ""year"": 2026, ""season"": ""{name}"" }}, ""titles"": [ 1 ], ""characters"": [ 3 ] }}");

            var season = (await Create().Ex.GetSeasonAsync(2026, requested)).Body;

            var uri = _stub.Requests.Single().RequestUri;
            Assert.Equal("/compat/php-api/anime/current-season", uri.AbsolutePath);
            Assert.Contains("2026", uri.Query);
            Assert.Contains(name, uri.Query);
            Assert.Equal(requested, season.Season);
        }

        [Fact]
        public async Task NewEpisodesAreMapped()
        {
            Respond(@"[ { ""episode_id"": ""8"", ""title_id"": ""100"", ""title"": ""Code Geass"", ""cover_artifact_id"": ""777"",
                ""episode_no"": ""3"", ""episode_time"": ""24"", ""sub_lang"": ""pl"", ""add_date"": ""2026-10-30 18:15:00"" } ]");

            var episode = (await Create().GetNewEpisodesAsync()).Body.Single();

            Assert.Equal("Code Geass (3)", episode.ToString());
            Assert.Equal("https://shinden.pl/epek/100/view/8", episode.EpisodeUrl);
            Assert.Equal(TimeSpan.FromMinutes(24), episode.EpisodeLength);
            Assert.Equal(Language.Polish, episode.SubtitlesLanguage);
            Assert.Equal(new DateTime(2026, 10, 30, 18, 15, 0), episode.AddDate);
        }

        [Fact]
        public async Task HealthUsesHealthEndpoint()
        {
            Respond("ok");
            var client = Create();

            Assert.True((await client.GetHealthAsync()).IsSuccessStatusCode());
            Assert.Equal("/health", _stub.Requests.Last().RequestUri.AbsolutePath);

            Respond("", HttpStatusCode.ServiceUnavailable);
            Assert.False((await client.GetHealthAsync()).IsSuccessStatusCode());
        }

        [Theory]
        [InlineData(HttpStatusCode.NotFound)]
        [InlineData(HttpStatusCode.BadRequest)]
        public async Task HttpErrorIsUnsuccessfulResponse(HttpStatusCode code)
        {
            Respond(@"{ ""status"": ""not found"" }", code);

            var res = await Create().User.GetAsync(1);

            Assert.False(res.IsSuccessStatusCode());
            Assert.Equal(code, res.Code);
            Assert.Null(res.Body);
        }

        [Fact]
        public async Task ParsingErrorIsUnsuccessfulResponse()
        {
            Respond("not json");

            var res = await Create().User.GetAsync(1);

            Assert.False(res.IsSuccessStatusCode());
            Assert.Equal(HttpStatusCode.InternalServerError, res.Code);
        }

        [Fact]
        public async Task TransportErrorThrowsLikeOldLibrary()
        {
            _stub.Respond = _ => throw new HttpRequestException("connection refused");

            await Assert.ThrowsAsync<ShindenException>(() => Create().User.GetAsync(1));
        }

        [Theory]
        [InlineData(null, "https://shinden.pl/", "https://shinden.pl/")]
        [InlineData("https://api.shinden.test/api/", "https://api.shinden.test/", "https://api.shinden.test/api/")]
        [InlineData("https://proxy.test/shinden/", "https://proxy.test/shinden/", "https://proxy.test/shinden/")]
        public void ShownUrisMatchClientConfiguration(string baseUri, string uri, string legacyUri)
        {
            var config = new ConfigShinden { BaseUri = baseUri };

            Assert.Equal(uri, ShindenClient.GetUri(config));
            Assert.Equal(legacyUri, ShindenClient.GetLegacyUri(config));
        }

        [Fact]
        public async Task ApiPathInBaseUriIsKeptOnlyForLegacy()
        {
            Respond("[]");
            var client = Create(baseUri: "https://api.shinden.test/api/");

            await client.Search.UserAsync("Sniku");
            Assert.Equal("https://api.shinden.test/v1/search/users", _stub.Requests.Last().RequestUri.GetLeftPart(UriPartial.Path));

            await client.User.GetFavCharactersAsync(1);
            Assert.Equal("https://api.shinden.test/api/user/1/fav-chars", _stub.Requests.Last().RequestUri.GetLeftPart(UriPartial.Path));
        }
    }
}
