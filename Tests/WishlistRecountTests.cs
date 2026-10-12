using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sanakan.Database;
using Sanakan.Database.Models;
using Sanakan.Database.Models.Analytics;
using Sanakan.Extensions;
using Sanakan.Services.PocketWaifu;
using Xunit;

namespace Artifacts
{
    public class WishlistRecountTests : IDisposable
    {
        private readonly SqliteConnection _connection = new SqliteConnection("Data Source=:memory:");
        private readonly DateTime _now = new FixedTime().Now();

        private sealed class TestDatabase : DatabaseContext
        {
            private readonly SqliteConnection _connection;
            public TestDatabase(SqliteConnection connection) : base(new FakeConfig()) => _connection = connection;
            protected override void OnConfiguring(DbContextOptionsBuilder builder) => builder.UseSqlite(_connection);
        }

        public WishlistRecountTests()
        {
            _connection.Open();
            using var db = Open();
            db.Database.EnsureCreated();
        }

        private TestDatabase Open() => new TestDatabase(_connection);
        public void Dispose() => _connection.Dispose();

        private User Player(ulong id, bool active, bool privateWishlist, params ulong[] characters)
        {
            var user = new User().Default(id, _now);
            user.MeasureDate = active ? _now : _now.AddMonths(-3);
            user.MessagesCnt = active ? 5UL : 0UL;
            user.MessagesCntAtDate = 0;
            user.GameDeck.LastSignificantActivity = _now.AddDays(-30);
            user.GameDeck.WishlistIsPrivate = privateWishlist;
            foreach (var character in characters)
                user.GameDeck.Wishes.Add(new WishlistObject { Type = WishlistObjectType.Character, ObjectId = character, ObjectName = "Character" });
            return user;
        }

        [Fact]
        public async Task Recount_FixesDriftedCountersFromPublicWishlists()
        {
            using (var db = Open())
            {
                db.Users.Add(Player(1, true, false, 100, 200));
                db.Users.Add(Player(2, false, false, 100));
                db.Users.Add(Player(3, true, true, 100));
                db.WishlistCountData.Add(new WishlistCount { Id = 100, Count = 9, ACount = 9, Name = "A" });
                db.WishlistCountData.Add(new WishlistCount { Id = 200, Count = 1, ACount = 1, Name = "B" });
                db.WishlistCountData.Add(new WishlistCount { Id = 300, Count = 4, ACount = 2, Name = "C" });
                await db.SaveChangesAsync();
            }

            using (var db = Open())
                Assert.Equal(2, await db.RecalculateWishlistCountsAsync(_now));

            using var read = Open();
            var first = await read.WishlistCountData.SingleAsync(x => x.Id == 100);
            var second = await read.WishlistCountData.SingleAsync(x => x.Id == 200);
            var third = await read.WishlistCountData.SingleAsync(x => x.Id == 300);
            Assert.Equal((2, 1), (first.Count, first.ACount));
            Assert.Equal((1, 1), (second.Count, second.ACount));
            Assert.Equal((0, 0), (third.Count, third.ACount));
        }

        [Theory]
        [InlineData(false, 1)]
        [InlineData(true, 2)]
        public async Task RemovingWish_ChangesCounterOnlyForPublicWishlist(bool privateWishlist, int expected)
        {
            using (var db = Open())
            {
                db.Users.Add(Player(1, true, privateWishlist, 100));
                db.WishlistCountData.Add(new WishlistCount { Id = 100, Count = 2, ACount = 2, Name = "A" });
                await db.SaveChangesAsync();
            }

            using (var db = Open())
            {
                var user = await db.GetUserOrCreateAsync(1);
                Assert.True(await user.GameDeck.RemoveCharacterFromWishListAsync(100, db));
                Assert.Empty(user.GameDeck.Wishes);
                await db.SaveChangesAsync();
            }

            using var read = Open();
            var count = await read.WishlistCountData.SingleAsync();
            Assert.Equal((expected, expected), (count.Count, count.ACount));
        }

        [Fact]
        public async Task TogglingPrivacy_KeepsCountersEqualToRecount()
        {
            using (var db = Open())
            {
                var user = Player(1, true, false, 100, 200);
                user.GameDeck.Wishes.Add(new WishlistObject { Type = WishlistObjectType.Title, ObjectId = 300, ObjectName = "Title" });
                db.Users.Add(user);
                db.Users.Add(Player(2, true, false, 100));
                db.WishlistCountData.Add(new WishlistCount { Id = 100, Count = 2, ACount = 2, Name = "A" });
                await db.SaveChangesAsync();
            }

            async Task SetPrivate(bool value)
            {
                using var db = Open();
                var user = await db.GetUserOrCreateAsync(1);
                await db.ChangeWishlistCountsAsync(user.GameDeck.Wishes, value ? -1 : 1);
                user.GameDeck.WishlistIsPrivate = value;
                await db.SaveChangesAsync();
            }

            await SetPrivate(true);
            using (var read = Open())
            {
                Assert.Equal((1, 1), await read.WishlistCountData.Where(x => x.Id == 100).Select(x => ValueTuple.Create(x.Count, x.ACount)).SingleAsync());
                Assert.Equal((0, 0), await read.WishlistCountData.Where(x => x.Id == 200).Select(x => ValueTuple.Create(x.Count, x.ACount)).SingleAsync());
                Assert.False(await read.WishlistCountData.AnyAsync(x => x.Id == 300));
                Assert.Equal(0, await read.RecalculateWishlistCountsAsync(_now));
            }

            await SetPrivate(false);
            using (var read = Open())
            {
                Assert.Equal((2, 2), await read.WishlistCountData.Where(x => x.Id == 100).Select(x => ValueTuple.Create(x.Count, x.ACount)).SingleAsync());
                Assert.Equal((1, 1), await read.WishlistCountData.Where(x => x.Id == 200).Select(x => ValueTuple.Create(x.Count, x.ACount)).SingleAsync());
                Assert.Equal(0, await read.RecalculateWishlistCountsAsync(_now));
            }
        }

        [Theory]
        [InlineData(3, 1)]
        [InlineData(4, 24)]
        [InlineData(5, 23)]
        public void NightlyRun_IsScheduledForNextFourAm(int hour, int expectedHours)
        {
            var now = new DateTime(2026, 10, 13, hour, 0, 0);
            Assert.Equal(TimeSpan.FromHours(expectedHours), WishlistRecount.UntilNextRun(now));
        }
    }
}
