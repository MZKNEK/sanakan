using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanakan.Database.Migrations
{
    /// <inheritdoc />
    public partial class PvpDefeats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PvpDefeats",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    AttackerId = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Avenged = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PvpDefeats", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_PvpDefeats_UserId_Date",
                table: "PvpDefeats",
                columns: new[] { "UserId", "Date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PvpDefeats");
        }
    }
}
