using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanakan.Database.Migrations
{
    /// <inheritdoc />
    public partial class ProfileLayers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProfilePictures",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    Use = table.Column<int>(type: "int", nullable: false),
                    File = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Width = table.Column<int>(type: "int", nullable: false),
                    Height = table.Column<int>(type: "int", nullable: false),
                    Animated = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Sha256 = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfilePictures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProfilePictures_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ProfileUnlocks",
                columns: table => new
                {
                    UserId = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    Feature = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Source = table.Column<int>(type: "int", nullable: false),
                    Currency = table.Column<int>(type: "int", nullable: true),
                    Price = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfileUnlocks", x => new { x.UserId, x.Feature });
                    table.ForeignKey(
                        name: "FK_ProfileUnlocks_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "UserProfiles",
                columns: table => new
                {
                    UserId = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    Layout = table.Column<string>(type: "longtext", maxLength: 16384, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Version = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserProfiles", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_UserProfiles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ProfilePictures_UserId",
                table: "ProfilePictures",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProfilePictures");

            migrationBuilder.DropTable(
                name: "ProfileUnlocks");

            migrationBuilder.DropTable(
                name: "UserProfiles");
        }
    }
}
