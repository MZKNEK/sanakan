using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Sanakan.Config.Model;
using Sanakan.Database;
using Xunit;

namespace Artifacts
{
    public class MigrationTests
    {
        // the bot's own context type: EF finds migrations by the exact type; builds SQL only, never connects
        private static DatabaseContext MySql() => new DatabaseContext(new FakeConfig
        {
            Model = new ConfigModel { DatabaseProvider = DatabaseProvider.MySql, ConnectionString = "Server=localhost;Database=sanakan" },
        });

        [Fact]
        public void ModelHasNoChangesWithoutAMigration()
        {
            using var db = MySql();

            // a change to the model needs: dotnet ef migrations add <Name> --project src/Sanakan.csproj
            //   --output-dir Database/Migrations --namespace Sanakan.Database.Migrations
            Assert.False(db.Database.HasPendingModelChanges());
        }

        [Fact]
        public void BaselineIsTheFirstMigration()
        {
            using var db = MySql();

            // an old database is marked with this name, a renamed baseline would be applied on top of its tables
            Assert.Equal(DatabaseContext.BaselineMigration, db.Database.GetMigrations().First());
        }

        [Fact]
        public void TriggersMigrationCreatesBothWishlistTriggersAgain()
        {
            using var db = MySql();
            var sql = db.GetService<IMigrator>().GenerateScript(DatabaseContext.BaselineMigration, "20261113000801_WishlistTriggers");

            Assert.Contains("DROP TRIGGER IF EXISTS `UpdateWishlist`", sql);
            Assert.Contains("CREATE TRIGGER `UpdateWishlist` AFTER UPDATE ON `WishlistCountData`", sql);
            Assert.Contains("CREATE TRIGGER `UpdateWishlistInsert` AFTER INSERT ON `WishlistCountData`", sql);
            Assert.DoesNotContain("DEFINER", sql);
        }

        [Fact]
        public void Utf8mb4MigrationNamesTablesExactlyAsTheModel()
        {
            using var db = MySql();
            var tables = db.Model.GetEntityTypes().Select(x => x.GetTableName()).ToHashSet(System.StringComparer.Ordinal);

            // table names are case sensitive on the Linux server
            var removedLater = new[] { "RaritysExcludedFromPacks", "BoosterPackCharacters" };
            Assert.All(Sanakan.Database.Migrations.Utf8mb4.Tables.Except(removedLater), x => Assert.Contains(x, tables));
            Assert.Equal(37, Sanakan.Database.Migrations.Utf8mb4.Tables.Distinct().Count());

            var sql = db.GetService<IMigrator>().GenerateScript("20261117151113_ExchangeMessages", "20261118181128_Utf8mb4");
            Assert.Contains("ALTER TABLE `UserActivities` CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;", sql);
            Assert.Contains("ALTER TABLE `Cards` CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;", sql);
        }

        [Fact]
        public void MigrateOnStartIsOnByDefault()
            => Assert.True(new ConfigModel().MigrateOnStart);
    }
}
