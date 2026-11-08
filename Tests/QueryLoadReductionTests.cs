using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Sanakan.Database;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services.PocketWaifu;
using Xunit;

namespace Artifacts
{
    public class QueryLoadReductionTests : IDisposable
    {
        private const ulong PlayerId = 71001;
        private const ulong OtherId = 71002;
        private const ulong ThirdId = 71003;
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"sanakan-load-{Guid.NewGuid():N}.db");
        private readonly DateTime _now = new DateTime(2026, 3, 10, 15, 0, 0);

        private sealed class TestDatabase : DatabaseContext
        {
            private readonly string _path;
            public TestDatabase(string path) : base(new FakeConfig()) => _path = path;
            protected override void OnConfiguring(DbContextOptionsBuilder builder)
                => builder.UseSqlite($"Data Source={_path};Foreign Keys=True").AddInterceptors(CacheActivity.Interceptor);
        }

        public QueryLoadReductionTests()
        {
            using var db = Open();
            db.Database.EnsureCreated();
        }

        public void Dispose()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(_path); } catch (IOException) { }
        }

        private TestDatabase Open() => new TestDatabase(_path);

        private void SeedUser(ulong id, int cards = 0, bool blacklisted = false, double deckPower = 0, int cardsInDeck = 0)
        {
            using var db = Open();
            var user = new User().Default(id, _now);
            user.IsBlacklisted = blacklisted;
            user.GameDeck.DeckPower = deckPower;
            user.GameDeck.CardsInDeck = cardsInDeck;
            for (int i = 0; i < cards; i++)
                user.GameDeck.Cards.Add(new Card { Name = $"card {i}", Character = (ulong)(i + 1) });
            db.Users.Add(user);
            db.SaveChanges();
        }

        private List<BoosterPack> Packs(ulong id)
        {
            using var db = Open();
            // every new player also gets a "Startowy pakiet"
            return db.BoosterPacks.AsQueryable().AsNoTracking().Where(x => x.GameDeckId == id && x.Name == "Pakiet kart za aktywność").ToList();
        }

        private TimeStatus PacketStatus(ulong id)
        {
            using var db = Open();
            return db.TimeStatuses.AsQueryable().AsNoTracking().SingleOrDefault(x => x.UserId == id && x.Type == StatusType.Packet);
        }

        private async Task<bool> GrantAsync(ulong id, DateTime now, long limit = 3)
        {
            using var db = Open();
            return await Spawn.TryGrantActivityPacketAsync(db, id, now, limit);
        }

        [Fact]
        public async Task ActivityPacket_IsAddedWithSameData_AndCardsAreUntouched()
        {
            SeedUser(PlayerId, cards: 3);

            Assert.True(await GrantAsync(PlayerId, _now));

            var pack = Assert.Single(Packs(PlayerId));
            Assert.Equal(2, pack.CardCnt);
            Assert.Equal(Rarity.E, pack.MinRarity);
            Assert.True(pack.IsCardFromPackTradable);
            Assert.Equal("Pakiet kart za aktywność", pack.Name);
            Assert.Equal(CardSource.Activity, pack.CardSourceFromPack);

            var status = PacketStatus(PlayerId);
            Assert.Equal(1, status.IValue);
            Assert.Equal(_now.Date.AddDays(1), status.EndsAt);

            using var db = Open();
            Assert.Equal(3, db.Cards.AsQueryable().Count(x => x.GameDeckId == PlayerId));
        }

        [Fact]
        public async Task ActivityPacket_StopsAtDailyLimit_WithoutSavingTheExtraAttempt()
        {
            SeedUser(PlayerId);

            for (int i = 0; i < 3; i++)
                Assert.True(await GrantAsync(PlayerId, _now.AddMinutes(i)));

            Assert.False(await GrantAsync(PlayerId, _now.AddMinutes(10)));
            Assert.Equal(3, Packs(PlayerId).Count);
            Assert.Equal(3, PacketStatus(PlayerId).IValue);
        }

        [Fact]
        public async Task ActivityPacket_CounterResetsNextDay()
        {
            SeedUser(PlayerId);
            for (int i = 0; i < 3; i++)
                await GrantAsync(PlayerId, _now);

            var tomorrow = _now.Date.AddDays(1).AddHours(8);
            Assert.True(await GrantAsync(PlayerId, tomorrow));

            Assert.Equal(4, Packs(PlayerId).Count);
            var status = PacketStatus(PlayerId);
            Assert.Equal(1, status.IValue);
            Assert.Equal(tomorrow.Date.AddDays(1), status.EndsAt);
        }

        [Fact]
        public async Task ActivityPacket_BlacklistedPlayerGetsNothing()
        {
            SeedUser(PlayerId, blacklisted: true);

            Assert.False(await GrantAsync(PlayerId, _now));
            Assert.Empty(Packs(PlayerId));
            Assert.Null(PacketStatus(PlayerId));
        }

        [Fact]
        public async Task ActivityPacket_CreatesMissingPlayer()
        {
            Assert.True(await GrantAsync(PlayerId, _now));

            using var db = Open();
            Assert.True(db.Users.AsQueryable().Any(x => x.Id == PlayerId));
            Assert.Single(Packs(PlayerId));
        }

        [Fact]
        public async Task ActivityPacket_ExpiresCachedFullPlayer()
        {
            SeedUser(PlayerId, cards: 2);
            int before;
            using (var db = Open())
                before = (await db.GetCachedFullUserAsync(PlayerId)).GameDeck.BoosterPacks.Count;

            Assert.True(await GrantAsync(PlayerId, _now));

            using (var db = Open())
                Assert.Equal(before + 1, (await db.GetCachedFullUserAsync(PlayerId)).GameDeck.BoosterPacks.Count);
        }

        [Fact]
        public async Task PvpPlayers_SharedCacheStillExcludesEachCaller()
        {
            SeedUser(PlayerId, deckPower: 500, cardsInDeck: 3);
            SeedUser(OtherId, deckPower: 800, cardsInDeck: 5);
            SeedUser(ThirdId, deckPower: 900, cardsInDeck: 4);
            SeedUser(71004, deckPower: 100, cardsInDeck: 3);
            SeedUser(71005, deckPower: 600, cardsInDeck: 9);
            SeedUser(71006, deckPower: 600, cardsInDeck: 0);

            using var db = Open();
            var forPlayer = (await db.GetCachedPlayersForPVP(PlayerId)).Select(x => x.UserId).OrderBy(x => x).ToList();
            var forOther = (await db.GetCachedPlayersForPVP(OtherId)).Select(x => x.UserId).OrderBy(x => x).ToList();
            var forDefault = (await db.GetCachedPlayersForPVP()).Select(x => x.UserId).OrderBy(x => x).ToList();

            Assert.Equal(new[] { OtherId, ThirdId }, forPlayer);
            Assert.Equal(new[] { PlayerId, ThirdId }, forOther);
            Assert.Equal(new[] { PlayerId, OtherId, ThirdId }, forDefault);
        }

        private void SeedLotteryCards(ulong giverId, int count, Rarity rarity = Rarity.A, string customImage = null, bool fromFigure = false, int whoWants = 0, string name = "lottery")
        {
            using var db = Open();
            var tag = new Tag { Name = $"t{giverId}", GameDeckId = giverId };
            db.Tags.Add(tag);
            for (int i = 0; i < count; i++)
            {
                db.Cards.Add(new Card
                {
                    Name = $"{name} {i}", Character = (ulong)(100 + i), GameDeckId = giverId, Rarity = rarity, WhoWantsCount = whoWants,
                    Active = true, InCage = true, Expedition = CardExpedition.NormalItemWithExp, Tags = new List<Tag> { tag },
                    CustomImage = customImage, FromFigure = fromFigure,
                });
            }
            db.SaveChanges();
        }

        private List<Card> CardsOf(ulong id)
        {
            using var db = Open();
            return db.Cards.AsQueryable().AsNoTracking().Include(x => x.Tags).Where(x => x.GameDeckId == id).ToList();
        }

        private async Task<(List<string> Cards, int Scalpels, int Ultimates, bool AnyWishlist)?> LotteryAsync(ulong giverId, ulong winnerId, uint count)
        {
            using var db = Open();
            return await Sanakan.Modules.Debug.TransferLotteryCardsAsync(db, giverId, Users.Discord(winnerId), count, new FixedTime { Value = _now });
        }

        [Fact]
        public async Task Lottery_MovesDrawnCardsWithClearedState_AndUpdatesWinnerWishlist()
        {
            SeedUser(PlayerId);
            SeedUser(OtherId, cards: 2);
            SeedLotteryCards(PlayerId, 4);
            using (var db = Open())
            {
                db.Wishes.Add(new WishlistObject { GameDeckId = OtherId, Type = WishlistObjectType.Character, ObjectId = 101, ObjectName = "x" });
                db.WishlistCountData.Add(new Sanakan.Database.Models.Analytics.WishlistCount { Id = 101, Name = "x", Count = 3, ACount = 3 });
                db.SaveChanges();
            }

            var won = await LotteryAsync(PlayerId, OtherId, 4);

            Assert.NotNull(won);
            Assert.Equal(4, won.Value.Cards.Count);
            Assert.Equal(0, won.Value.Scalpels);
            Assert.Equal(0, won.Value.Ultimates);
            Assert.True(won.Value.AnyWishlist);
            Assert.Empty(CardsOf(PlayerId));

            var winnerCards = CardsOf(OtherId);
            Assert.Equal(6, winnerCards.Count);
            foreach (var card in winnerCards.Where(x => x.Name.StartsWith("lottery")))
            {
                Assert.False(card.Active);
                Assert.False(card.InCage);
                Assert.Equal(CardExpedition.None, card.Expedition);
                Assert.Empty(card.Tags);
            }

            using var read = Open();
            Assert.False(read.Wishes.AsQueryable().Any(x => x.GameDeckId == OtherId && x.ObjectId == 101));
            Assert.Equal(2, read.WishlistCountData.AsQueryable().Single(x => x.Id == 101).Count);
            Assert.Equal(1, read.UserActivities.AsQueryable().Count(x => x.Type == ActivityType.WonLottery));
            Assert.Equal(1, read.UserActivities.AsQueryable().Count(x => x.Type == ActivityType.AcquiredCardWishlist));
        }

        [Fact]
        public async Task Lottery_MovesExactlyRequestedCount()
        {
            SeedUser(PlayerId);
            SeedUser(OtherId);
            SeedLotteryCards(PlayerId, 10);

            var won = await LotteryAsync(PlayerId, OtherId, 3);

            Assert.Equal(3, won.Value.Cards.Count);
            Assert.Equal(7, CardsOf(PlayerId).Count);
            Assert.Equal(3, CardsOf(OtherId).Select(x => x.Id).Distinct().Count());
        }

        [Fact]
        public async Task Lottery_MarksScalpelsAndUltimatesButNotSSS()
        {
            SeedUser(PlayerId);
            SeedUser(OtherId);
            SeedLotteryCards(PlayerId, 1, Rarity.SSS);
            SeedLotteryCards(PlayerId, 2, customImage: "https://cdn.example/scalpel.png");
            SeedLotteryCards(PlayerId, 1, fromFigure: true);

            var won = (await LotteryAsync(PlayerId, OtherId, 4)).Value;

            Assert.Equal(2, won.Scalpels);
            Assert.Equal(1, won.Ultimates);
            Assert.False(won.AnyWishlist);
            Assert.Equal(3, won.Cards.Count(x => x.StartsWith(Sanakan.Modules.Debug.LotterySpecialMark)));
        }

        [Fact]
        public async Task Lottery_PutsSpecialAndMostWantedCardsFirst()
        {
            SeedUser(PlayerId);
            SeedUser(OtherId);
            SeedLotteryCards(PlayerId, 1, whoWants: 1, name: "malo");
            SeedLotteryCards(PlayerId, 1, whoWants: 9, name: "duzo");
            SeedLotteryCards(PlayerId, 1, customImage: "https://cdn.example/s.png", name: "skalpel");
            SeedLotteryCards(PlayerId, 1, customImage: "https://cdn.example/u.png", fromFigure: true, name: "oba");

            var cards = (await LotteryAsync(PlayerId, OtherId, 4)).Value.Cards;

            Assert.Contains("oba", cards[0]);
            Assert.Contains("skalpel", cards[1]);
            Assert.Contains("duzo", cards[2]);
            Assert.Contains("malo", cards[3]);
        }

        [Theory]
        [InlineData(0, 0, "🎉 Loteria rozstrzygnięta")]
        [InlineData(1, 0, "🌟 Loteria rozstrzygnięta: skalpel!")]
        [InlineData(2, 1, "🌟 Loteria rozstrzygnięta: 2 skalpele i karta ultimate!")]
        [InlineData(0, 5, "🌟 Loteria rozstrzygnięta: 5 kart ultimate!")]
        public void LotteryResultTitle_HighlightsScalpelsAndUltimates(int scalpels, int ultimates, string expected)
        {
            Assert.Equal(expected, Sanakan.Modules.Debug.LotteryResultTitle(scalpels, ultimates));
        }

        [Fact]
        public async Task Lottery_GiverWithoutCards_ChangesNothing()
        {
            SeedUser(PlayerId);

            Assert.Null(await LotteryAsync(PlayerId, OtherId, 3));

            using var db = Open();
            Assert.False(db.Users.AsQueryable().Any(x => x.Id == OtherId));
            Assert.False(db.UserActivities.AsQueryable().Any());
        }

        [Fact]
        public async Task Lottery_ExpiresCachedProfilesOfBothPlayers()
        {
            SeedUser(PlayerId);
            SeedUser(OtherId);
            SeedLotteryCards(PlayerId, 2);
            using (var db = Open())
            {
                Assert.Equal(2, (await db.GetCachedFullUserAsync(PlayerId)).GameDeck.Cards.Count);
                Assert.Empty((await db.GetCachedFullUserAsync(OtherId)).GameDeck.Cards);
            }

            await LotteryAsync(PlayerId, OtherId, 2);

            using (var db = Open())
            {
                Assert.Empty((await db.GetCachedFullUserAsync(PlayerId)).GameDeck.Cards);
                Assert.Equal(2, (await db.GetCachedFullUserAsync(OtherId)).GameDeck.Cards.Count);
            }
        }

        [Fact]
        public async Task TagCounts_MatchPerTagCounting()
        {
            SeedUser(PlayerId);
            SeedUser(OtherId);

            using (var db = Open())
            {
                var own1 = new Tag { Id = 501, Name = "a", GameDeckId = PlayerId };
                var own2 = new Tag { Id = 502, Name = "b", GameDeckId = PlayerId };
                var empty = new Tag { Id = 503, Name = "c", GameDeckId = PlayerId };
                var shared = new Tag { Id = 504, Name = "ulubione", GameDeckId = OtherId };
                db.Tags.AddRange(own1, own2, empty, shared);
                db.Cards.AddRange(
                    new Card { Name = "1", GameDeckId = PlayerId, Tags = new List<Tag> { own1, shared } },
                    new Card { Name = "2", GameDeckId = PlayerId, Tags = new List<Tag> { own1 } },
                    new Card { Name = "3", GameDeckId = PlayerId, Tags = new List<Tag> { own2, shared } },
                    new Card { Name = "4", GameDeckId = PlayerId, Tags = new List<Tag>() },
                    new Card { Name = "5", GameDeckId = OtherId, Tags = new List<Tag> { shared } });
                db.SaveChanges();
            }

            var ids = new ulong[] { 501, 502, 503, 504, 999 };
            using var read = Open();
            var counts = await read.CountUserCardsByTagsAsync(PlayerId, ids);

            foreach (var id in ids)
            {
                var expected = await read.Cards.AsQueryable().AsNoTracking().CountAsync(x => x.GameDeckId == PlayerId && x.Tags.Any(t => t.Id == id));
                Assert.Equal(expected, counts.TryGetValue(id, out var count) ? count : 0);
            }
            Assert.Equal(2, counts[501]);
            Assert.Equal(1, counts[502]);
            Assert.Equal(2, counts[504]);
            Assert.False(counts.ContainsKey(503));
        }
    }
}
