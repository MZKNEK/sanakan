using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanakan.Database.Migrations
{
    /// <inheritdoc />
    public partial class PlayerTradesAndDuelDeres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AttackerDeres",
                table: "DuelResults",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "DefenderDeres",
                table: "DuelResults",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CardExchanges",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Date = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    User1Id = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    User2Id = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    User1Gave = table.Column<int>(type: "int", nullable: false),
                    User2Gave = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardExchanges", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ScGifts",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Date = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    FromUserId = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    ToUserId = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    Sent = table.Column<long>(type: "bigint", nullable: false),
                    Received = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScGifts", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CardExchangeCards",
                columns: table => new
                {
                    ExchangeId = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    CardId = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    FromUserId = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    ToUserId = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardExchangeCards", x => new { x.ExchangeId, x.CardId });
                    table.ForeignKey(
                        name: "FK_CardExchangeCards_CardExchanges_ExchangeId",
                        column: x => x.ExchangeId,
                        principalTable: "CardExchanges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_CardExchangeCards_CardId",
                table: "CardExchangeCards",
                column: "CardId");

            migrationBuilder.CreateIndex(
                name: "IX_CardExchanges_Date",
                table: "CardExchanges",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_CardExchanges_User1Id",
                table: "CardExchanges",
                column: "User1Id");

            migrationBuilder.CreateIndex(
                name: "IX_CardExchanges_User2Id",
                table: "CardExchanges",
                column: "User2Id");

            migrationBuilder.CreateIndex(
                name: "IX_ScGifts_Date",
                table: "ScGifts",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_ScGifts_FromUserId",
                table: "ScGifts",
                column: "FromUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ScGifts_ToUserId",
                table: "ScGifts",
                column: "ToUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CardExchangeCards");

            migrationBuilder.DropTable(
                name: "ScGifts");

            migrationBuilder.DropTable(
                name: "CardExchanges");

            migrationBuilder.DropColumn(
                name: "AttackerDeres",
                table: "DuelResults");

            migrationBuilder.DropColumn(
                name: "DefenderDeres",
                table: "DuelResults");
        }
    }
}
