using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Sanakan.Config.Model;
using Sanakan.Database;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Xunit;

namespace Artifacts
{
    public class SqliteProviderTests
    {
        private static FakeConfig SqliteConfig(string path)
        {
            var config = new FakeConfig();
            config.Model.DatabaseProvider = DatabaseProvider.Sqlite;
            config.Model.ConnectionString = $"Data Source={path}";
            return config;
        }

        private static string NewPath() => Path.Combine(Path.GetTempPath(), $"sanakan-provider-{Guid.NewGuid():N}.db");

        [Fact]
        public async Task DatabaseContext_WorksOnSqlite_WithCacheInvalidation()
        {
            var config = SqliteConfig(NewPath());
            const ulong userId = 95001;

            using (var db = new DatabaseContext(config))
            {
                db.EnsureReady();
                Assert.True(db.Database.IsSqlite());

                db.Users.Add(new User().Default(userId, DateTime.Now));
                await db.SaveChangesAsync();
            }

            using (var db = new DatabaseContext(config))
                Assert.Equal(0L, (await db.GetCachedFullUserAsync(userId)).GameDeck.CTCnt);

            using (var db = new DatabaseContext(config))
            {
                var deck = await db.GameDecks.FirstAsync(x => x.UserId == userId);
                deck.CTCnt = 7;
                await db.SaveChangesAsync();
            }

            using (var db = new DatabaseContext(config))
                Assert.Equal(7L, (await db.GetCachedFullUserAsync(userId)).GameDeck.CTCnt);
        }

        [Fact]
        public void EnsureReady_TurnsOnWal()
        {
            var config = SqliteConfig(NewPath());
            using (var db = new DatabaseContext(config))
                db.EnsureReady();

            using var check = new DatabaseContext(config);
            var connection = check.Database.GetDbConnection();
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode;";
            Assert.Equal("wal", command.ExecuteScalar()?.ToString());
        }

        private static async Task<FakeConfig> SeedTextAsync()
        {
            var config = SqliteConfig(NewPath());
            config.Model.ConnectionString += ";Foreign Keys=False";
            using var db = new DatabaseContext(config);
            db.EnsureReady();
            db.Tags.Add(new Tag { Id = 1, Name = "ulubione", GameDeckId = 5 });
            db.Tags.Add(new Tag { Id = 2, Name = "Żółte", GameDeckId = 5 });
            db.Cards.AddRange(
                new Card { Id = 1, Name = "Żółta Róża", Title = "Seria", GameDeckId = 5 },
                new Card { Id = 2, Name = "alfa", Title = "Seria", GameDeckId = 5 },
                new Card { Id = 3, Name = "Beta", Title = "Seria", GameDeckId = 5 });
            await db.SaveChangesAsync();
            return config;
        }

        [Fact]
        public async Task TextEquality_IgnoresCase_LikeMySql()
        {
            using var db = new DatabaseContext(await SeedTextAsync());
            Assert.True(await db.Tags.AnyAsync(x => x.Name.Equals("Ulubione") && x.GameDeckId == 5));
            Assert.False(await db.Tags.AnyAsync(x => x.Name.Equals("ulubion")));

            // the built-in NOCASE folds only ASCII, so these pass only with the registered override
            Assert.True(await db.Tags.AnyAsync(x => x.Name.Equals("żółte")));
            Assert.True(await db.Tags.AnyAsync(x => x.Name.Equals("zolte")));
        }

        [Theory]
        [InlineData("róża")]
        [InlineData("roza")]
        [InlineData("ZOLTA")]
        [InlineData("Róża")]
        public async Task TextSearch_IgnoresCaseAndAccents_LikeMySql(string text)
        {
            using var db = new DatabaseContext(await SeedTextAsync());
            Assert.Equal(new ulong[] { 1 }, await db.Cards.Where(x => x.Name.Contains(text)).Select(x => x.Id).ToListAsync());
        }

        [Fact]
        public async Task TextOrder_IgnoresCase_LikeMySql()
        {
            using var db = new DatabaseContext(await SeedTextAsync());
            Assert.Equal(new[] { "alfa", "Beta", "Żółta Róża" }, await db.Cards.OrderBy(x => x.Name).Select(x => x.Name).ToListAsync());
        }

        [Fact]
        public async Task TestConnection_FailsForMissingFile_WithoutCreatingIt()
        {
            var path = NewPath();

            await Assert.ThrowsAnyAsync<Exception>(() => DatabaseContext.TestConnectionAsync(DatabaseProvider.Sqlite, $"Data Source={path}"));
            Assert.False(File.Exists(path));
        }

        [Fact]
        public async Task TestConnection_PassesForExistingDatabase()
        {
            var path = NewPath();
            using (var db = new DatabaseContext(SqliteConfig(path)))
                db.EnsureReady();

            await DatabaseContext.TestConnectionAsync(DatabaseProvider.Sqlite, $"Data Source={path}");
        }

        [Fact]
        public void Config_DefaultsToMySql_AndStoresProviderByName()
        {
            Assert.Equal(DatabaseProvider.MySql, JsonConvert.DeserializeObject<ConfigModel>("{}").DatabaseProvider);
            Assert.Equal(DatabaseProvider.Sqlite, JsonConvert.DeserializeObject<ConfigModel>("{\"DatabaseProvider\":\"Sqlite\"}").DatabaseProvider);
            Assert.Contains("\"DatabaseProvider\":\"Sqlite\"", JsonConvert.SerializeObject(new ConfigModel { DatabaseProvider = DatabaseProvider.Sqlite }));
        }
    }
}
