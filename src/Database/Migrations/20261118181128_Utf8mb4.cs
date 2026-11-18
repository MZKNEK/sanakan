using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanakan.Database.Migrations
{
    // Tables from before the migrations came from a utf8mb3 dump, which cannot store emoji (a nick with one failed the
    // whole save). utf8mb4 holds every utf8mb3 character with the same bytes, so the data stays as it was. Cards takes
    // about a minute and a half, EnsureReady gives migrations a longer command timeout. Tags.Name becomes mediumtext.
    /// <inheritdoc />
    public partial class Utf8mb4 : Migration
    {
        public static readonly string[] Tables =
        {
            "BoosterPackCharacters", "BoosterPacks", "CardPvPStats", "Cards", "CommandChannels", "ExpContainers", "Figures",
            "GameDecks", "Guilds", "IgnoredChannels", "Items", "LevelRoles", "ModeratorRoles", "MuteModifiers", "MyLands",
            "OwnedRoles", "Penalties", "Raports", "RaritysExcludedFromPacks", "SelfRoles", "SlotMachineConfigs", "SystemData",
            "TagCardRelations", "Tags", "TimeStatuses", "TransferData", "UserActivities", "UserApiKeys", "Users", "UsersStats",
            "WaifuCommandChannels", "WaifuFightChannels", "Waifus", "Wishes", "WishlistCountData", "WithoutExpChannels",
            "WithoutSupervisionChannels",
        };

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var table in Tables)
                migrationBuilder.Sql($"ALTER TABLE `{table}` CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;");
        }

        // going back to utf8mb3 would fail on any emoji already saved, the previous build works on utf8mb4 as well
        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
