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

        internal static string NewDb()
        {
            var path = Path.Combine(Path.GetTempPath(), $"sanakan-qcache-{Guid.NewGuid():N}.db");
            using var db = new TestDatabase(path);
            db.Database.EnsureCreated();
            return path;
        }

        internal static void AddCard(string path, ulong id, ulong character)
        {
            using var db = new TestDatabase(path);
            db.Cards.Add(new Card { Id = id, Character = character, GameDeckId = 1 });
            db.SaveChanges();
        }

        internal static async Task<int> CountAsync(string path, string key, params string[] tags)
        {
            using var db = new TestDatabase(path);
            return (await db.Cards.AsNoTracking().FromCacheAsync(db, key, tags)).Count();
        }

        internal static string NewTag() => $"test-{Guid.NewGuid():N}";

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

        [Theory]
        [InlineData("user-full-123", "user-full")]
        [InlineData("characters-cards-1,2,3", "characters-cards")]
        [InlineData("characters-cards-,5", "characters-cards")]
        [InlineData("characters-cards-", "characters-cards")]
        [InlineData("top-users-Level", "top-users-Level")]
        [InlineData("ultimate-cards", "ultimate-cards")]
        public void KindOf_DropsTheIdPart(string key, string kind)
            => Assert.Equal(kind, QueryCache.KindOf(key));

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

    // stats are global, so other tests must not record into them meanwhile
    [CollectionDefinition(nameof(QueryCacheStatsTests), DisableParallelization = true)]
    public class QueryCacheStatsCollection { }

    [Collection(nameof(QueryCacheStatsTests))]
    public class QueryCacheStatsTests
    {
        [Fact]
        public async Task Report_CountsHitsAndLoadsPerKind()
        {
            var path = QueryCacheTests.NewDb();
            var tag = QueryCacheTests.NewTag();
            QueryCacheTests.AddCard(path, 1, 10);
            QueryCacheStats.Flush();

            await QueryCacheTests.CountAsync(path, "statshit-1", tag);
            await QueryCacheTests.CountAsync(path, "statshit-1", tag);

            var row = Row(QueryCacheStats.Flush(), "statshit");
            Assert.NotNull(row);
            Assert.Equal(new[] { "statshit", "50%", "2", "1" }, row.Take(4));
            Assert.Equal("1", row[6]);
        }

        // query, hit%, reqs, loads, avg, max, rows, exp, inv, inv age
        private static string[] Row(string report, string kind)
            => (report ?? "").Split('\n').Select(x => x.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .FirstOrDefault(x => x.Length > 0 && x[0] == kind);

        [Fact]
        public async Task Report_CountsInvalidatedEntries()
        {
            var path = QueryCacheTests.NewDb();
            var tag = QueryCacheTests.NewTag();
            QueryCacheTests.AddCard(path, 1, 10);
            QueryCacheStats.Flush();

            await QueryCacheTests.CountAsync(path, "statsexpire-1", tag);
            QueryCache.ExpireTag(tag);

            // eviction callbacks run on the thread pool
            string[] row = null;
            for (int i = 0; i < 100 && row?[8] != "1"; i++)
            {
                await Task.Delay(20);
                row = Row(QueryCacheStats.Flush(), "statsexpire") ?? row;
            }

            Assert.NotNull(row);
            Assert.Equal("1", row[8]);
            Assert.EndsWith("s", row[9]);
        }

        [Fact]
        public async Task Report_ShowsWhatIsHeldInMemory_UntilEvicted()
        {
            var path = QueryCacheTests.NewDb();
            var tag = QueryCacheTests.NewTag();
            QueryCacheTests.AddCard(path, 1, 10);
            QueryCacheTests.AddCard(path, 2, 10);

            await QueryCacheTests.CountAsync(path, "statslive-1", tag);
            Assert.Equal((1, 2), QueryCacheStats.GetLive("statslive"));
            Assert.Contains("In memory: ", QueryCacheStats.Flush());

            QueryCache.ExpireTag(tag);
            for (int i = 0; i < 100 && QueryCacheStats.GetLive("statslive").Entries > 0; i++)
                await Task.Delay(20);

            Assert.Equal((0, 0), QueryCacheStats.GetLive("statslive"));
        }

        [Fact]
        public void Report_StaysWithinDiscordLimit()
        {
            QueryCacheStats.Flush();
            for (int i = 0; i < 50; i++)
            {
                var kind = $"statslong{new string((char)('a' + i % 26), 40)}{i}";
                QueryCacheStats.Load(kind, 123456, 99999, true);
                QueryCacheStats.Stored(kind, 0);
                QueryCacheStats.Evicted(kind, Microsoft.Extensions.Caching.Memory.EvictionReason.TokenExpired, 123456, 0);
            }

            var report = QueryCacheStats.Flush();
            Assert.True(report.Length < 1600, $"report has {report.Length} chars");
            Assert.Contains("more)", report);
            Assert.Contains("In memory: ", report);
        }
    }
}
