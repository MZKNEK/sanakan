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
            _telemetry.Duel(new DuelResult { AttackerCards = "S", DefenderCards = "A" }, Player, Other);

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
            _telemetry.Duel(new DuelResult { Outcome = DuelOutcome.Draw, AttackerWinChance = 0.25, AttackerCards = "SSS,A", DefenderCards = "B" }, Player, Other);
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
        public void DeresFollowTheOrderOfRarities()
        {
            var cards = new[]
            {
                new Card { Rarity = Rarity.E, Dere = Dere.Tsundere }, new Card { Rarity = Rarity.SSS, Dere = Dere.Yato },
                new Card { Rarity = Rarity.S, Dere = Dere.Yami }, new Card { Rarity = Rarity.SSS, Dere = Dere.Raito },
            };
            Assert.Equal("SSS,SSS,S,E", GameTelemetry.Rarities(cards));
            Assert.Equal("Yato,Raito,Yami,Tsundere", GameTelemetry.Deres(cards));
        }

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
                db.CardExchanges.AddRange(
                    new CardExchange { Date = day.AddHours(1), User1Id = Player, User2Id = Other, User1Gave = 2, User2Gave = 1 },
                    new CardExchange { Date = day.AddHours(2), User1Id = Other, User2Id = Player, User1Gave = 3, User2Gave = 0 },
                    new CardExchange { Date = day.AddDays(1), User1Id = Player, User2Id = Other, User1Gave = 9, User2Gave = 9 });
                db.ScGifts.AddRange(
                    new ScGift { Date = day.AddHours(3), FromUserId = Player, ToUserId = Other, Sent = 2000, Received = 1200 },
                    new ScGift { Date = day.AddHours(4), FromUserId = Player, ToUserId = Other, Sent = 1000, Received = 600 });
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
                new GameTelemetry.ShopPrice { Label = "Sklepik:1:Cheap", Currency = CurrencyType.TC, Cost = 10 },
                new GameTelemetry.ShopPrice { Label = "Sklepik:2:Dear", Currency = CurrencyType.TC, Cost = 999 },
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
            Assert.Equal((2L, 6L, 1L, 2L, 1L), (stats[(day, "exchanges", "count")], stats[(day, "exchanges", "cards")],
                stats[(day, "exchanges", "one_sided")], stats[(day, "exchanges", "players")], stats[(day, "exchanges", "pairs")]));
            Assert.Equal((2L, 3000L, 1800L, 1L, 1L), (stats[(day, "sc_gift", "count")], stats[(day, "sc_gift", "sent")],
                stats[(day, "sc_gift", "received")], stats[(day, "sc_gift", "givers")], stats[(day, "sc_gift", "recipients")]));

            // running it again replaces the values
            await _telemetry.SnapshotAsync(Open, day, prices);
            using var again = Open();
            Assert.Equal(stats.Count, again.GameStats.Count());
        }

        [Fact]
        public async Task StaffAccountsAreNotCounted()
        {
            const ulong Staff = 51003;
            _telemetry.ExcludedUsers = () => new[] { Staff };
            using (var db = WithReason("other"))
            {
                db.Users.Add(new User().Default(Staff, _now));
                db.SaveChanges();
            }
            using (var db = WithReason("give"))
            {
                var staff = db.Users.Include(x => x.GameDeck).First(x => x.Id == Staff);
                staff.ScCnt -= 100;
                staff.GameDeck.CTCnt += 5;
                staff.GameDeck.Cards = new List<Card> { new Card { Character = 1, Name = "a", Title = "t", Rarity = Rarity.SSS, Source = CardSource.GodIntervention, Dere = Dere.Yato, Tags = new List<Tag>() } };
                db.Items.Add(new Item { GameDeckId = Staff, Type = ItemType.AffectionRecoverySmall, Count = 5, Name = "x" });
                // what the staff gives a player is still counted
                db.Users.First(x => x.Id == Player).ScCnt += 100;
                db.SaveChanges();
            }
            using (var db = WithReason("zniszcz"))
            {
                db.Cards.Remove(db.Cards.First(x => x.GameDeckId == Staff));
                db.SaveChanges();
            }
            _telemetry.Command(Staff, "give", false);
            _telemetry.ErrorReply(Staff, "give");
            _telemetry.Duel(new DuelResult { AttackerCards = "S", DefenderCards = "A" }, Player, Staff);

            var stats = await StatsAsync();
            Assert.Equal(new[] { ("currency_in", "SC:give") }, stats.Keys.ToArray());
            Assert.Equal(100, stats[("currency_in", "SC:give")]);
            using var check = Open();
            Assert.Empty(check.PlayerDays);
            Assert.Empty(check.DuelResults);
        }

        [Fact]
        public async Task SnapshotLeavesOutStaffAccounts()
        {
            const ulong Staff = 51003;
            _telemetry.ExcludedUsers = () => new[] { Staff };
            var day = _now.Date;
            using (var db = Open())
            {
                db.Users.Add(new User().Default(Staff, day));
                db.PlayerDays.AddRange(new PlayerDay { Date = day, UserId = Player, Commands = 1 }, new PlayerDay { Date = day, UserId = Staff, Commands = 9 });
                db.SaveChanges();
                db.Users.First(x => x.Id == Staff).TcCnt = 1_000_000;
                db.Users.First(x => x.Id == Player).TcCnt = 30;
                db.GameDecks.First(x => x.Id == Staff).Cards = new List<Card> { new Card { Character = 1, Name = "a", Title = "t", Rarity = Rarity.SSS, Dere = Dere.Yato, Tags = new List<Tag>() } };
                db.GameDecks.First(x => x.Id == Player).Cards = new List<Card> { new Card { Character = 2, Name = "b", Title = "t", Rarity = Rarity.SSS, Dere = Dere.Yami, Tags = new List<Tag>() } };
                db.CardExchanges.Add(new CardExchange { Date = day.AddHours(1), User1Id = Staff, User2Id = Player, User1Gave = 5, User2Gave = 0 });
                db.ScGifts.Add(new ScGift { Date = day.AddHours(1), FromUserId = Staff, ToUserId = Player, Sent = 100, Received = 60 });
                db.SaveChanges();
            }

            await _telemetry.SnapshotAsync(Open, day);

            using var check = Open();
            var stats = check.GameStats.AsNoTracking().Where(x => x.Date == day).ToList().ToDictionary(x => (x.Metric, x.Label), x => x.Value);
            Assert.Equal(1, stats[("players_active", "")]);
            Assert.Equal(30, stats[("circulation", "TC")]);
            Assert.Equal(30, stats[("balance", "TC:p99")]);
            Assert.Equal(1, stats[("cards_total", "SSS")]);
            Assert.Equal(0, stats[("exchanges", "count")]);
            Assert.Equal(0, stats[("sc_gift", "count")]);
        }

        [Fact]
        public async Task ACardDiscardedAsSoonAsDrawnCountsWithTheSave()
        {
            const ulong Staff = 51003;
            _telemetry.ExcludedUsers = () => new[] { Staff };
            using (var db = WithReason("pakiet"))
            {
                GameTelemetry.CountDiscardedNewCard(db, new Card { Rarity = Rarity.C, Source = CardSource.Activity }, Player);
                GameTelemetry.CountDiscardedNewCard(db, new Card { Rarity = Rarity.C, Source = CardSource.Activity }, Staff);
                // the command returned before saving
            }
            Assert.Empty(await StatsAsync());

            using (var db = WithReason("pakiet"))
            {
                GameTelemetry.CountDiscardedNewCard(db, new Card { Rarity = Rarity.D, Source = CardSource.Shop }, Player);
                GameTelemetry.CountDiscardedNewCard(db, new Card { Rarity = Rarity.D, Source = CardSource.Shop }, Staff);
                GameTelemetry.CountAfterSave(db, Player, "quest_claimed", "DPacket");
                db.Users.First(x => x.Id == Player).ScCnt += 1;
                db.SaveChanges();
                // counted once
                db.Users.First(x => x.Id == Player).ScCnt += 1;
                db.SaveChanges();
            }

            var stats = await StatsAsync();
            Assert.Equal(1, stats[("card_in", "D:Shop")]);
            Assert.Equal(1, stats[("card_out", "D:pakiet")]);
            Assert.Equal(1, stats[("quest_claimed", "DPacket")]);
            Assert.False(stats.ContainsKey(("card_in", "C:Activity")));
        }

        [Fact]
        public async Task ARestartIsNotAnUpgrade()
        {
            ulong cardId;
            using (var db = WithReason("x"))
            {
                var card = new Card { Character = 1, Name = "a", Title = "t", Rarity = Rarity.SSS, Dere = Dere.Yato, Tags = new List<Tag>() };
                db.GameDecks.First(x => x.Id == Player).Cards = new List<Card> { card };
                db.SaveChanges();
                cardId = card.Id;
            }
            using (var db = WithReason("reset"))
            {
                var card = db.Cards.First(x => x.Id == cardId);
                card.Rarity = Rarity.E;
                card.RestartCnt += 1;
                db.SaveChanges();
            }

            var stats = await StatsAsync();
            Assert.Equal(1, stats[("card_restart", "SSS>E")]);
            Assert.False(stats.ContainsKey(("card_upgrade", "SSS>E")));
        }

        [Fact]
        public async Task ARemovedPlayerTakesTheirCurrencyAndExpAway()
        {
            using (var db = WithReason("x"))
            {
                var user = db.Users.Include(x => x.GameDeck).ThenInclude(x => x.ExpContainer).First(x => x.Id == Player);
                user.GameDeck.CTCnt = 40;
                user.GameDeck.ExpContainer.ExpCount = 2.5;
                db.SaveChanges();
            }
            await StatsAsync();
            using (var db = Open())
            {
                db.GameStats.RemoveRange(db.GameStats);
                db.SaveChanges();
            }
            using (var db = WithReason("usuń"))
            {
                var user = db.Users.Include(x => x.GameDeck).ThenInclude(x => x.ExpContainer).First(x => x.Id == Player);
                var sc = user.ScCnt;
                db.Users.Remove(user);
                db.SaveChanges();

                var stats = await StatsAsync();
                Assert.Equal(sc, stats[("currency_out", "SC:usuń")]);
                Assert.Equal(40, stats[("currency_out", "CT:usuń")]);
                Assert.Equal(250, stats[("exp_out", "Chest:usuń")]);
                Assert.DoesNotContain(stats.Keys, x => x.Metric.StartsWith("karma"));
            }
        }

        [Fact]
        public async Task KarmaExpAndPlayerCountersAreCounted()
        {
            using (var db = WithReason("zniszcz"))
            {
                var user = db.Users.Include(x => x.Stats).Include(x => x.GameDeck).ThenInclude(x => x.ExpContainer).First(x => x.Id == Player);
                user.GameDeck.Karma -= 2.5;
                user.GameDeck.ExpContainer.ExpCount += 3;
                user.Stats.DestroyedCards += 2;
                user.GameDeck.Cards = new List<Card> { new Card { Character = 1, Name = "a", Title = "t", Rarity = Rarity.A, Dere = Dere.Yato, ExpCnt = 10.25, Tags = new List<Tag>() } };
                db.SaveChanges();
            }
            using (var db = WithReason("ulepsz"))
            {
                var card = db.Cards.First(x => x.GameDeckId == Player);
                card.ExpCnt = 0;
                db.GameDecks.First(x => x.Id == Player).Karma += 1;
                db.SaveChanges();
            }

            var stats = await StatsAsync();
            Assert.Equal(250, stats[("karma_out", "zniszcz")]);
            Assert.Equal(100, stats[("karma_in", "ulepsz")]);
            Assert.Equal(300, stats[("exp_in", "Chest:zniszcz")]);
            Assert.Equal(1025, stats[("exp_in", "Card:zniszcz")]);
            Assert.Equal(1025, stats[("exp_out", "Card:ulepsz")]);
            Assert.Equal(2, stats[("user_stat", "DestroyedCards:zniszcz")]);
        }

        [Fact]
        public async Task RefusalsAreCountedWithTheirReason()
        {
            _telemetry.ErrorReply(Player, "pakiet", "<@51001> masz już **Asuna Yuuki** i 1000 kart!\nDrugi wiersz");
            _telemetry.ErrorReply(Other, "pakiet", "<@51002> masz już **Kirito** i 200 kart!");

            var stats = await StatsAsync();
            Assert.Equal(2, stats[("command_error_reply", "pakiet")]);
            Assert.Equal(2, stats[("command_refusal", "pakiet:masz już * i # kart!")]);
        }

        [Fact]
        public async Task BackgroundWorkCanNameItsOwnReason()
        {
            GameTelemetry.Reason = "pakiet";
            var seen = "";
            var work = new Executable("safari", () => { seen = GameTelemetry.Reason; return Task.CompletedTask; }, reason: "auto:safari");

            await Task.Run(() => work.ExecuteAsync(null));

            Assert.Equal("auto:safari", seen);
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
