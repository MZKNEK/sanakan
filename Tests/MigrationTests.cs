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
        public void MigrateOnStartIsOnByDefault()
            => Assert.True(new ConfigModel().MigrateOnStart);
    }
}
