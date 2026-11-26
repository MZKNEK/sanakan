using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanakan.Database.Migrations
{
    /// <inheritdoc />
    public partial class PackDateRaritiesAndCharacters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // the default keeps inserts of the previous build working if Run.sh rolls back to it
            migrationBuilder.AddColumn<string>(
                name: "Characters",
                table: "BoosterPacks",
                type: "longtext",
                nullable: false,
                defaultValue: "[]")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "CreationDate",
                table: "BoosterPacks",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "ExcludedRarities",
                table: "BoosterPacks",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // packs granted before the column existed get the day the migration ran
            migrationBuilder.Sql("UPDATE `BoosterPacks` SET `CreationDate` = NOW(6);");

            // BoosterPackCharacters and RaritysExcludedFromPacks stay one version for the previous build
            migrationBuilder.Sql(@"UPDATE `BoosterPacks` p
JOIN (SELECT `BoosterPackId`, JSON_ARRAYAGG(`Character`) AS `Ids` FROM `BoosterPackCharacters`
      GROUP BY `BoosterPackId`) c ON c.`BoosterPackId` = p.`Id`
SET p.`Characters` = c.`Ids`;");

            migrationBuilder.Sql(@"UPDATE `BoosterPacks` p
JOIN (SELECT `BoosterPackId`, BIT_OR(1 << `Rarity`) AS `Mask` FROM `RaritysExcludedFromPacks`
      WHERE `Rarity` BETWEEN 0 AND 7 GROUP BY `BoosterPackId`) r ON r.`BoosterPackId` = p.`Id`
SET p.`ExcludedRarities` = r.`Mask`;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Characters",
                table: "BoosterPacks");

            migrationBuilder.DropColumn(
                name: "CreationDate",
                table: "BoosterPacks");

            migrationBuilder.DropColumn(
                name: "ExcludedRarities",
                table: "BoosterPacks");
        }
    }
}
