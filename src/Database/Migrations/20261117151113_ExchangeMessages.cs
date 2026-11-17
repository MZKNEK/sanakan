using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanakan.Database.Migrations
{
    /// <inheritdoc />
    public partial class ExchangeMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Imported",
                table: "CardExchanges",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<ulong>(
                name: "MessageId",
                table: "CardExchanges",
                type: "bigint unsigned",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PlayerNames",
                columns: table => new
                {
                    UserId = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SeenAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerNames", x => x.UserId);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_CardExchanges_MessageId",
                table: "CardExchanges",
                column: "MessageId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayerNames");

            migrationBuilder.DropIndex(
                name: "IX_CardExchanges_MessageId",
                table: "CardExchanges");

            migrationBuilder.DropColumn(
                name: "Imported",
                table: "CardExchanges");

            migrationBuilder.DropColumn(
                name: "MessageId",
                table: "CardExchanges");
        }
    }
}
