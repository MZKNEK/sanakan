using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sanakan.Database;
using Sanakan.Database.Models;
using Sanakan.Database.Models.Analytics;
using Sanakan.Extensions;
using Sanakan.Services;
using Sanakan.Services.Executor;
using Sanakan.Services.PocketWaifu;
using Xunit;

namespace Artifacts
{
    public class GameTelemetryTests : IDisposable
    {
        private const ulong Player = 51001;
        private const ulong Other = 51002;

        private readonly SqliteConnection _connection = new SqliteConnection("Data Source=:memory:");
        private readonly GameTelemetry _telemetry = new GameTelemetry();
        private DateTime _now = new DateTime(2026, 11, 13, 12, 0, 0);

        private sealed class TestDatabase : DatabaseContext
        {
            private readonly SqliteConnection _connection;
            private readonly GameTelemetry _telemetry;
            public bool FailSave { get; set; }

            public TestDatabase(SqliteConnection connection, GameTelemetry telemetry) : base(new FakeConfig())
            {
                _connection = connection;
                _telemetry = telemetry;
            }

            protected override void OnConfiguring(DbContextOptionsBuilder builder)
                => builder.UseSqlite(_connection).AddInterceptors(_telemetry.Interceptor);

            public override int SaveChanges(bool acceptAllChangesOnSuccess)
            {
                if (FailSave) throw new DbUpdateException("save failed");
                return base.SaveChanges(acceptAllChangesOnSuccess);
            }

            public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, System.Threading.CancellationToken cancellationToken = default)
            {
                if (FailSave) throw new DbUpdateException("save failed");
                return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            }
        }

        public GameTelemetryTests()
        {
            _telemetry.Now = () => _now;
            _connection.Open();
            SqliteText.RegisterPow(_connection);
            using var db = Open();
            db.Database.EnsureCreated();
            db.Users.Add(new User().Default(Player, _now));
            db.SaveChanges();
            // the setup's own new player and starting SC are not what the tests look at
            _telemetry.FlushAsync(Open).GetAwaiter().GetResult();
            db.GameStats.RemoveRange(db.GameStats);
            db.PlayerDays.RemoveRange(db.PlayerDays);
            db.SaveChanges();
        }

        public void Dispose() => _connection.Dispose();

        private TestDatabase Open() => new TestDatabase(_connection, _telemetry);

        private async Task<Dictionary<(string Metric, string Label), long>> StatsAsync(DateTime? date = null)
        {
            await _telemetry.FlushAsync(Open);
            using var db = Open();
            return (await db.GameStats.AsNoTracking().Where(x => x.Date == (date ?? _now.Date)).ToListAsync())
                .ToDictionary(x => (x.Metric, x.Label), x => x.Value);
        }

        private TestDatabase WithReason(string reason)
        {
            GameTelemetry.Reason = reason;
            return Open();
        }

        [Fact]
        public async Task CurrencyIsCountedFromWhatTheSaveWrites()
        {
            using (var db = WithReason("drobne"))
            {
                var user = db.Users.Include(x => x.GameDeck).First(x => x.Id == Player);
                user.ScCnt += 100;
                user.TcCnt += 50;
                user.TcCnt -= 20;
                user.GameDeck.CTCnt += 3;
                db.SaveChanges();
            }
            using (var db = WithReason("sklepik"))
            {
                var user = db.Users.First(x => x.Id == Player);
                user.TcCnt -= 30;
                db.SaveChanges();
            }

            var stats = await StatsAsync();
            Assert.Equal(100, stats[("currency_in", "SC:drobne")]);
            Assert.Equal(30, stats[("currency_in", "TC:drobne")]);
            Assert.Equal(3, stats[("currency_in", "CT:drobne")]);
            Assert.Equal(30, stats[("currency_out", "TC:sklepik")]);
        }

        [Fact]
        public async Task NothingIsCountedWithoutASuccessfulSave()
        {
            using (var db = WithReason("rzut"))
            {
                var user = db.Users.First(x => x.Id == Player);
                user.ScCnt -= 10;
                // the command returned before saving
            }
            using (var db = WithReason("rzut"))
            {
                var user = db.Users.First(x => x.Id == Player);
                user.ScCnt -= 10;
                db.FailSave = true;
                Assert.Throws<DbUpdateException>(() => db.SaveChanges());
            }

            Assert.Empty(await StatsAsync());
        }

        [Fact]
        public async Task ASaveIsCountedOnce()
        {
            using var db = WithReason("drobne");
            var user = db.Users.First(x => x.Id == Player);
            user.ScCnt += 100;
            db.SaveChanges();
            db.SaveChanges();

            Assert.Equal(100, (await StatsAsync())[("currency_in", "SC:drobne")]);
        }

        [Fact]
        public async Task NewPlayersAreMarkedOnTheirDay()
        {
            using (var db = WithReason("other"))
            {
                db.Users.Add(new User().Default(Other, _now));
                db.SaveChanges();
            }
            _telemetry.Command(Other, "karta+", false);
            _telemetry.Command(Other, "karta+", true);

            var stats = await StatsAsync();
            Assert.Equal(1, stats[("players_new", "")]);
            Assert.Equal(2, stats[("command", "karta+")]);
            Assert.Equal(1, stats[("command_failed", "karta+")]);
            using var check = Open();
            var day = check.PlayerDays.Single(x => x.UserId == Other);
            Assert.True(day.IsNew);
            Assert.Equal(2, day.Commands);
            Assert.Equal(_now.Date, day.Date);
        }

        [Fact]
        public async Task CardsPacksAndItemsAreCounted()
        {
            ulong cardId;
            using (var db = WithReason("pakiet"))
            {
                var deck = db.GameDecks.First(x => x.Id == Player);
                var card = new Card { Character = 1, Name = "a", Title = "t", Rarity = Rarity.A, Source = CardSource.Activity, Dere = Dere.Deredere, Tags = new List<Tag>() };
                deck.Cards = new List<Card> { card };
                db.Items.Add(new Item { GameDeckId = Player, Type = ItemType.AffectionRecoverySmall, Count = 5, Name = "x" });
                db.BoosterPacks.Add(new BoosterPack { GameDeckId = Player, Name = "p", CardCnt = 2, CardSourceFromPack = CardSource.Lottery });
                db.SaveChanges();
                cardId = card.Id;
            }
            using (var db = WithReason("ulepsz"))
            {
                var card = db.Cards.First(x => x.Id == cardId);
                card.Rarity = Rarity.S;
                var item = db.Items.First(x => x.GameDeckId == Player);
                item.Count -= 2;
                // a new player also has the starter pack
                db.BoosterPacks.Remove(db.BoosterPacks.First(x => x.GameDeckId == Player && x.CardSourceFromPack == CardSource.Lottery));
                db.SaveChanges();
            }
            using (var db = WithReason("zniszcz"))
            {
                db.Cards.Remove(db.Cards.First(x => x.Id == cardId));
                db.Items.Remove(db.Items.First(x => x.GameDeckId == Player));
                db.SaveChanges();
            }

            var stats = await StatsAsync();
            Assert.Equal(1, stats[("card_in", "A:Activity")]);
            Assert.Equal(1, stats[("card_upgrade", "A>S")]);
            Assert.Equal(1, stats[("card_out", "S:zniszcz")]);
            Assert.Equal(5, stats[("item_in", "AffectionRecoverySmall:pakiet")]);
            Assert.Equal(2, stats[("item_out", "AffectionRecoverySmall:ulepsz")]);
            Assert.Equal(3, stats[("item_out", "AffectionRecoverySmall:zniszcz")]);
            Assert.Equal(1, stats[("pack_in", "Lottery:pakiet")]);
            Assert.Equal(1, stats[("pack_out", "Lottery:ulepsz")]);
        }

        [Fact]
        public async Task FlushAddsToTheDayAndKeepsDaysApart()
        {
            _telemetry.Add("command", "x", 2);
            await _telemetry.FlushAsync(Open);
            _telemetry.Add("command", "x", 3);
            _now = _now.AddDays(1);
            _telemetry.Add("command", "x", 7);

            Assert.Equal(5, (await StatsAsync(_now.Date.AddDays(-1)))[("command", "x")]);
            Assert.Equal(7, (await StatsAsync())[("command", "x")]);
        }

        [Fact]
        public async Task AFailedFlushKeepsTheCounters()
        {
            _telemetry.Add("command", "x", 4);
            _telemetry.Command(Player, "x", false);
            _telemetry.Duel(new DuelResult { AttackerCards = "S", DefenderCards = "A" });

            await Assert.ThrowsAnyAsync<Exception>(() => _telemetry.FlushAsync(() =>
            {
                var db = Open();
                db.FailSave = true;
                return db;
            }));

            Assert.Equal(5, (await StatsAsync())[("command", "x")]);
            using var check = Open();
            Assert.Equal(1, check.PlayerDays.Single(x => x.UserId == Player).Commands);
            Assert.Single(check.DuelResults);
        }

        [Fact]
        public async Task DuelsAreStoredWithTheirTime()
        {
            _telemetry.Duel(new DuelResult { Outcome = DuelOutcome.Draw, AttackerWinChance = 0.25, AttackerCards = "SSS,A", DefenderCards = "B" });
            await _telemetry.FlushAsync(Open);

            using var db = Open();
            var duel = db.DuelResults.Single();
            Assert.Equal(_now, duel.Date);
            Assert.Equal(DuelOutcome.Draw, duel.Outcome);
            Assert.Equal("SSS,A", duel.AttackerCards);
        }

        [Fact]
        public void RaritiesAreListedBestFirst()
            => Assert.Equal("SSS,S,E", GameTelemetry.Rarities(new[] { new Card { Rarity = Rarity.E }, new Card { Rarity = Rarity.SSS }, new Card { Rarity = Rarity.S } }));

        [Fact]
        public async Task SnapshotShowsRetentionWalletsAndWhatPlayersCanAfford()
        {
            var day = _now.Date;
            using (var db = Open())
            {
                // a new player of a week ago who came back today, another one who did not
                db.Users.Add(new User().Default(Other, day.AddDays(-7)));
                db.PlayerDays.AddRange(
                    new PlayerDay { Date = day.AddDays(-7), UserId = Player, Commands = 3, IsNew = true },
                    new PlayerDay { Date = day.AddDays(-7), UserId = Other, Commands = 1, IsNew = true },
                    new PlayerDay { Date = day, UserId = Player, Commands = 2 });
                db.SaveChanges();
                db.Users.First(x => x.Id == Player).TcCnt = 1000;
                db.Users.First(x => x.Id == Other).TcCnt = 20;
                db.SaveChanges();
            }
            await _telemetry.FlushAsync(Open);
            using (var db = Open())
            {
                db.GameStats.RemoveRange(db.GameStats);
                db.SaveChanges();
            }

            var prices = new[]
            {
                new GameTelemetry.ShopPrice { Shop = "Sklepik", Item = "1:Cheap", Currency = CurrencyType.TC, Cost = 10 },
                new GameTelemetry.ShopPrice { Shop = "Sklepik", Item = "2:Dear", Currency = CurrencyType.TC, Cost = 999 },
            };
            Assert.False(await GameTelemetry.HasSnapshotAsync(Open, day));
            await _telemetry.SnapshotAsync(Open, day, prices);
            Assert.True(await GameTelemetry.HasSnapshotAsync(Open, day));

            using var check = Open();
            var stats = check.GameStats.AsNoTracking().ToList().ToDictionary(x => (x.Date, x.Metric, x.Label), x => x.Value);
            Assert.Equal(1, stats[(day, "players_active", "")]);
            Assert.Equal(2, stats[(day, "players_active", "30d")]);
            Assert.Equal(500, stats[(day.AddDays(-7), "retention", "d7")]);
            Assert.Equal(2, stats[(day.AddDays(-7), "newbies_d7", "players")]);
            Assert.Equal(1000, stats[(day, "can_afford", "Sklepik:1:Cheap")]);
            Assert.Equal(500, stats[(day, "can_afford", "Sklepik:2:Dear")]);
            Assert.Equal(1000, stats[(day, "balance", "TC:p99")]);
            Assert.Equal(1020, stats[(day, "circulation", "TC")]);
            Assert.Equal(20, stats[(day, "balance", "TC:p50")]);
            Assert.Equal(200, stats[(day, "circulation", "SC")]);

            // running it again replaces the values
            await _telemetry.SnapshotAsync(Open, day, prices);
            using var again = Open();
            Assert.Equal(stats.Count, again.GameStats.Count());
        }

        [Fact]
        public async Task CleanupRemovesWhatIsOlderThanAYear()
        {
            var old = _now.Date - GameTelemetry.Retention - TimeSpan.FromDays(1);
            using (var db = Open())
            {
                db.PlayerDays.AddRange(new PlayerDay { Date = old, UserId = Player }, new PlayerDay { Date = _now.Date, UserId = Player });
                db.DuelResults.AddRange(new DuelResult { Date = old, AttackerCards = "", DefenderCards = "" },
                    new DuelResult { Date = _now, AttackerCards = "", DefenderCards = "" });
                db.SystemData.AddRange(Enumerable.Range(0, 12001).Select(_ => new SystemAnalytics { MeasureDate = old, Value = 1 }));
                db.SystemData.Add(new SystemAnalytics { MeasureDate = _now, Value = 2 });
                db.GameStats.Add(new GameStat { Date = old, Metric = "command", Label = "x", Value = 1 });
                db.SaveChanges();
            }

            await _telemetry.CleanupAsync(Open);

            using var check = Open();
            Assert.Equal(_now.Date, check.PlayerDays.Single().Date);
            Assert.Equal(_now, check.DuelResults.Single().Date);
            Assert.Equal(2, check.SystemData.Single().Value);
            Assert.Single(check.GameStats);
        }

        [Fact]
        public async Task QueuedWorkKeepsTheCommandItWasQueuedBy()
        {
            GameTelemetry.Reason = "pojedynek";
            var seen = "";
            var work = new Executable("duel", () => { seen = GameTelemetry.Reason; return Task.CompletedTask; });
            GameTelemetry.Reason = "something else";

            await Task.Run(() => work.ExecuteAsync(null));

            Assert.Equal("pojedynek", seen);
        }
    }
}
