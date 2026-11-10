using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Sanakan;
using Sanakan.Database;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services;
using Xunit;

namespace Artifacts
{
    public class DatabaseLoadTests
    {
        private static readonly DateTime Now = new FixedTime().Now();

        private sealed class TagCapture : SaveChangesInterceptor
        {
            public List<string> Tags { get; private set; }

            public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
                InterceptionResult<int> result, CancellationToken cancellationToken = default)
            {
                Tags = ((DatabaseContext)eventData.Context).PendingCacheTags?.ToList() ?? new List<string>();
                return new ValueTask<InterceptionResult<int>>(result);
            }
        }

        private sealed class TestDatabase : DatabaseContext
        {
            private readonly string _path;
            private readonly TagCapture _capture;

            public TestDatabase(string path, TagCapture capture = null) : base(new FakeConfig())
            {
                _path = path;
                _capture = capture;
            }

            protected override void OnConfiguring(DbContextOptionsBuilder builder)
            {
                builder.UseSqlite($"Data Source={_path};Foreign Keys=False");
                if (_capture != null)
                    builder.AddInterceptors(CacheActivity.Interceptor, _capture);
            }
        }

        private static string NewDb()
        {
            var path = Path.Combine(Path.GetTempPath(), $"sanakan-load-{Guid.NewGuid():N}.db");
            using var db = new TestDatabase(path);
            db.Database.EnsureCreated();
            return path;
        }

        private static Card NewCard(ulong id, Rarity rarity, double power)
            => new Card { Id = id, Character = id, Name = $"karta {id}", Rarity = rarity, CardPower = power };

        private static User Player(ulong id, long exp, double karma, params Card[] cards)
        {
            var user = new User().Default(id, Now);
            user.ExpCnt = exp;
            user.GameDeck.Karma = karma;
            foreach (var card in cards)
                user.GameDeck.Cards.Add(card);
            return user;
        }

        private static string SeedTop()
        {
            var path = NewDb();
            using var db = new TestDatabase(path);
            // the removed-cards account has the most and the strongest cards, but must never be ranked
            db.Users.Add(Player(1, 999999, 999, Enumerable.Range(0, 8).Select(i => NewCard(100 + (ulong)i, Rarity.SS, 900 + i)).ToArray()));
            db.Users.Add(Player(10, 300, 1.5, NewCard(1001, Rarity.A, 50), NewCard(1002, Rarity.A, 10), NewCard(1003, Rarity.E, 5)));
            db.Users.Add(Player(20, 100, -2, NewCard(2001, Rarity.SS, 400), NewCard(2002, Rarity.SS, 399)));
            db.Users.Add(Player(30, 200, 7.25, NewCard(3001, Rarity.B, 30), NewCard(3002, Rarity.C, 20), NewCard(3003, Rarity.D, 15), NewCard(3004, Rarity.B, 1)));
            db.Users.Add(Player(40, 50, 0));
            db.SaveChanges();
            return path;
        }

        // the way the top was built before: full collections loaded and ordered in memory
        private static List<TopEntry> ExpectedTop(string path, TopType type)
        {
            using var db = new TestDatabase(path);
            var users = db.Users.AsQueryable().Include(x => x.GameDeck).ThenInclude(x => x.Cards).AsNoTracking().ToList()
                .Where(x => x.Id != 1);

            users = type switch
            {
                TopType.Cards => users.OrderByDescending(x => x.GameDeck.Cards.Count),
                TopType.CardsPower => users.OrderByDescending(x => x.GameDeck.Cards.Sum(c => c.CardPower)),
                TopType.Card => users.OrderByDescending(x => x.GameDeck.Cards.Select(c => (double?)c.CardPower).Max() ?? double.MinValue),
                TopType.Karma => users.OrderByDescending(x => x.GameDeck.Karma),
                _ => users.OrderByDescending(x => x.ExpCnt),
            };

            return users.Select(x => new TopEntry(x.Id, x.GetViewValueForTop(type))).ToList();
        }

        private static Profile NewProfile() => new Profile(null, null, null, new ListLogger(), new FakeConfig(), new FixedTime(), null);

        [Theory]
        [InlineData(TopType.Level)]
        [InlineData(TopType.Karma)]
        [InlineData(TopType.Cards)]
        [InlineData(TopType.CardsPower)]
        [InlineData(TopType.Card)]
        public async Task Top_MatchesFullCollectionResult_WithoutRemovedCardsAccount(TopType type)
        {
            var path = SeedTop();

            using var db = new TestDatabase(path);
            var top = await NewProfile().GetTopUsers(db, type);

            Assert.Equal(ExpectedTop(path, type), top);
            Assert.DoesNotContain(top, x => x.UserId == 1);
        }

        [Fact]
        public async Task Top_ShowsValuesReadableForPlayers()
        {
            var path = SeedTop();
            using var db = new TestDatabase(path);
            var profile = NewProfile();

            Assert.Equal("4", (await profile.GetTopUsers(db, TopType.Cards)).First().Value);
            Assert.Equal("**SS**: 2 ", (await profile.GetTopUsers(db, TopType.CardsPower)).First().Value);
            Assert.Contains("karta 2001", (await profile.GetTopUsers(db, TopType.Card)).First().Value);
            Assert.Equal("---", (await profile.GetTopUsers(db, TopType.Card)).Last().Value);
        }

        [Fact]
        public void CardCountStats_KeepsRarityOrderAndSkipsMissing()
        {
            var deck = new GameDeck { Cards = new List<Card> { NewCard(1, Rarity.E, 0), NewCard(2, Rarity.SSS, 0), NewCard(3, Rarity.E, 0) } };

            Assert.Equal("**SSS**: 1 **E**: 2 ", deck.GetCardCountStats());
            Assert.Equal("", UserExtension.FormatCardCountStats(new Dictionary<Rarity, int>()));
        }

        [Fact]
        public async Task RemovingUser_MovesCardsWithoutLoadingRemovedCardsAccountCollection()
        {
            var path = NewDb();
            using (var seed = new TestDatabase(path))
            {
                seed.Users.Add(Player(1, 0, 0, NewCard(500, Rarity.A, 1), NewCard(501, Rarity.A, 1)));
                seed.Users.Add(Player(30, 0, 0, NewCard(3000, Rarity.B, 1)));
                seed.SaveChanges();
            }

            using (var db = new TestDatabase(path))
            {
                await db.RemoveUserAsync(30);
                Assert.DoesNotContain(db.ChangeTracker.Entries<Card>(), x => x.Entity.Id == 500 || x.Entity.Id == 501);
            }

            using var read = new TestDatabase(path);
            Assert.False(await read.Users.AnyAsync(x => x.Id == 30));
            Assert.Equal(new ulong[] { 500, 501, 3000 }, (await read.Cards.AsQueryable().Where(x => x.GameDeckId == 1).Select(x => x.Id).ToListAsync()).OrderBy(x => x));
            Assert.Equal(30UL, (await read.Cards.AsQueryable().SingleAsync(x => x.Id == 3000)).LastIdOwner);
        }

        [Fact]
        public async Task RemovingUser_CreatesRemovedCardsAccountWhenMissing()
        {
            var path = NewDb();
            using (var seed = new TestDatabase(path))
            {
                seed.Users.Add(Player(30, 0, 0, NewCard(3000, Rarity.B, 1)));
                seed.SaveChanges();
            }

            using (var db = new TestDatabase(path))
                await db.RemoveUserAsync(30);

            using var read = new TestDatabase(path);
            Assert.True(await read.Users.AnyAsync(x => x.Id == 1));
            Assert.Equal(1UL, (await read.Cards.AsQueryable().SingleAsync(x => x.Id == 3000)).GameDeckId);
        }

        private static async Task<List<string>> TagsOfSaveAsync(string path, Action<TestDatabase> change)
        {
            var capture = new TagCapture();
            using var db = new TestDatabase(path, capture);
            change(db);
            await db.SaveChangesAsync();
            return capture.Tags;
        }

        private static string SeedCards()
        {
            var path = NewDb();
            using var db = new TestDatabase(path);
            db.Cards.Add(new Card { Id = 1, Character = 1, GameDeckId = 10 });
            db.Cards.Add(new Card { Id = 2, Character = 2, GameDeckId = 10, FromFigure = true });
            db.Cards.Add(new Card { Id = 3, Character = 3, GameDeckId = 10, Unique = true });
            db.SaveChanges();
            return path;
        }

        [Fact]
        public async Task OrdinaryCard_DoesNotExpireUltimateOrUniqueLists()
        {
            var path = SeedCards();

            var added = await TagsOfSaveAsync(path, db => db.Cards.Add(new Card { Id = 9, Character = 9, GameDeckId = 10 }));
            var removed = await TagsOfSaveAsync(path, db => db.Cards.Remove(db.Cards.Single(x => x.Id == 1)));
            var moved = await TagsOfSaveAsync(path, db => db.Cards.Single(x => x.Id == 9).GameDeckId = 20);

            foreach (var tags in new[] { added, removed, moved })
            {
                Assert.DoesNotContain(CacheTags.UltimateCards, tags);
                Assert.DoesNotContain(CacheTags.UniqueCards, tags);
            }
            Assert.Contains(CacheTags.User(10), added);
        }

        [Fact]
        public async Task UltimateCard_ExpiresOnlyUltimateList()
        {
            var path = SeedCards();

            var added = await TagsOfSaveAsync(path, db => db.Cards.Add(new Card { Id = 9, Character = 9, GameDeckId = 10, FromFigure = true }));
            var moved = await TagsOfSaveAsync(path, db => db.Cards.Single(x => x.Id == 2).GameDeckId = 20);
            var removed = await TagsOfSaveAsync(path, db => db.Cards.Remove(db.Cards.Single(x => x.Id == 9)));
            var flagged = await TagsOfSaveAsync(path, db => db.Cards.Single(x => x.Id == 1).FromFigure = true);
            var powered = await TagsOfSaveAsync(path, db => db.Cards.Single(x => x.Id == 2).CardPower = 123);

            foreach (var tags in new[] { added, moved, removed, flagged, powered })
            {
                Assert.Contains(CacheTags.UltimateCards, tags);
                Assert.DoesNotContain(CacheTags.UniqueCards, tags);
            }
        }

        [Fact]
        public async Task UniqueCard_ExpiresOnlyUniqueList()
        {
            var path = SeedCards();

            var added = await TagsOfSaveAsync(path, db => db.Cards.Add(new Card { Id = 9, Character = 9, GameDeckId = 10, Unique = true }));
            var moved = await TagsOfSaveAsync(path, db => db.Cards.Single(x => x.Id == 3).GameDeckId = 20);
            var powered = await TagsOfSaveAsync(path, db => db.Cards.Single(x => x.Id == 3).CardPower = 123);
            var unflagged = await TagsOfSaveAsync(path, db => db.Cards.Single(x => x.Id == 3).Unique = false);

            foreach (var tags in new[] { added, moved, powered, unflagged })
            {
                Assert.Contains(CacheTags.UniqueCards, tags);
                Assert.DoesNotContain(CacheTags.UltimateCards, tags);
            }
        }

        [Fact]
        public void CacheSize_CountsCardsOfUsersAndDecks()
        {
            var user = new User().Default(5, Now);
            user.GameDeck.Cards.Add(new Card());
            user.GameDeck.Cards.Add(new Card());

            Assert.Equal(3, QueryCache.SizeOf(new List<User> { user }));
            Assert.Equal(4, QueryCache.SizeOf(new List<GameDeck> { new GameDeck { Cards = new List<Card> { new Card(), new Card(), new Card() } } }));
            Assert.Equal(2, QueryCache.SizeOf(new List<User> { new User() }) + QueryCache.SizeOf(new List<int>()));
            Assert.Equal(3, QueryCache.SizeOf(new List<string> { "a", "b", "c" }));
        }

        [Fact]
        public async Task CacheEntryOverLimit_IsNotKept()
        {
            var path = NewDb();
            using var db = new TestDatabase(path);
            var key = $"over-limit-{Guid.NewGuid():N}";
            var loads = 0;

            Task<List<int>> Load()
            {
                loads++;
                return Task.FromResult(new List<int>(new int[QueryCache.SizeLimit + 1]));
            }

            Assert.Equal(QueryCache.SizeLimit + 1, (await QueryCache.GetOrLoadAsync(db, key, Load, null)).Count);
            await QueryCache.GetOrLoadAsync(db, key, Load, null);
            Assert.Equal(2, loads);
        }

        [Fact]
        public async Task CacheEntryWithinLimit_IsKept()
        {
            var path = NewDb();
            using var db = new TestDatabase(path);
            var key = $"within-limit-{Guid.NewGuid():N}";
            var loads = 0;

            Task<List<int>> Load()
            {
                loads++;
                return Task.FromResult(new List<int> { 1, 2, 3 });
            }

            var first = await QueryCache.GetOrLoadAsync(db, key, Load, null);
            var second = await QueryCache.GetOrLoadAsync(db, key, Load, null);
            Assert.Same(first, second);
            Assert.Equal(1, loads);
        }

        [Fact]
        public async Task LightUser_LoadsStatusesAndDeckRowWithoutCollections_AndCreatesMissingUser()
        {
            var path = NewDb();
            using (var db = new TestDatabase(path))
            {
                var player = Player(10, 0, 0, NewCard(1001, Rarity.A, 50));
                player.TimeStatuses.Add(StatusType.Daily.NewTimeStatus());
                player.GameDeck.PVPCoins = 70;
                player.GameDeck.Items.Add(new Item { Name = "Tort", Type = ItemType.AffectionRecoveryBig, Count = 3 });
                player.GameDeck.Wishes.Add(new WishlistObject { Type = WishlistObjectType.Character, ObjectId = 5, ObjectName = "x" });
                db.Users.Add(player);
                db.SaveChanges();
            }

            using (var db = new TestDatabase(path))
            {
                var user = await db.GetUserOrCreateLightAsync(10);

                Assert.Single(user.TimeStatuses);
                Assert.NotNull(user.Stats);
                Assert.NotNull(user.SMConfig);
                Assert.Equal(70, user.GameDeck.PVPCoins);
                Assert.Null(user.GameDeck.Items);
                Assert.Null(user.GameDeck.Wishes);
                Assert.Null(user.GameDeck.Cards);

                var created = await db.GetUserOrCreateLightAsync(30);
                Assert.Equal(EntityState.Added, db.Entry(created).State);
                await db.SaveChangesAsync();
            }

            using (var db = new TestDatabase(path))
                Assert.True(await db.Users.AnyAsync(x => x.Id == 30));
        }

        [Fact]
        public async Task UserWithItems_LoadsOnlyTheItemsOfTheDeck()
        {
            var path = NewDb();
            using (var db = new TestDatabase(path))
            {
                var player = Player(10, 0, 0, NewCard(1001, Rarity.A, 50), NewCard(1002, Rarity.B, 10));
                player.GameDeck.Items.Add(new Item { Name = "Tort", Type = ItemType.AffectionRecoveryBig, Count = 3 });
                player.GameDeck.Items.Add(new Item { Name = "Lizak", Type = ItemType.AffectionRecoverySmall, Count = 7 });
                db.Users.Add(player);
                db.Users.Add(Player(20, 0, 0));
                db.SaveChanges();
            }

            using (var db = new TestDatabase(path))
            {
                var user = await db.GetUserWithItemsAsync(10);

                Assert.Equal(new[] { ItemType.AffectionRecoverySmall, ItemType.AffectionRecoveryBig }, user.GetAllItems().Select(x => x.Type));
                Assert.Equal(new long[] { 7, 3 }, user.GetAllItems().Select(x => x.Count));
                Assert.True(user.GameDeck.Cards == null || user.GameDeck.Cards.Count == 0);
                Assert.Empty((await db.GetUserWithItemsAsync(20)).GameDeck.Items);
                Assert.Null(await db.GetUserWithItemsAsync(30));
            }
        }
    }
}
