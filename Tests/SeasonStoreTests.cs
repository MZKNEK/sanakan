using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Sanakan.Config.Model;
using Sanakan.Database.Models;
using Sanakan.Services.Cache;
using Sanakan.Services.PocketWaifu;
using Sanakan.ShindenCompat.Models;
using Sanakan.ShindenCompat.Models.Entities;
using Xunit;

namespace Artifacts
{
    public class SeasonStoreTests : IDisposable
    {
        private static readonly TimeSpan Day = TimeSpan.FromDays(1);
        private static readonly TimeSpan Retry = TimeSpan.FromMinutes(5);
        private static readonly DateTime Autumn = new DateTime(2026, 11, 7, 12, 0, 0);
        private static readonly DateTime Winter = new DateTime(2027, 1, 3, 12, 0, 0);

        private readonly string _dir = Path.Combine(Path.GetTempPath(), "sanakan-seasons-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, true);
        }

        private static List<ulong> Ids(params ulong[] ids) => ids.ToList();

        private static CurrentSeason Data(List<ulong> titles, List<ulong> characters, int? year = null, AnimeSeason? season = null)
            => new CurrentSeason { Titles = titles, Characters = characters, Year = year, Season = season };

        private static Func<Task<CurrentSeason>> Returns(List<ulong> titles, List<ulong> characters, Action onCall = null) => Returns(Data(titles, characters), onCall);

        private static Func<Task<CurrentSeason>> Returns(CurrentSeason value, Action onCall = null) => () =>
        {
            onCall?.Invoke();
            return Task.FromResult(value);
        };

        private static Func<Task<CurrentSeason>> Throws(Action onCall = null) => () =>
        {
            onCall?.Invoke();
            throw new Exception("NotFound");
        };

        // same steps as the store, with the current season being the only one known
        private static SeasonSnapshot Apply(SeasonSnapshot current, CurrentSeason value, DateTime now)
        {
            var (year, season) = SeasonStore.Target(current, value, now);
            var existing = current?.Key == SeasonSnapshot.KeyOf(year, season) ? current : null;
            return SeasonStore.Merge(existing, year, season, value, now);
        }

        private static SeasonSnapshot Apply(SeasonSnapshot current, List<ulong> titles, List<ulong> characters, DateTime now)
            => Apply(current, Data(titles, characters), now);

        [Theory]
        [InlineData(1, AnimeSeason.Winter)]
        [InlineData(3, AnimeSeason.Winter)]
        [InlineData(4, AnimeSeason.Spring)]
        [InlineData(7, AnimeSeason.Summer)]
        [InlineData(10, AnimeSeason.Autumn)]
        [InlineData(12, AnimeSeason.Autumn)]
        public void Season_FollowsCalendarQuarters(int month, AnimeSeason expected)
        {
            Assert.Equal(expected, SeasonSnapshot.Of(new DateTime(2026, month, 15)).Season);
        }

        [Theory]
        [InlineData("2026-autumn", 2026, AnimeSeason.Autumn)]
        [InlineData("2026-fall", 2026, AnimeSeason.Autumn)]
        [InlineData(" 2026-FALL ", 2026, AnimeSeason.Autumn)]
        [InlineData("2027-winter", 2027, AnimeSeason.Winter)]
        [InlineData("2027-Spring", 2027, AnimeSeason.Spring)]
        public void Key_IsParsed(string key, int year, AnimeSeason season)
        {
            Assert.True(SeasonSnapshot.TryParseKey(key, out var y, out var s));
            Assert.Equal(year, y);
            Assert.Equal(season, s);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("2026")]
        [InlineData("2026-3")]
        [InlineData("2026-monsoon")]
        [InlineData("abc-fall")]
        [InlineData("2026-fall-x")]
        [InlineData("99999-fall")]
        public void InvalidKey_IsRejected(string key)
        {
            Assert.False(SeasonSnapshot.TryParseKey(key, out _, out _));
        }

        [Fact]
        public async Task Fill_AddsTaggedSeasonWithoutChangingCurrent()
        {
            var store = new SeasonStore(Day, Retry, _dir);
            await store.GetCurrentAsync(Returns(Ids(1), Ids(10)), Autumn);

            var (filled, error) = await store.FillAsync(Returns(Data(Ids(5), Ids(50), 2026, AnimeSeason.Summer)), Autumn.AddHours(1));

            Assert.Null(error);
            Assert.Equal("2026-summer", filled.Key);
            Assert.Equal("2026-autumn", store.Current.Key);
            Assert.Equal(Ids(50), store.Get("2026-summer").Characters);

            var restored = new SeasonStore(Day, Retry, _dir);
            Assert.Equal("2026-autumn", restored.Current.Key);
            Assert.Equal(Ids(50), restored.Get("2026-summer").Characters);
        }

        [Fact]
        public async Task Fill_CurrentSeason_UpdatesCurrent()
        {
            var store = new SeasonStore(Day, Retry);
            await store.GetCurrentAsync(Returns(Ids(1), Ids(10)), Autumn);

            await store.FillAsync(Returns(Data(Ids(2), Ids(11), 2026, AnimeSeason.Autumn)), Autumn.AddHours(1));

            Assert.Equal(Ids(10, 11), store.Current.Characters);
        }

        [Fact]
        public async Task Fill_WithoutTagOrData_IsRejected()
        {
            var store = new SeasonStore(Day, Retry);

            Assert.Equal("Response without season", (await store.FillAsync(Returns(Ids(1), Ids(10)), Autumn)).Error);
            Assert.Equal("Empty response", (await store.FillAsync(Returns(Data(Ids(), Ids(), 2026, AnimeSeason.Summer)), Autumn)).Error);
            Assert.Equal("NotFound", (await store.FillAsync(Throws(), Autumn)).Error);
            Assert.Empty(store.All());
        }

        [Fact]
        public async Task Restart_WithoutPointerFile_UsesLastWrittenSeason()
        {
            var store = new SeasonStore(Day, Retry, _dir);
            await store.GetCurrentAsync(Returns(Ids(1), Ids(10)), Autumn);
            await store.GetCurrentAsync(Returns(Ids(7), Ids(20)), Winter);
            File.Delete(Path.Combine(_dir, "current.txt"));

            Assert.Equal("2027-winter", new SeasonStore(Day, Retry, _dir).Current.Key);
        }

        [Fact]
        public void Season_HasStableKeyAndPolishName()
        {
            var season = new SeasonSnapshot { Year = 2026, Season = AnimeSeason.Autumn };

            Assert.Equal("2026-autumn", season.Key);
            Assert.Equal("jesień 2026", season.Name());
        }

        [Fact]
        public void Apply_WithoutCurrent_StartsSeasonFromCalendar()
        {
            var season = Apply(null, Ids(1, 2, 2), Ids(10, 10, 11), Autumn);

            Assert.Equal("2026-autumn", season.Key);
            Assert.Equal(Ids(1, 2), season.Titles);
            Assert.Equal(Ids(10, 11), season.Characters);
            Assert.Equal(Autumn, season.UpdatedAt);
        }

        [Fact]
        public void Apply_MostlyKnownTitles_AddsNewAndKeepsMissing()
        {
            var current = Apply(null, Ids(1, 2), Ids(10, 11), Autumn);

            var season = Apply(current, Ids(1, 2, 3), Ids(11, 12), Autumn.AddDays(1));

            Assert.Equal("2026-autumn", season.Key);
            Assert.Equal(Ids(1, 2, 3), season.Titles);
            Assert.Equal(Ids(10, 11, 12), season.Characters);
            Assert.Equal(Ids(10, 11), current.Characters);
        }

        [Fact]
        public void Apply_ExactlyHalfTitlesKnown_KeepsSeason()
        {
            var current = Apply(null, Ids(1, 2), Ids(10), Autumn);

            Assert.Equal("2026-autumn", Apply(current, Ids(1, 2, 3, 4), Ids(20), Autumn.AddDays(1)).Key);
        }

        [Fact]
        public void Apply_ShindenSwitchesBeforeCalendar_StartsNextSeason()
        {
            var current = Apply(null, Ids(1, 2, 3, 4), Ids(10), Autumn);

            var season = Apply(current, Ids(4, 7, 8, 9), Ids(10, 20), new DateTime(2026, 12, 28));

            Assert.Equal("2027-winter", season.Key);
            Assert.Equal(Ids(4, 7, 8, 9), season.Titles);
            Assert.Equal(Ids(10, 20), season.Characters);
        }

        [Fact]
        public void Apply_ShindenStillOnOldSeasonAfterCalendar_KeepsFillingOldSeason()
        {
            var current = Apply(null, Ids(1, 2, 3, 4), Ids(10), Autumn);

            var season = Apply(current, Ids(1, 2, 3, 9), Ids(11), Winter);

            Assert.Equal("2026-autumn", season.Key);
            Assert.Equal(Ids(10, 11), season.Characters);
        }

        [Fact]
        public void Apply_NewTitlesAfterCalendar_StartsCalendarSeason()
        {
            var current = Apply(null, Ids(1, 2, 3, 4), Ids(10), Autumn);

            Assert.Equal("2027-winter", Apply(current, Ids(7, 8, 9), Ids(20), Winter).Key);
        }

        [Fact]
        public void Apply_NewTitlesAfterLongBreak_JumpsToCalendarSeason()
        {
            var current = Apply(null, Ids(1, 2), Ids(10), Autumn);

            Assert.Equal("2027-summer", Apply(current, Ids(7, 8), Ids(20), new DateTime(2027, 8, 1)).Key);
        }

        [Fact]
        public void Apply_WithoutTitles_KeepsSeason()
        {
            var current = Apply(null, Ids(1), Ids(10), Autumn);

            Assert.Equal("2026-autumn", Apply(current, Ids(), Ids(20), Winter).Key);
        }

        [Fact]
        public void Apply_SeasonReportedByShinden_WinsOverTitlesAndCalendar()
        {
            var current = Apply(null, Ids(1, 2), Ids(10), Autumn);

            Assert.Equal("2026-autumn", Apply(current, Data(Ids(7, 8), Ids(20), 2026, AnimeSeason.Autumn), Winter).Key);
            Assert.Equal("2027-spring", Apply(current, Data(Ids(1, 2), Ids(20), 2027, AnimeSeason.Spring), Autumn).Key);
        }

        [Fact]
        public async Task Store_SeasonReportedByShinden_FillsExistingSeasonAndBecomesCurrent()
        {
            var store = new SeasonStore(Day, Retry, _dir);
            await store.GetCurrentAsync(Returns(Ids(1), Ids(10)), Autumn);
            await store.GetCurrentAsync(Returns(Ids(7), Ids(20)), Winter);

            var current = await store.GetCurrentAsync(Returns(Data(Ids(2), Ids(11), 2026, AnimeSeason.Autumn)), Winter.Add(Day));

            Assert.Equal("2026-autumn", current.Key);
            Assert.Equal(Ids(10, 11), current.Characters);
            Assert.Equal(Ids(20), store.Get("2027-winter").Characters);
            Assert.Equal("2026-autumn", (await new SeasonStore(Day, Retry, _dir).GetCurrentAsync(Throws(), Winter.Add(Day))).Key);
        }

        [Fact]
        public async Task Store_FetchesOncePerDay()
        {
            var store = new SeasonStore(Day, Retry);
            int calls = 0;

            await store.GetCurrentAsync(Returns(Ids(1), Ids(10), () => calls++), Autumn);
            var season = await store.GetCurrentAsync(Returns(Ids(1), Ids(11), () => calls++), Autumn.AddHours(23));
            Assert.Equal(1, calls);
            Assert.Equal(Ids(10), season.Characters);

            season = await store.GetCurrentAsync(Returns(Ids(1), Ids(11), () => calls++), Autumn.Add(Day));
            Assert.Equal(2, calls);
            Assert.Equal(Ids(10, 11), season.Characters);
        }

        [Fact]
        public async Task Store_Force_FetchesEvenWhenFresh()
        {
            var store = new SeasonStore(Day, Retry);
            int calls = 0;
            await store.GetCurrentAsync(Returns(Ids(1), Ids(10), () => calls++), Autumn);

            var season = await store.GetCurrentAsync(Returns(Ids(1), Ids(11), () => calls++), Autumn.AddMinutes(1), force: true);

            Assert.Equal(2, calls);
            Assert.Equal(Ids(10, 11), season.Characters);
            Assert.Same(season, store.Current);
        }

        [Fact]
        public async Task Store_All_ListsNewestSeasonFirst()
        {
            var store = new SeasonStore(Day, Retry);
            await store.GetCurrentAsync(Returns(Ids(1), Ids(10)), Autumn);
            await store.GetCurrentAsync(Returns(Ids(7), Ids(20)), Winter);

            Assert.Equal(new[] { "2027-winter", "2026-autumn" }, store.All().Select(x => x.Key));
            Assert.Null(store.FailedAt);
        }

        [Fact]
        public async Task Store_FailedOrEmptyRefresh_KeepsSeasonAndWaits()
        {
            var store = new SeasonStore(Day, Retry);
            int calls = 0;
            await store.GetCurrentAsync(Returns(Ids(1), Ids(10)), Autumn);

            var later = Autumn.Add(Day);
            Assert.Equal(Ids(10), (await store.GetCurrentAsync(Throws(() => calls++), later)).Characters);
            Assert.Equal("NotFound", store.LastError);
            Assert.Equal(Ids(10), (await store.GetCurrentAsync(Throws(() => calls++), later.AddMinutes(4))).Characters);
            Assert.Equal(1, calls);

            Assert.Equal(Ids(10), (await store.GetCurrentAsync(Returns(Ids(), Ids(), () => calls++), later.Add(Retry))).Characters);
            Assert.Equal(2, calls);
            Assert.NotNull(store.LastError);
        }

        [Fact]
        public async Task Store_WithoutDataAndConnection_ReturnsNull()
        {
            var store = new SeasonStore(Day, Retry);

            Assert.Null(await store.GetCurrentAsync(Throws(), Autumn));
            Assert.Null(store.Get("2026-autumn"));
            Assert.Null(store.Get(null));
        }

        [Fact]
        public async Task Store_KeepsEverySeasonAfterRestart()
        {
            var store = new SeasonStore(Day, Retry, _dir);
            await store.GetCurrentAsync(Returns(Ids(1, 2), Ids(10)), Autumn);
            await store.GetCurrentAsync(Returns(Ids(7, 8), Ids(20)), Winter);

            int calls = 0;
            var restored = new SeasonStore(Day, Retry, _dir, new ListLogger());
            var current = await restored.GetCurrentAsync(Returns(Ids(9), Ids(30), () => calls++), Winter.AddHours(1));

            Assert.Equal(0, calls);
            Assert.Equal("2027-winter", current.Key);
            Assert.Equal(Ids(20), current.Characters);
            Assert.Equal(Ids(10), restored.Get("2026-autumn").Characters);
        }

        [Fact]
        public async Task Store_BrokenFile_IsSkipped()
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(Path.Combine(_dir, "2026-summer.json"), "{ not json");
            await new SeasonStore(Day, Retry, _dir).GetCurrentAsync(Returns(Ids(1), Ids(10)), Autumn);

            var logger = new ListLogger();
            var restored = new SeasonStore(Day, Retry, _dir, logger);

            Assert.Equal(Ids(10), restored.Get("2026-autumn").Characters);
            Assert.NotEmpty(logger.Messages);
        }
    }

    public class SeasonPackTests : IDisposable
    {
        private class ShindenStub : HttpMessageHandler
        {
            public List<string> Paths { get; } = new List<string>();
            public string Season { get; set; } = @"{ ""titles"": [ 100 ], ""characters"": [ ""15"" ] }";

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var path = request.RequestUri.AbsolutePath;
                lock (Paths) Paths.Add(path);

                string json = path == "/compat/php-api/anime/current-season" ? Season : null;
                if (path.StartsWith("/compat/php-api/character/"))
                {
                    var id = path.Split('/').Last();
                    json = $@"{{ ""character_id"": ""{id}"", ""first_name"": ""Postać"", ""last_name"": ""{id}"", ""picture_artifact_id"": null }}";
                }

                return Task.FromResult(json == null
                    ? new HttpResponseMessage(HttpStatusCode.NotFound)
                    : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
            }
        }

        private readonly string _dir = Path.Combine(Path.GetTempPath(), "sanakan-season-packs-" + Guid.NewGuid().ToString("N"));
        private readonly ShindenStub _stub = new ShindenStub();
        private readonly FixedTime _time = new FixedTime { Value = new DateTime(2026, 11, 7, 12, 0, 0) };
        private readonly Sanakan.Services.Shinden _shinden;
        private readonly Waifu _waifu;

        public SeasonPackTests()
        {
            var client = Sanakan.ShindenCompat.ShindenClient.Create(new ConfigShinden { Token = "token", UserAgent = "tests", Marmolade = "marmolade" },
                null, _stub, TimeSpan.FromSeconds(5));
            var logger = new ListLogger();
            _shinden = new Sanakan.Services.Shinden(client, null, null, logger, _time, _dir);
            _waifu = new Waifu(null, client, null, logger, null, null, null, _time, _shinden, null, new FakeConfig());
        }

        public void Dispose()
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, true);
        }

        private static BoosterPack SeasonPack(string season, int cards = 2) => new BoosterPack
        {
            CardCnt = cards,
            Season = season,
            Characters = new List<BoosterPackCharacter>(),
            RarityExcludedFromPack = new List<RarityExcluded>(),
            CardSourceFromPack = CardSource.Lottery,
            MinRarity = Rarity.E,
        };

        private int SeasonRequests => _stub.Paths.Count(x => x.EndsWith("current-season"));

        [Fact]
        public async Task SeasonPack_DrawsOnlyFromCharactersOfItsSeason()
        {
            _stub.Season = @"{ ""titles"": [ 100, 101 ], ""characters"": [ ""15"", ""16"" ] }";
            var season = await _shinden.GetCurrentSeasonAsync();

            var cards = await _waifu.OpenBoosterPackAsync(null, SeasonPack(season.Key, 10), CharacterPoolType.Anime);

            Assert.Equal(10, cards.Count);
            Assert.All(cards, x => Assert.Contains(x.Character, new ulong[] { 15, 16 }));
            Assert.All(cards, x => Assert.Equal(CardSource.Lottery, x.Source));
            Assert.Equal(1, SeasonRequests);
        }

        [Fact]
        public async Task OldSeasonPack_UsesSavedSeasonAndNotTheCurrentOne()
        {
            await CacheFile.WriteAsync(Path.Combine(_dir, "seasons", "2026-summer.json"),
                new SeasonSnapshot { Year = 2026, Season = AnimeSeason.Summer, UpdatedAt = _time.Value.AddMonths(-3), Titles = new List<ulong> { 50 }, Characters = new List<ulong> { 77 } });
            await _shinden.GetCurrentSeasonAsync();

            var cards = await _waifu.OpenBoosterPackAsync(null, SeasonPack("2026-summer"), CharacterPoolType.Anime);

            Assert.Equal(2, cards.Count);
            Assert.All(cards, x => Assert.Equal(77ul, x.Character));
        }

        [Fact]
        public async Task SeasonPack_WithLostSeasonFile_FallsBackToCurrentSeason()
        {
            var cards = await _waifu.OpenBoosterPackAsync(null, SeasonPack("2020-spring"), CharacterPoolType.Anime);

            Assert.Equal(2, cards.Count);
            Assert.All(cards, x => Assert.Equal(15ul, x.Character));
        }

        [Fact]
        public async Task FillSeason_UntaggedResponse_IsSavedAsRequestedSeason()
        {
            _stub.Season = @"{ ""titles"": [ 300 ], ""characters"": [ ""30"" ] }";

            var (season, error) = await _shinden.FillSeasonAsync(2026, AnimeSeason.Summer);

            Assert.Null(error);
            Assert.Equal("2026-summer", season.Key);
            Assert.Null(_shinden.Seasons.Current);
            Assert.Contains(_stub.Paths, x => x.EndsWith("current-season"));
        }

        [Fact]
        public async Task FillSeason_OtherSeasonReturned_IsRejected()
        {
            _stub.Season = @"{ ""season"": { ""year"": 2026, ""season"": ""spring"" }, ""titles"": [ 300 ], ""characters"": [ ""30"" ] }";

            var (season, error) = await _shinden.FillSeasonAsync(2026, AnimeSeason.Summer);

            Assert.Null(season);
            Assert.Contains("2026-spring", error);
            Assert.Empty(_shinden.Seasons.All());
        }

        [Fact]
        public async Task CurrentSeason_TaggedByShinden_UsesTagInsteadOfCalendar()
        {
            _stub.Season = @"{ ""season"": { ""year"": 2026, ""season"": ""summer"" }, ""titles"": [ 100 ], ""characters"": [ ""15"" ] }";

            Assert.Equal("2026-summer", (await _shinden.GetCurrentSeasonAsync()).Key);
        }

        [Fact]
        public async Task SeasonPack_WithoutAnySeason_GivesNoCardsSoItIsNotConsumed()
        {
            _stub.Season = @"{ ""titles"": [], ""characters"": [] }";

            var cards = await _waifu.OpenBoosterPackAsync(null, SeasonPack("2026-autumn"), CharacterPoolType.Anime);

            Assert.Empty(cards);
        }
    }
}
