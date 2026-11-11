using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Sanakan.Database;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Xunit;

namespace Artifacts
{
    // karta+ loads only the ultimate cards and the wish for the drawn character instead of the whole profile
    public class FreeCardLoadingTests : IDisposable
    {
        private const ulong Player = 10;

        private readonly FixedTime _time = new FixedTime();
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"sanakan-freecard-{Guid.NewGuid():N}.db");

        private sealed class TestDatabase : DatabaseContext
        {
            private readonly string _path;
            public TestDatabase(string path) : base(new FakeConfig()) => _path = path;
            protected override void OnConfiguring(DbContextOptionsBuilder builder) => builder.UseSqlite($"Data Source={_path}");
        }

        public FreeCardLoadingTests()
        {
            using var db = new TestDatabase(_path);
            db.Database.EnsureCreated();

            var user = new User().Default(Player, _time.Now());
            user.GameDeck.ExpContainer.Level = ExpContainerLevel.Level1;
            ulong id = 100;
            foreach (var (ultimate, quality) in new[] { (true, Quality.Broken), (true, Quality.Delta), (true, Quality.Theta), (false, Quality.Broken), (false, Quality.Omega) })
                user.GameDeck.Cards.Add(NewCard(++id, 50 + id, ultimate, quality));
            user.GameDeck.Wishes.Add(new WishlistObject { Type = WishlistObjectType.Character, ObjectId = 77, ObjectName = "a", Entry = WishlistEntryType.Normal });
            user.GameDeck.Wishes.Add(new WishlistObject { Type = WishlistObjectType.Character, ObjectId = 78, ObjectName = "b", Entry = WishlistEntryType.Persistent });
            user.GameDeck.Wishes.Add(new WishlistObject { Type = WishlistObjectType.Character, ObjectId = 79, ObjectName = "c", Entry = WishlistEntryType.Normal });
            user.GameDeck.Wishes.Add(new WishlistObject { Type = WishlistObjectType.Title, ObjectId = 77, ObjectName = "t", Entry = WishlistEntryType.Normal });
            db.Users.Add(user);
            db.SaveChanges();
        }

        public void Dispose()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(_path); } catch (IOException) { }
        }

        private static Card NewCard(ulong id, ulong character, bool ultimate = false, Quality quality = Quality.Broken) => new Card
        {
            Id = id, Character = character, Name = "x", Rarity = Rarity.A, FromFigure = ultimate, Quality = quality,
            ExpCnt = 40, MarketValue = 1, Tags = new List<Tag>(), CreationDate = new DateTime(2026, 1, 1)
        };

        private static Card NewDrawnCard(ulong character) => new Card
        {
            Character = character, Name = "drawn", Rarity = Rarity.A, ExpCnt = 40, MarketValue = 1, Tags = new List<Tag>(), CreationDate = new DateTime(2026, 1, 1)
        };

        [Fact]
        public async Task NarrowUser_GivesTheSameCooldownReductionAndCardCount_AsTheWholeProfile()
        {
            int fullReduction, fullCount;
            using (var db = new TestDatabase(_path))
            {
                var full = await db.GetUserOrCreateAsync(Player, _time);
                fullReduction = full.GameDeck.GetFreeCardCooldownReductionHours();
                fullCount = full.GameDeck.Cards.Count;
            }

            using (var db = new TestDatabase(_path))
            {
                var user = await db.GetUserOrCreateWithUltimateCardsAsync(Player, _time);

                Assert.Equal(3, user.GameDeck.Cards.Count);
                Assert.Equal(fullReduction, user.GameDeck.GetFreeCardCooldownReductionHours());
                Assert.Equal(fullCount, await db.CountCardsAsync(Player));
                Assert.Null(user.GameDeck.Wishes);
            }
        }

        [Theory]
        [InlineData(77ul, true, new ulong[] { 78, 79 })]
        [InlineData(78ul, true, new ulong[] { 77, 78, 79 })]
        [InlineData(99ul, false, new ulong[] { 77, 78, 79 })]
        public async Task DrawnCharacter_LeavesTheWishlistLikeTheWholeProfileWould(ulong character, bool wished, ulong[] charactersLeft)
        {
            using (var db = new TestDatabase(_path))
            {
                var user = await db.GetUserOrCreateWithUltimateCardsAsync(Player, _time);
                await db.LoadCharacterWishAsync(user.GameDeck, character);

                Assert.Equal(wished, await user.GameDeck.RemoveCharacterFromWishListAsync(character, db));
                user.GameDeck.Cards.Add(NewDrawnCard(character));
                await db.SaveChangesAsync();
            }

            using (var db = new TestDatabase(_path))
            {
                var wishes = await db.Wishes.AsNoTracking().Where(x => x.GameDeckId == Player).ToListAsync();
                Assert.Equal(charactersLeft, wishes.Where(x => x.Type == WishlistObjectType.Character).Select(x => x.ObjectId).OrderBy(x => x));
                Assert.Single(wishes, x => x.Type == WishlistObjectType.Title);
                Assert.Equal(6, await db.CountCardsAsync(Player));
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task DiscardingTheDrawnCard_UpdatesThePlayer_AndKeepsTheCollection(bool release)
        {
            using (var db = new TestDatabase(_path))
            {
                var user = await db.GetUserOrCreateWithUltimateCardsAsync(Player, _time);
                NewDrawnCard(99).DestroyOrRelease(user, release);
                await db.SaveChangesAsync();
            }

            using (var db = new TestDatabase(_path))
            {
                var user = await db.GetUserOrCreateAsync(Player, _time);
                Assert.Equal(release ? 1 : 0, user.Stats.ReleasedCards);
                Assert.Equal(release ? 0 : 1, user.Stats.DestroyedCards);
                Assert.True(user.GameDeck.ExpContainer.ExpCount > 0);
                Assert.Equal(5, user.GameDeck.Cards.Count);
                Assert.Equal(4, user.GameDeck.Wishes.Count);
            }
        }

        [Fact]
        public async Task NewPlayer_GetsTheCardWithoutAnyLookups()
        {
            using (var db = new TestDatabase(_path))
            {
                var user = await db.GetUserOrCreateWithUltimateCardsAsync(500, _time);
                Assert.Equal(_time.Now(), user.GameDeck.LastSignificantActivity);
                Assert.Equal(0, await db.CountCardsAsync(500));
                await db.LoadCharacterWishAsync(user.GameDeck, 77);

                Assert.False(await user.GameDeck.RemoveCharacterFromWishListAsync(77, db));
                user.GameDeck.Cards.Add(NewDrawnCard(77));
                await db.SaveChangesAsync();
            }

            using (var db = new TestDatabase(_path))
            {
                Assert.Equal(1, await db.CountCardsAsync(500));
                Assert.Equal(3, await db.Wishes.CountAsync(x => x.GameDeckId == Player && x.Type == WishlistObjectType.Character));
            }
        }
    }
}
