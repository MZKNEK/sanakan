using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanakan.Database.Migrations
{
    // Cards.WhoWantsCount and AWhoWantsCount follow WishlistCountData through these triggers, the bot only writes the
    // counts. Created again so that a restored dump does not keep the old server's DEFINER, which fails elsewhere.
    /// <inheritdoc />
    public partial class WishlistTriggers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS `UpdateWishlist`;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS `UpdateWishlistInsert`;");
            migrationBuilder.Sql("CREATE TRIGGER `UpdateWishlist` AFTER UPDATE ON `WishlistCountData` FOR EACH ROW "
                + "UPDATE `Cards` SET `WhoWantsCount` = NEW.`Count`, `AWhoWantsCount` = NEW.`ACount` WHERE `Character` = NEW.`Id`;");
            migrationBuilder.Sql("CREATE TRIGGER `UpdateWishlistInsert` AFTER INSERT ON `WishlistCountData` FOR EACH ROW "
                + "UPDATE `Cards` SET `WhoWantsCount` = NEW.`Count`, `AWhoWantsCount` = NEW.`ACount` WHERE `Character` = NEW.`Id`;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS `UpdateWishlist`;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS `UpdateWishlistInsert`;");
        }
    }
}
