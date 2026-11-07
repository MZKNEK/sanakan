using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Sanakan;
using Sanakan.Database;
using Sanakan.Database.Models;
using Xunit;

namespace Artifacts
{
    public class QueryCacheTests
    {
        private sealed class TestDatabase : DatabaseContext
        {
            private readonly string _path;
            public TestDatabase(string path) : base(new FakeConfig()) => _path = path;
            protected override void OnConfiguring(DbContextOptionsBuilder builder) => builder.UseSqlite($"Data Source={_path};Foreign Keys=False");
        }

        private static string NewDb()
        {
            var path = Path.Combine(Path.GetTempPath(), $"sanakan-qcache-{Guid.NewGuid():N}.db");
            using var db = new TestDatabase(path);
            db.Database.EnsureCreated();
            return path;
        }

        private static void AddCard(string path, ulong id, ulong character)
        {
            using var db = new TestDatabase(path);
            db.Cards.Add(new Card { Id = id, Character = character, GameDeckId = 1 });
            db.SaveChanges();
        }

        private static async Task<int> CountAsync(string path, string key, params string[] tags)
        {
            using var db = new TestDatabase(path);
            return (await db.Cards.AsNoTracking().FromCacheAsync(db, key, tags)).Count();
        }

        private static string NewTag() => $"test-{Guid.NewGuid():N}";

        [Fact]
        public async Task ReturnsCachedResult_UntilTagExpires()
        {
            var path = NewDb();
            var tag = NewTag();
            AddCard(path, 1, 10);

            Assert.Equal(1, await CountAsync(path, tag, tag));
            AddCard(path, 2, 10);
            Assert.Equal(1, await CountAsync(path, tag, tag));

            QueryCache.ExpireTag(tag);
            Assert.Equal(2, await CountAsync(path, tag, tag));
        }

        [Fact]
        public async Task UnrelatedTag_DoesNotExpireEntry()
        {
            var path = NewDb();
            var tag = NewTag();
            AddCard(path, 1, 10);

            Assert.Equal(1, await CountAsync(path, tag, tag));
            AddCard(path, 2, 10);

            QueryCache.ExpireTag(NewTag());
            Assert.Equal(1, await CountAsync(path, tag, tag));
        }

        [Fact]
        public async Task OneTag_ExpiresEveryKeyUsingIt()
        {
            var path = NewDb();
            var shared = NewTag();
            AddCard(path, 1, 10);

            Assert.Equal(1, await CountAsync(path, $"{shared}-a", shared));
            Assert.Equal(1, await CountAsync(path, $"{shared}-b", shared, NewTag()));
            AddCard(path, 2, 10);

            QueryCache.ExpireTag(shared);
            Assert.Equal(2, await CountAsync(path, $"{shared}-a", shared));
            Assert.Equal(2, await CountAsync(path, $"{shared}-b", shared));
        }

        [Fact]
        public async Task SameKey_IsSeparatePerDatabase()
        {
            var first = NewDb();
            var second = NewDb();
            var key = NewTag();
            AddCard(first, 1, 10);

            Assert.Equal(1, await CountAsync(first, key));
            Assert.Equal(0, await CountAsync(second, key));
        }

        [Fact]
        public async Task TagExpiredBeforeAnyRead_StillCachesNextRead()
        {
            var path = NewDb();
            var tag = NewTag();
            AddCard(path, 1, 10);

            QueryCache.ExpireTag(tag);
            Assert.Equal(1, await CountAsync(path, tag, tag));
            AddCard(path, 2, 10);
            Assert.Equal(1, await CountAsync(path, tag, tag));
        }
    }
}
