using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Sanakan;
using Sanakan.Api.Controllers;
using Sanakan.Api.Models;
using Sanakan.Database;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Xunit;
using Z.EntityFramework.Plus;

namespace Artifacts
{
    public class CachedCardListTests : IDisposable
    {
        private const ulong DiscordId = 93001;
        private const ulong TagId = 600;

        private readonly string _path = Path.Combine(Path.GetTempPath(), $"sanakan-cardlist-{Guid.NewGuid():N}.db");
        // cache entries expire relative to the real clock
        private readonly FixedTime _time = new FixedTime { Value = DateTime.Now };

        private sealed class TestDatabase : DatabaseContext
        {
            private readonly string _path;
            public TestDatabase(string path) : base(new FakeConfig()) => _path = path;
            protected override void OnConfiguring(DbContextOptionsBuilder builder) => builder.UseSqlite($"Data Source={_path}");
        }

        private sealed class TestController : WaifuController
        {
            private readonly string _path;
            public TestController(string path, FixedTime time) : base(null, null, new FakeExecutor(), null, new FakeConfig(), time, null, null, null)
                => _path = path;
            protected override DatabaseContext CreateDatabaseContext() => new TestDatabase(_path);
        }

        public CachedCardListTests()
        {
            QueryCacheManager.ExpireTag(CacheTags.UltimateCards, CacheTags.UniqueCards);

            using var db = new TestDatabase(_path);
            db.Database.EnsureCreated();
            db.Users.Add(new User().Default(DiscordId, _time.Now()));
            var tag = new Tag { Id = TagId, Name = "ulubione", GameDeckId = DiscordId };
            db.Cards.AddRange(
                Ultimate("Żółta Róża", "Seria A", new List<Tag> { tag }),
                Ultimate("Zielony", null),
                Ultimate("Biały", "Inna seria"),
                new Card { Name = "Róża zwykła", Title = "Seria A", GameDeckId = DiscordId, Unique = true, Tags = new List<Tag>() });
            db.SaveChanges();
        }

        public void Dispose()
        {
            QueryCacheManager.ExpireTag(CacheTags.UltimateCards, CacheTags.UniqueCards);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(_path); } catch (IOException) { }
        }

        private static Card Ultimate(string name, string title, List<Tag> tags = null)
            => new Card { Name = name, Title = title, GameDeckId = DiscordId, FromFigure = true, Tags = tags ?? new List<Tag>() };

        private async Task<FilteredCards> Ultimate(CardsQueryFilter filter, uint offset = 0, uint count = 10)
            => (await ControllerHarness.Attach(new TestController(_path, _time)).GetUltimateCardsWithOffsetAndFilterAsync(offset, count, filter)).Value;

        private static List<string> Names(FilteredCards cards) => cards.Cards.Select(x => x.Name).ToList();

        [Fact]
        public async Task Search_IgnoresCaseAndAccents_AndSkipsMissingTitles()
        {
            var res = await Ultimate(new CardsQueryFilter { SearchText = "ROZA" });

            Assert.Equal(new[] { "Żółta Róża" }, Names(res));
            Assert.Equal(1, res.TotalCards);
            Assert.Equal(new[] { "Biały" }, Names(await Ultimate(new CardsQueryFilter { SearchText = "inna" })));
        }

        [Fact]
        public async Task Filters_OrderAndPageInMemory()
        {
            var page = await Ultimate(new CardsQueryFilter { OrderBy = OrderType.Name }, offset: 1, count: 1);
            Assert.Equal(3, page.TotalCards);
            Assert.Equal(new[] { "Zielony" }, Names(page));

            var tagged = await Ultimate(new CardsQueryFilter { IncludeTags = new List<TagIdPair> { new TagIdPair { Id = TagId } } });
            Assert.Equal(new[] { "Żółta Róża" }, Names(tagged));

            var untagged = await Ultimate(new CardsQueryFilter { ExcludeTags = new List<TagIdPair> { new TagIdPair { Id = TagId } }, OrderBy = OrderType.NameDes });
            Assert.Equal(new[] { "Zielony", "Biały" }, Names(untagged));
        }

        [Fact]
        public async Task DifferentFilters_ShareOneCachedList()
        {
            await Ultimate(new CardsQueryFilter { SearchText = "biały" });

            using (var db = new TestDatabase(_path))
            {
                db.Cards.Add(Ultimate("Nowa", "Nowa seria"));
                db.SaveChanges();
            }

            // the test database has no cache interceptor, so a fresh query per filter would see the new card
            Assert.Equal(3, (await Ultimate(new CardsQueryFilter { OrderBy = OrderType.IdDes })).TotalCards);

            QueryCacheManager.ExpireTag(CacheTags.UltimateCards);
            Assert.Equal(4, (await Ultimate(new CardsQueryFilter())).TotalCards);
        }

        [Fact]
        public async Task UniqueList_UsesTheSameFiltering()
        {
            var res = (await ControllerHarness.Attach(new TestController(_path, _time))
                .GetUniqueCardsWithOffsetAndFilterAsync(0, 10, new CardsQueryFilter { SearchText = "roza zwyk" })).Value;

            Assert.Equal(new[] { "Róża zwykła" }, Names(res));
        }
    }
}
