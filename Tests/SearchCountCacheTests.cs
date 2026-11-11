using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Sanakan;
using Sanakan.Api.Controllers;
using Sanakan.Api.Models;
using Sanakan.Database;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Xunit;

namespace Artifacts
{
    // the card lists keep the count of a text search for a while, other filters are counted every time
    public class SearchCountCacheTests : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"sanakan-searchcount-{Guid.NewGuid():N}.db");
        private readonly ulong _deck = (ulong)new Random().Next(100_000, 900_000);

        private sealed class TestDatabase : DatabaseContext
        {
            private readonly string _path;
            public TestDatabase(string path) : base(new FakeConfig()) => _path = path;
            protected override void OnConfiguring(DbContextOptionsBuilder builder) => builder.UseSqlite($"Data Source={_path}");
        }

        public SearchCountCacheTests()
        {
            using var db = new TestDatabase(_path);
            db.Database.EnsureCreated();
            var user = new User().Default(_deck, new FixedTime().Now());
            user.GameDeck.Cards.Add(NewCard(1, "Asuna"));
            user.GameDeck.Cards.Add(NewCard(2, "Asuna Yuuki"));
            db.Users.Add(user);
            db.SaveChanges();
        }

        public void Dispose()
        {
            QueryCache.ExpireTag(CacheTags.User(_deck));
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(_path); } catch (IOException) { }
        }

        private static Card NewCard(ulong id, string name) => new Card { Id = id, Name = name, Title = "Seria", Rarity = Rarity.A, Tags = new List<Tag>(), CreationDate = new DateTime(2026, 1, 1) };

        private Task<int> CountAsync(CardsQueryFilter filter)
        {
            var db = new TestDatabase(_path);
            var query = db.Cards.AsQueryable().Where(x => x.GameDeckId == _deck);
            if (!string.IsNullOrEmpty(filter.SearchText))
                query = query.Where(x => x.Name.Contains(filter.SearchText) || x.Title.Contains(filter.SearchText));
            return (Task<int>)typeof(WaifuController).GetMethod("CountFilteredCardsAsync", BindingFlags.NonPublic | BindingFlags.Static, null,
                new[] { typeof(DatabaseContext), typeof(IQueryable<Card>), typeof(CardsQueryFilter), typeof(ulong) }, null)
                .Invoke(null, new object[] { db, query, filter, _deck });
        }

        private void AddCard(ulong id, string name)
        {
            using var db = new TestDatabase(_path);
            db.Cards.Add(new Card { Id = id, Name = name, Title = "Seria", Rarity = Rarity.A, GameDeckId = _deck, Tags = new List<Tag>(), CreationDate = new DateTime(2026, 1, 1) });
            db.SaveChanges();
        }

        [Fact]
        public void Key_IgnoresTheSortAndCase_ButNotTheFilters()
        {
            var tag = new List<TagIdPair> { new TagIdPair { Id = 4 } };
            var key = WaifuController.SearchCountKey(new CardsQueryFilter { SearchText = "Asuna", OrderBy = OrderType.IdDes }, 5);

            Assert.Equal(key, WaifuController.SearchCountKey(new CardsQueryFilter { SearchText = "asuna", OrderBy = OrderType.Name }, 5));
            Assert.NotEqual(key, WaifuController.SearchCountKey(new CardsQueryFilter { SearchText = "Asun" }, 5));
            Assert.NotEqual(key, WaifuController.SearchCountKey(new CardsQueryFilter { SearchText = "Asuna" }, 6));
            Assert.NotEqual(key, WaifuController.SearchCountKey(new CardsQueryFilter { SearchText = "Asuna", IncludeTags = tag }, 5));
            Assert.NotEqual(key, WaifuController.SearchCountKey(new CardsQueryFilter { SearchText = "Asuna", ExcludeTags = tag }, 5));
            Assert.NotEqual(key, WaifuController.SearchCountKey(new CardsQueryFilter { SearchText = "Asuna", CharIds = new List<ulong> { 1 } }, 5));
            Assert.Equal("search-count", QueryCache.KindOf(key));
        }

        [Fact]
        public async Task TextSearch_KeepsItsCount_UntilThePlayersCardsChange()
        {
            Assert.Equal(2, await CountAsync(new CardsQueryFilter { SearchText = "Asuna" }));
            AddCard(3, "Asuna kopia");

            Assert.Equal(2, await CountAsync(new CardsQueryFilter { SearchText = "asuna", OrderBy = OrderType.Name }));
            QueryCache.ExpireTag(CacheTags.User(_deck));
            Assert.Equal(3, await CountAsync(new CardsQueryFilter { SearchText = "Asuna" }));
        }

        [Fact]
        public async Task OtherFilters_AreCountedEveryTime()
        {
            Assert.Equal(2, await CountAsync(new CardsQueryFilter()));
            AddCard(3, "Inna");

            Assert.Equal(3, await CountAsync(new CardsQueryFilter()));
        }
    }
}
