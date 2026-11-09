using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sanakan.Api.Controllers;
using Sanakan.Api.Models;
using Sanakan.Database;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services.Cache;
using Sanakan.Services.Executor;
using Sanakan.Services.Time;
using Sanakan.ShindenCompat.Models;
using Xunit;

namespace Artifacts
{
    public class SeasonPackApiTests : IDisposable
    {
        private const ulong DiscordId = 93001;
        private const ulong ShindenId = 94001;
        private const string PackName = "Pakiet sezonowy ze strony";

        private readonly SqliteConnection _connection = new SqliteConnection("Data Source=:memory:");
        private readonly FakeExecutor _executor = new FakeExecutor { RunImmediately = true };
        private readonly FixedTime _time = new FixedTime { Value = new DateTime(2026, 11, 7, 12, 0, 0) };
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "sanakan-season-api-" + Guid.NewGuid().ToString("N"));

        private sealed class TestDatabase : DatabaseContext
        {
            private readonly SqliteConnection _connection;
            public TestDatabase(SqliteConnection connection) : base(new FakeConfig()) => _connection = connection;
            protected override void OnConfiguring(DbContextOptionsBuilder builder) => builder.UseSqlite(_connection);
        }

        private sealed class TestController : WaifuController
        {
            private readonly Func<DatabaseContext> _factory;
            public TestController(IExecutor executor, ISystemTime time, Sanakan.Services.Shinden shinden, Func<DatabaseContext> factory)
                : base(null, null, executor, null, new FakeConfig(), time, shinden, null, null, null) => _factory = factory;
            protected override DatabaseContext CreateDatabaseContext() => _factory();
        }

        public SeasonPackApiTests()
        {
            _connection.Open();
            Sanakan.Database.SqliteText.RegisterPow(_connection);
            using var db = Open();
            db.Database.EnsureCreated();
            var user = new User().Default(DiscordId, _time.Now());
            user.Shinden = ShindenId;
            db.Users.Add(user);
            db.SaveChanges();
        }

        public void Dispose()
        {
            _connection.Dispose();
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, true);
        }

        private TestDatabase Open() => new TestDatabase(_connection);

        private async Task SaveSeasonAsync(int year, AnimeSeason season, DateTime updatedAt) => await CacheFile.WriteAsync(
            Path.Combine(_dir, "seasons", $"{SeasonSnapshot.KeyOf(year, season)}.json"),
            new SeasonSnapshot { Year = year, Season = season, UpdatedAt = updatedAt, Titles = new List<ulong> { 1 }, Characters = new List<ulong> { 15 } });

        private TestController Controller(bool withSeasons = true) => ControllerHarness.Attach(new TestController(_executor, _time,
            new Sanakan.Services.Shinden(null, null, null, new ListLogger(), _time, withSeasons ? _dir : null), Open));

        private static List<CardBoosterPack> SeasonPacks(string season = null) => new List<CardBoosterPack>
        {
            new CardBoosterPack { Name = PackName, Count = 2, Pool = new BoosterPackPool { Type = CardsPoolType.Season, Season = season } },
        };

        private List<BoosterPack> SavedPacks()
        {
            using var db = Open();
            return db.BoosterPacks.AsQueryable().Where(x => x.GameDeckId == DiscordId && x.Name == PackName).ToList();
        }

        [Fact]
        public async Task SeasonPoolWithoutKey_GetsCurrentSeason()
        {
            await SaveSeasonAsync(2026, AnimeSeason.Summer, _time.Value.AddMonths(-2));
            await SaveSeasonAsync(2026, AnimeSeason.Autumn, _time.Value);

            var res = await Controller().GiveShindenUserAPacksAsync(ShindenId, SeasonPacks(), $"key-{Guid.NewGuid():N}");

            Assert.IsType<UserWithToken>(res.Value);
            var pack = Assert.Single(SavedPacks());
            Assert.Equal("2026-autumn", pack.Season);
            Assert.Equal(0ul, pack.Title);
            Assert.Equal(CardsPoolType.Season, UserBoosterPack.From(pack, 1).Pool);
        }

        [Fact]
        public async Task SeasonPoolWithKey_GetsThatSeason()
        {
            await SaveSeasonAsync(2026, AnimeSeason.Summer, _time.Value.AddMonths(-2));
            await SaveSeasonAsync(2026, AnimeSeason.Autumn, _time.Value);

            var res = await Controller().GiveShindenUserAPacksAsync(ShindenId, SeasonPacks(" 2026-SUMMER "), $"key-{Guid.NewGuid():N}");

            Assert.IsType<UserWithToken>(res.Value);
            Assert.Equal("2026-summer", Assert.Single(SavedPacks()).Season);
        }

        [Fact]
        public async Task UnknownSeason_Returns404AndAddsNothing()
        {
            await SaveSeasonAsync(2026, AnimeSeason.Autumn, _time.Value);

            var res = await Controller().GiveShindenUserAPacksAsync(ShindenId, SeasonPacks("2019-winter"), $"key-{Guid.NewGuid():N}");

            Assert.Equal(404, res.StatusOf());
            Assert.Empty(SavedPacks());
            Assert.Empty(_executor.Added);
        }

        [Fact]
        public async Task NoSeasonKnown_Returns503AndAddsNothing()
        {
            var res = await Controller(withSeasons: false).GiveShindenUserAPacksAsync(ShindenId, SeasonPacks(), $"key-{Guid.NewGuid():N}");

            Assert.Equal(503, res.StatusOf());
            Assert.Empty(SavedPacks());
        }

        [Fact]
        public async Task OtherPools_DoNotTouchSeasons()
        {
            var packs = new List<CardBoosterPack> { new CardBoosterPack { Name = PackName, Count = 1, Pool = new BoosterPackPool { Type = CardsPoolType.Title, TitleId = 5, Season = "2026-autumn" } } };

            var res = await Controller(withSeasons: false).GiveShindenUserAPacksAsync(ShindenId, packs, $"key-{Guid.NewGuid():N}");

            Assert.IsType<UserWithToken>(res.Value);
            var pack = Assert.Single(SavedPacks());
            Assert.Null(pack.Season);
            Assert.Equal(5ul, pack.Title);
        }
    }
}
