using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sanakan.Database;
using Sanakan.Database.Models;
using Sanakan.Database.Models.Analytics;
using Sanakan.Extensions;
using Sanakan.Services;
using Xunit;

namespace Artifacts
{
    public class LeftUserTests : IDisposable
    {
        private readonly SqliteConnection _connection = new SqliteConnection("Data Source=:memory:");
        private readonly DateTime _now = new FixedTime().Now();

        private sealed class TestDatabase : DatabaseContext
        {
            private readonly SqliteConnection _connection;
            public TestDatabase(SqliteConnection connection) : base(new FakeConfig()) => _connection = connection;
            protected override void OnConfiguring(DbContextOptionsBuilder builder) => builder.UseSqlite(_connection);
        }

        public LeftUserTests()
        {
            _connection.Open();
            using var db = Open();
            db.Database.EnsureCreated();
            db.Users.Add(new User().Default(1, _now));
            db.Users.Add(Player(10, null));
            db.WishlistCountData.Add(new WishlistCount { Id = 500, Count = 1, ACount = 1, Name = "A" });
            db.SaveChanges();
        }

        private TestDatabase Open() => new TestDatabase(_connection);
        public void Dispose() => _connection.Dispose();

        private DateTime Cutoff => _now - Greeting.LeftUserGracePeriod;

        private User Player(ulong id, DateTime? leftAt)
        {
            var user = new User().Default(id, _now);
            user.LeftAt = leftAt;
            user.TcCnt = 1234;
            user.GameDeck.Cards.Add(new Card { Id = id * 100, Character = 7, InCage = true, Tags = new[] { new Tag { Id = id * 100, Name = "ulubione", GameDeckId = id } }.ToList() });
            user.GameDeck.Wishes.Add(new WishlistObject { Type = WishlistObjectType.Character, ObjectId = 500, ObjectName = "A" });
            return user;
        }

        [Fact]
        public async Task Leaving_OnlyMarksTheUser()
        {
            using (var db = Open())
                await db.MarkUserLeftAsync(10, _now);

            using var read = Open();
            var user = await read.GetUserOrCreateAsync(10);
            Assert.Equal(_now, user.LeftAt);
            Assert.Equal(1234, user.TcCnt);
            Assert.Single(user.GameDeck.Cards);
            Assert.Equal(1, (await read.WishlistCountData.SingleAsync()).Count);
        }

        [Fact]
        public async Task Leaving_DoesNotCreateUnknownUser()
        {
            using (var db = Open())
                await db.MarkUserLeftAsync(99, _now);

            using var read = Open();
            Assert.False(await read.Users.AnyAsync(x => x.Id == 99));
        }

        [Fact]
        public async Task Rejoining_KeepsTheProfile()
        {
            using (var db = Open())
                await db.MarkUserLeftAsync(10, _now.AddDays(-40));

            using (var db = Open())
                Assert.True(await db.ClearUserLeftAsync(10));

            using (var db = Open())
            {
                Assert.False(await db.ClearUserLeftAsync(10));
                Assert.Empty(await db.GetUsersLeftBeforeAsync(Cutoff));
                Assert.False(await db.RemoveUserLeftBeforeAsync(10, Cutoff));
            }

            using var read = Open();
            var user = await read.GetUserOrCreateAsync(10);
            Assert.Null(user.LeftAt);
            Assert.Single(user.GameDeck.Cards);
        }

        [Fact]
        public async Task OnlyUsersPastGracePeriod_AreRemoved()
        {
            using (var db = Open())
            {
                db.Users.Add(Player(20, _now.AddDays(-29)));
                db.Users.Add(Player(30, _now.AddDays(-31)));
                await db.SaveChangesAsync();
            }

            using (var db = Open())
                Assert.Equal(new ulong[] { 30 }, await db.GetUsersLeftBeforeAsync(Cutoff));

            using (var db = Open())
                Assert.False(await db.RemoveUserLeftBeforeAsync(20, Cutoff));
            using (var db = Open())
                Assert.True(await db.RemoveUserLeftBeforeAsync(30, Cutoff));

            using var read = Open();
            Assert.True(await read.Users.AnyAsync(x => x.Id == 20));
            Assert.False(await read.Users.AnyAsync(x => x.Id == 30));

            var card = await read.Cards.AsQueryable().Include(x => x.Tags).SingleAsync(x => x.Id == 3000);
            Assert.Equal(1UL, card.GameDeckId);
            Assert.Equal(30UL, card.LastIdOwner);
            Assert.False(card.InCage);
            Assert.Empty(card.Tags);
            Assert.Equal(0, (await read.WishlistCountData.SingleAsync()).Count);
        }

        [Fact]
        public async Task RemovingWithCards_DeletesThem()
        {
            using (var db = Open())
                await db.RemoveUserAsync(10, removeCards: true);

            using var read = Open();
            Assert.False(await read.Users.AnyAsync(x => x.Id == 10));
            Assert.False(await read.Cards.AnyAsync(x => x.Id == 1000));
            Assert.Equal(0, (await read.WishlistCountData.SingleAsync()).Count);
        }

        [Theory]
        [InlineData(4, 1)]
        [InlineData(5, 24)]
        [InlineData(23, 6)]
        public void Cleanup_RunsDailyAtFive(int hour, int expectedHours)
        {
            Assert.Equal(TimeSpan.FromHours(expectedHours), Greeting.UntilNextCleanup(new DateTime(2026, 1, 1, hour, 0, 0)));
        }
    }
}
