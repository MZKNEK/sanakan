using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanakan.Database.Migrations
{
    /// <inheritdoc />
    public partial class Baseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CommandsData",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    GuildId = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CmdName = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CmdParams = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommandsData", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Guilds",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    MuteRole = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    ModMuteRole = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    UserRole = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    AdminRole = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    SemiAdminRole = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    TesterRole = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    GlobalEmotesRole = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    WaifuRole = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    NotificationChannel = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    RaportChannel = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    ToDoChannel = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    NsfwChannel = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    LogChannel = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    GreetingChannel = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    NitroRole = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    AlwaysBanChannel = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    WelcomeMessage = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    WelcomeMessagePW = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    GoodbyeMessage = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SafariLimit = table.Column<long>(type: "bigint", nullable: false),
                    Supervision = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ChaosMode = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Prefix = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NoUserRoleHelp = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Guilds", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "IdempotencyRecords",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Scope = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Key = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OwnerId = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    RequestHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Result = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyRecords", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "MuteModifiers",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    User = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    Guild = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<long>(type: "bigint", nullable: false),
                    Count = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MuteModifiers", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Penalties",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    User = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    Guild = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    Reason = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Type = table.Column<int>(type: "int", nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    DurationInHours = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Penalties", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SystemData",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Value = table.Column<long>(type: "bigint", nullable: false),
                    MeasureDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemData", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "TransferData",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Value = table.Column<long>(type: "bigint", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    DiscordId = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    ShindenId = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    ValueBefore = table.Column<long>(type: "bigint", nullable: false),
                    ExpectedValue = table.Column<long>(type: "bigint", nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransferData", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "UserActivities",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    ShindenId = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    TargetId = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Date = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Misc = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserActivities", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Shinden = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    IsBlacklisted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    AcCnt = table.Column<long>(type: "bigint", nullable: false),
                    TcCnt = table.Column<long>(type: "bigint", nullable: false),
                    ScCnt = table.Column<long>(type: "bigint", nullable: false),
                    Level = table.Column<long>(type: "bigint", nullable: false),
                    ExpCnt = table.Column<long>(type: "bigint", nullable: false),
                    ProfileType = table.Column<int>(type: "int", nullable: false),
                    BackgroundProfileUri = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StatsReplacementProfileUri = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MessagesCnt = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    CommandsCnt = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    MeasureDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    MessagesCntAtDate = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    CharacterCntFromDate = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    Warnings = table.Column<long>(type: "bigint", nullable: false),
                    PoolType = table.Column<int>(type: "int", nullable: false),
                    AvatarBorder = table.Column<int>(type: "int", nullable: false),
                    StatsStyleSettings = table.Column<int>(type: "int", nullable: false),
                    CustomProfileOverlayUrl = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PremiumCustomProfileOverlayUrl = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ProfileShadowsOpacity = table.Column<float>(type: "float", nullable: false),
                    LeftAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "UsersData",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Value = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    GuildId = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    MeasureDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsersData", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "WishlistCountData",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Count = table.Column<int>(type: "int", nullable: false),
                    ACount = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WishlistCountData", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CommandChannels",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Channel = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    GuildOptionsId = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommandChannels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommandChannels_Guilds_GuildOptionsId",
                        column: x => x.GuildOptionsId,
                        principalTable: "Guilds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "IgnoredChannels",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Channel = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    GuildOptionsId = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IgnoredChannels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IgnoredChannels_Guilds_GuildOptionsId",
                        column: x => x.GuildOptionsId,
                        principalTable: "Guilds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "LevelRoles",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Role = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    Level = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    GuildOptionsId = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LevelRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LevelRoles_Guilds_GuildOptionsId",
                        column: x => x.GuildOptionsId,
                        principalTable: "Guilds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ModeratorRoles",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Role = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    GuildOptionsId = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModeratorRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ModeratorRoles_Guilds_GuildOptionsId",
                        column: x => x.GuildOptionsId,
                        principalTable: "Guilds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "MyLands",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Manager = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    Underling = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    GuildOptionsId = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MyLands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MyLands_Guilds_GuildOptionsId",
                        column: x => x.GuildOptionsId,
                        principalTable: "Guilds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Raports",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    User = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    Message = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    GuildOptionsId = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Raports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Raports_Guilds_GuildOptionsId",
                        column: x => x.GuildOptionsId,
                        principalTable: "Guilds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SelfRoles",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Role = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    Name = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    GuildOptionsId = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SelfRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SelfRoles_Guilds_GuildOptionsId",
                        column: x => x.GuildOptionsId,
                        principalTable: "Guilds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Waifus",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    MarketChannel = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    SpawnChannel = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    DuelChannel = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    TrashFightChannel = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    TrashSpawnChannel = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    TrashCommandsChannel = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    GuildOptionsId = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Waifus", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Waifus_Guilds_GuildOptionsId",
                        column: x => x.GuildOptionsId,
                        principalTable: "Guilds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "WithoutExpChannels",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Channel = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    GuildOptionsId = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WithoutExpChannels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WithoutExpChannels_Guilds_GuildOptionsId",
                        column: x => x.GuildOptionsId,
                        principalTable: "Guilds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "WithoutSupervisionChannels",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Channel = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    GuildOptionsId = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WithoutSupervisionChannels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WithoutSupervisionChannels_Guilds_GuildOptionsId",
                        column: x => x.GuildOptionsId,
                        principalTable: "Guilds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "OwnedRoles",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Role = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    PenaltyInfoId = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OwnedRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OwnedRoles_Penalties_PenaltyInfoId",
                        column: x => x.PenaltyInfoId,
                        principalTable: "Penalties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "GameDecks",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    CTCnt = table.Column<long>(type: "bigint", nullable: false),
                    Waifu = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    Karma = table.Column<double>(type: "double", nullable: false),
                    ItemsDropped = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    WishlistIsPrivate = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    PVPCoins = table.Column<long>(type: "bigint", nullable: false),
                    DeckPower = table.Column<double>(type: "double", nullable: false),
                    CardsInDeck = table.Column<long>(type: "bigint", nullable: false),
                    PVPWinStreak = table.Column<long>(type: "bigint", nullable: false),
                    GlobalPVPRank = table.Column<long>(type: "bigint", nullable: false),
                    SeasonalPVPRank = table.Column<long>(type: "bigint", nullable: false),
                    MatachMakingRatio = table.Column<double>(type: "double", nullable: false),
                    PVPDailyGamesPlayed = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    PVPSeasonBeginDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ExchangeConditions = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BackgroundImageUrl = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ForegroundImageUrl = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ForegroundColor = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ForegroundPosition = table.Column<int>(type: "int", nullable: false),
                    BackgroundPosition = table.Column<int>(type: "int", nullable: false),
                    MaxNumberOfCards = table.Column<long>(type: "bigint", nullable: false),
                    CardsInGallery = table.Column<int>(type: "int", nullable: false),
                    PremiumWaifu = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    MaxNumberOfTags = table.Column<int>(type: "int", nullable: false),
                    EndOfExpeditionAction = table.Column<int>(type: "int", nullable: false),
                    GalleryOrderedIds = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LastSignificantActivity = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TagsOrder = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameDecks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameDecks_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SlotMachineConfigs",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    PsayMode = table.Column<long>(type: "bigint", nullable: false),
                    Beat = table.Column<int>(type: "int", nullable: false),
                    Rows = table.Column<int>(type: "int", nullable: false),
                    Multiplier = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlotMachineConfigs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SlotMachineConfigs_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "TimeStatuses",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Type = table.Column<int>(type: "int", nullable: false),
                    EndsAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IValue = table.Column<long>(type: "bigint", nullable: false),
                    BValue = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Guild = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    UserId = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimeStatuses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TimeStatuses_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "UserApiKeys",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    Application = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    KeyHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserApiKeys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserApiKeys_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "UsersStats",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ScLost = table.Column<long>(type: "bigint", nullable: false),
                    IncomeInSc = table.Column<long>(type: "bigint", nullable: false),
                    SlotMachineGames = table.Column<long>(type: "bigint", nullable: false),
                    Tail = table.Column<long>(type: "bigint", nullable: false),
                    Head = table.Column<long>(type: "bigint", nullable: false),
                    Hit = table.Column<long>(type: "bigint", nullable: false),
                    Misd = table.Column<long>(type: "bigint", nullable: false),
                    RightAnswers = table.Column<long>(type: "bigint", nullable: false),
                    TotalAnswers = table.Column<long>(type: "bigint", nullable: false),
                    TurnamentsWon = table.Column<long>(type: "bigint", nullable: false),
                    UpgaredCards = table.Column<long>(type: "bigint", nullable: false),
                    SacraficeCards = table.Column<long>(type: "bigint", nullable: false),
                    DestroyedCards = table.Column<long>(type: "bigint", nullable: false),
                    UnleashedCards = table.Column<long>(type: "bigint", nullable: false),
                    ReleasedCards = table.Column<long>(type: "bigint", nullable: false),
                    OpenedBoosterPacks = table.Column<long>(type: "bigint", nullable: false),
                    OpenedBoosterPacksActivity = table.Column<long>(type: "bigint", nullable: false),
                    YamiUpgrades = table.Column<long>(type: "bigint", nullable: false),
                    RaitoUpgrades = table.Column<long>(type: "bigint", nullable: false),
                    YatoUpgrades = table.Column<long>(type: "bigint", nullable: false),
                    WastedTcOnCookies = table.Column<long>(type: "bigint", nullable: false),
                    WastedTcOnCards = table.Column<long>(type: "bigint", nullable: false),
                    UpgradedToSSS = table.Column<long>(type: "bigint", nullable: false),
                    WastedPuzzlesOnCookies = table.Column<long>(type: "bigint", nullable: false),
                    WastedPuzzlesOnCards = table.Column<long>(type: "bigint", nullable: false),
                    WastedPuzzlesOnCardsReal = table.Column<long>(type: "bigint", nullable: false),
                    WastedActivityOnCookies = table.Column<long>(type: "bigint", nullable: false),
                    WastedActivityOnCards = table.Column<long>(type: "bigint", nullable: false),
                    LotteryTicketsUsed = table.Column<long>(type: "bigint", nullable: false),
                    ReversedKarmaCnt = table.Column<long>(type: "bigint", nullable: false),
                    CreatedCardsFromItems = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsersStats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UsersStats_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "WaifuCommandChannels",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Channel = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    WaifuId = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WaifuCommandChannels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WaifuCommandChannels_Waifus_WaifuId",
                        column: x => x.WaifuId,
                        principalTable: "Waifus",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "WaifuFightChannels",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Channel = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    WaifuId = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WaifuFightChannels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WaifuFightChannels_Waifus_WaifuId",
                        column: x => x.WaifuId,
                        principalTable: "Waifus",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "BoosterPacks",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Title = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    Season = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CardCnt = table.Column<int>(type: "int", nullable: false),
                    MinRarity = table.Column<int>(type: "int", nullable: false),
                    IsCardFromPackTradable = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CardSourceFromPack = table.Column<int>(type: "int", nullable: false),
                    GameDeckId = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BoosterPacks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BoosterPacks_GameDecks_GameDeckId",
                        column: x => x.GameDeckId,
                        principalTable: "GameDecks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CardPvPStats",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Result = table.Column<int>(type: "int", nullable: false),
                    GameDeckId = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardPvPStats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CardPvPStats_GameDecks_GameDeckId",
                        column: x => x.GameDeckId,
                        principalTable: "GameDecks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Cards",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Active = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    InCage = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsTradable = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ExpCnt = table.Column<double>(type: "double", nullable: false),
                    Affection = table.Column<double>(type: "double", nullable: false),
                    UpgradesCnt = table.Column<int>(type: "int", nullable: false),
                    RestartCnt = table.Column<int>(type: "int", nullable: false),
                    Rarity = table.Column<int>(type: "int", nullable: false),
                    RarityOnStart = table.Column<int>(type: "int", nullable: false),
                    Dere = table.Column<int>(type: "int", nullable: false),
                    Defence = table.Column<int>(type: "int", nullable: false),
                    Attack = table.Column<int>(type: "int", nullable: false),
                    Health = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Character = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    CreationDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Image = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CustomImage = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FirstIdOwner = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    LastIdOwner = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    Unique = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    StarStyle = table.Column<int>(type: "int", nullable: false),
                    CustomBorder = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MarketValue = table.Column<double>(type: "double", nullable: false),
                    Curse = table.Column<int>(type: "int", nullable: false),
                    CardPower = table.Column<double>(type: "double", nullable: false),
                    WhoWantsCount = table.Column<int>(type: "int", nullable: false),
                    AWhoWantsCount = table.Column<int>(type: "int", nullable: false),
                    CustomImageDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    FixedCustomImageCnt = table.Column<int>(type: "int", nullable: false),
                    IsAnimatedImage = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    RatePositive = table.Column<int>(type: "int", nullable: false),
                    RateNegative = table.Column<int>(type: "int", nullable: false),
                    EnhanceCnt = table.Column<int>(type: "int", nullable: false),
                    FromFigure = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    FigureId = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    Quality = table.Column<int>(type: "int", nullable: false),
                    AttackBonus = table.Column<int>(type: "int", nullable: false),
                    HealthBonus = table.Column<int>(type: "int", nullable: false),
                    DefenceBonus = table.Column<int>(type: "int", nullable: false),
                    CritChance = table.Column<double>(type: "double", nullable: false),
                    QualityOnStart = table.Column<int>(type: "int", nullable: false),
                    PAS = table.Column<int>(type: "int", nullable: false),
                    BorderVariant = table.Column<int>(type: "int", nullable: false),
                    BorderOverflow = table.Column<int>(type: "int", nullable: false),
                    Expedition = table.Column<int>(type: "int", nullable: false),
                    ExpeditionDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ExpeditionEndDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Fatigue = table.Column<double>(type: "double", nullable: false),
                    PictureKind = table.Column<byte>(type: "tinyint unsigned", nullable: false, computedColumnSql: "CASE WHEN CustomImage IS NULL THEN (CASE WHEN Image IS NULL THEN 0 ELSE 1 END) ELSE (CASE WHEN IsAnimatedImage THEN 3 ELSE 2 END) END", stored: true),
                    Cursed = table.Column<bool>(type: "tinyint(1)", nullable: false, computedColumnSql: "CASE WHEN Curse <> 0 THEN 1 ELSE 0 END", stored: true),
                    GameDeckId = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Cards_GameDecks_GameDeckId",
                        column: x => x.GameDeckId,
                        principalTable: "GameDecks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ExpContainers",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ExpCount = table.Column<double>(type: "double", nullable: false),
                    Level = table.Column<int>(type: "int", nullable: false),
                    GameDeckId = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpContainers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExpContainers_GameDecks_GameDeckId",
                        column: x => x.GameDeckId,
                        principalTable: "GameDecks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Figures",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Dere = table.Column<int>(type: "int", nullable: false),
                    Attack = table.Column<int>(type: "int", nullable: false),
                    Health = table.Column<int>(type: "int", nullable: false),
                    Defence = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Title = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsFocus = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ExpCnt = table.Column<double>(type: "double", nullable: false),
                    RestartCnt = table.Column<int>(type: "int", nullable: false),
                    Character = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    IsComplete = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CritChance = table.Column<double>(type: "double", nullable: false),
                    CreatedCardId = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    PAS = table.Column<int>(type: "int", nullable: false),
                    SkeletonQuality = table.Column<int>(type: "int", nullable: false),
                    CompletionDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    FocusedPart = table.Column<int>(type: "int", nullable: false),
                    PartExp = table.Column<double>(type: "double", nullable: false),
                    HeadQuality = table.Column<int>(type: "int", nullable: false),
                    BodyQuality = table.Column<int>(type: "int", nullable: false),
                    LeftArmQuality = table.Column<int>(type: "int", nullable: false),
                    RightArmQuality = table.Column<int>(type: "int", nullable: false),
                    LeftLegQuality = table.Column<int>(type: "int", nullable: false),
                    RightLegQuality = table.Column<int>(type: "int", nullable: false),
                    ClothesQuality = table.Column<int>(type: "int", nullable: false),
                    GameDeckId = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Figures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Figures_GameDecks_GameDeckId",
                        column: x => x.GameDeckId,
                        principalTable: "GameDecks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Items",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Count = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Quality = table.Column<int>(type: "int", nullable: false),
                    GameDeckId = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Items_GameDecks_GameDeckId",
                        column: x => x.GameDeckId,
                        principalTable: "GameDecks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Tags",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    GameDeckId = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tags_GameDecks_GameDeckId",
                        column: x => x.GameDeckId,
                        principalTable: "GameDecks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Wishes",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ObjectId = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    ObjectName = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Entry = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    GameDeckId = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Wishes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Wishes_GameDecks_GameDeckId",
                        column: x => x.GameDeckId,
                        principalTable: "GameDecks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "BoosterPackCharacters",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Character = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    BoosterPackId = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BoosterPackCharacters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BoosterPackCharacters_BoosterPacks_BoosterPackId",
                        column: x => x.BoosterPackId,
                        principalTable: "BoosterPacks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "RaritysExcludedFromPacks",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Rarity = table.Column<int>(type: "int", nullable: false),
                    BoosterPackId = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RaritysExcludedFromPacks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RaritysExcludedFromPacks_BoosterPacks_BoosterPackId",
                        column: x => x.BoosterPackId,
                        principalTable: "BoosterPacks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "TagCardRelations",
                columns: table => new
                {
                    TagId = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    CardId = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TagCardRelations", x => new { x.TagId, x.CardId });
                    table.ForeignKey(
                        name: "FK_TagCardRelations_Cards_CardId",
                        column: x => x.CardId,
                        principalTable: "Cards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TagCardRelations_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_BoosterPackCharacters_BoosterPackId",
                table: "BoosterPackCharacters",
                column: "BoosterPackId");

            migrationBuilder.CreateIndex(
                name: "IX_BoosterPacks_GameDeckId",
                table: "BoosterPacks",
                column: "GameDeckId");

            migrationBuilder.CreateIndex(
                name: "IX_CardPvPStats_GameDeckId_Type_Result",
                table: "CardPvPStats",
                columns: new[] { "GameDeckId", "Type", "Result" });

            migrationBuilder.CreateIndex(
                name: "IX_Cards_Affection",
                table: "Cards",
                column: "Affection");

            migrationBuilder.CreateIndex(
                name: "IX_Cards_AWhoWantsCount_Character",
                table: "Cards",
                columns: new[] { "AWhoWantsCount", "Character" });

            migrationBuilder.CreateIndex(
                name: "IX_Cards_CardPower",
                table: "Cards",
                column: "CardPower");

            migrationBuilder.CreateIndex(
                name: "IX_Cards_Character",
                table: "Cards",
                column: "Character");

            migrationBuilder.CreateIndex(
                name: "IX_Cards_Dere",
                table: "Cards",
                column: "Dere");

            migrationBuilder.CreateIndex(
                name: "IX_Cards_Expedition",
                table: "Cards",
                column: "Expedition");

            migrationBuilder.CreateIndex(
                name: "IX_Cards_FromFigure",
                table: "Cards",
                column: "FromFigure");

            migrationBuilder.CreateIndex(
                name: "IX_Cards_GameDeckId",
                table: "Cards",
                column: "GameDeckId");

            migrationBuilder.CreateIndex(
                name: "IX_Cards_GameDeckId_Active_CardPower",
                table: "Cards",
                columns: new[] { "GameDeckId", "Active", "CardPower" });

            migrationBuilder.CreateIndex(
                name: "IX_Cards_GameDeckId_Character",
                table: "Cards",
                columns: new[] { "GameDeckId", "Character" });

            migrationBuilder.CreateIndex(
                name: "IX_Cards_GameDeckId_Cursed",
                table: "Cards",
                columns: new[] { "GameDeckId", "Cursed" });

            migrationBuilder.CreateIndex(
                name: "IX_Cards_GameDeckId_Fatigue",
                table: "Cards",
                columns: new[] { "GameDeckId", "Fatigue" });

            migrationBuilder.CreateIndex(
                name: "IX_Cards_GameDeckId_PictureKind_CustomImageDate_Id",
                table: "Cards",
                columns: new[] { "GameDeckId", "PictureKind", "CustomImageDate", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Cards_GameDeckId_Rarity_FromFigure_Quality_RestartCnt",
                table: "Cards",
                columns: new[] { "GameDeckId", "Rarity", "FromFigure", "Quality", "RestartCnt" });

            migrationBuilder.CreateIndex(
                name: "IX_Cards_Health",
                table: "Cards",
                column: "Health");

            migrationBuilder.CreateIndex(
                name: "IX_Cards_Name_Title",
                table: "Cards",
                columns: new[] { "Name", "Title" });

            migrationBuilder.CreateIndex(
                name: "IX_Cards_PictureKind_CustomImageDate_Id",
                table: "Cards",
                columns: new[] { "PictureKind", "CustomImageDate", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Cards_Rarity_Quality_BorderOverflow",
                table: "Cards",
                columns: new[] { "Rarity", "Quality", "BorderOverflow" },
                descending: new[] { true, false, false });

            migrationBuilder.CreateIndex(
                name: "IX_Cards_Unique",
                table: "Cards",
                column: "Unique");

            migrationBuilder.CreateIndex(
                name: "IX_Cards_WhoWantsCount_Character",
                table: "Cards",
                columns: new[] { "WhoWantsCount", "Character" });

            migrationBuilder.CreateIndex(
                name: "IX_CommandChannels_GuildOptionsId",
                table: "CommandChannels",
                column: "GuildOptionsId");

            migrationBuilder.CreateIndex(
                name: "IX_CommandsData_GuildId_Date",
                table: "CommandsData",
                columns: new[] { "GuildId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_CommandsData_UserId_Date",
                table: "CommandsData",
                columns: new[] { "UserId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpConteiners_GameDeckId",
                table: "ExpContainers",
                column: "GameDeckId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Figures_GameDeckId",
                table: "Figures",
                column: "GameDeckId");

            migrationBuilder.CreateIndex(
                name: "IX_GameDecks_DeckPower",
                table: "GameDecks",
                column: "DeckPower");

            migrationBuilder.CreateIndex(
                name: "IX_GameDecks_UserId",
                table: "GameDecks",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GameDecks_WishlistIsPrivate",
                table: "GameDecks",
                column: "WishlistIsPrivate");

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_CreatedAt",
                table: "IdempotencyRecords",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_Scope_Key",
                table: "IdempotencyRecords",
                columns: new[] { "Scope", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IgnoredChannels_GuildOptionsId",
                table: "IgnoredChannels",
                column: "GuildOptionsId");

            migrationBuilder.CreateIndex(
                name: "IX_Items_GameDeckId",
                table: "Items",
                column: "GameDeckId");

            migrationBuilder.CreateIndex(
                name: "IX_LevelRoles_GuildOptionsId",
                table: "LevelRoles",
                column: "GuildOptionsId");

            migrationBuilder.CreateIndex(
                name: "IX_ModeratorRoles_GuildOptionsId",
                table: "ModeratorRoles",
                column: "GuildOptionsId");

            migrationBuilder.CreateIndex(
                name: "IX_MuteModifiers_User_Guild",
                table: "MuteModifiers",
                columns: new[] { "User", "Guild" });

            migrationBuilder.CreateIndex(
                name: "IX_MyLands_GuildOptionsId",
                table: "MyLands",
                column: "GuildOptionsId");

            migrationBuilder.CreateIndex(
                name: "IX_OwnedRoles_PenaltyInfoId",
                table: "OwnedRoles",
                column: "PenaltyInfoId");

            migrationBuilder.CreateIndex(
                name: "IX_Penalties_User_Guild_Type",
                table: "Penalties",
                columns: new[] { "User", "Guild", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_Raports_GuildOptionsId",
                table: "Raports",
                column: "GuildOptionsId");

            migrationBuilder.CreateIndex(
                name: "IX_RaritysExcludedFromPacks_BoosterPackId",
                table: "RaritysExcludedFromPacks",
                column: "BoosterPackId");

            migrationBuilder.CreateIndex(
                name: "IX_SelfRoles_GuildOptionsId",
                table: "SelfRoles",
                column: "GuildOptionsId");

            migrationBuilder.CreateIndex(
                name: "IX_SlotMachineConfigs_UserId",
                table: "SlotMachineConfigs",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SystemData_MeasureDate",
                table: "SystemData",
                column: "MeasureDate");

            migrationBuilder.CreateIndex(
                name: "IX_TagCardRelations_CardId",
                table: "TagCardRelations",
                column: "CardId");

            migrationBuilder.CreateIndex(
                name: "GameDeckId",
                table: "Tags",
                column: "GameDeckId");

            migrationBuilder.CreateIndex(
                name: "IX_TimeStatuses_BValue",
                table: "TimeStatuses",
                column: "BValue");

            migrationBuilder.CreateIndex(
                name: "IX_TimeStatuses_Guild",
                table: "TimeStatuses",
                column: "Guild");

            migrationBuilder.CreateIndex(
                name: "IX_TimeStatuses_Type",
                table: "TimeStatuses",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_TimeStatuses_UserId",
                table: "TimeStatuses",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_TransferData_DiscordId_Date",
                table: "TransferData",
                columns: new[] { "DiscordId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_TransferData_ShindenId",
                table: "TransferData",
                column: "ShindenId");

            migrationBuilder.CreateIndex(
                name: "IX_UserActivities_ShindenId",
                table: "UserActivities",
                column: "ShindenId");

            migrationBuilder.CreateIndex(
                name: "IX_UserApiKeys_KeyHash",
                table: "UserApiKeys",
                column: "KeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserApiKeys_UserId_Application",
                table: "UserApiKeys",
                columns: new[] { "UserId", "Application" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Shinden",
                table: "Users",
                column: "Shinden");

            migrationBuilder.CreateIndex(
                name: "IX_UsersData_GuildId_MeasureDate",
                table: "UsersData",
                columns: new[] { "GuildId", "MeasureDate" });

            migrationBuilder.CreateIndex(
                name: "IX_UsersData_UserId_MeasureDate",
                table: "UsersData",
                columns: new[] { "UserId", "MeasureDate" });

            migrationBuilder.CreateIndex(
                name: "IX_UsersStats_UserId",
                table: "UsersStats",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WaifuCommandChannels_WaifuId",
                table: "WaifuCommandChannels",
                column: "WaifuId");

            migrationBuilder.CreateIndex(
                name: "IX_WaifuFightChannels_WaifuId",
                table: "WaifuFightChannels",
                column: "WaifuId");

            migrationBuilder.CreateIndex(
                name: "IX_Waifus_GuildOptionsId",
                table: "Waifus",
                column: "GuildOptionsId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Wishes_GameDeckId",
                table: "Wishes",
                column: "GameDeckId");

            migrationBuilder.CreateIndex(
                name: "IX_Wishes_ObjectId_Type",
                table: "Wishes",
                columns: new[] { "ObjectId", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_Wishes_Type",
                table: "Wishes",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "KCCount",
                table: "WishlistCountData",
                column: "Count");

            migrationBuilder.CreateIndex(
                name: "IX_WithoutExpChannels_GuildOptionsId",
                table: "WithoutExpChannels",
                column: "GuildOptionsId");

            migrationBuilder.CreateIndex(
                name: "IX_WithoutSupervisionChannels_GuildOptionsId",
                table: "WithoutSupervisionChannels",
                column: "GuildOptionsId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BoosterPackCharacters");

            migrationBuilder.DropTable(
                name: "CardPvPStats");

            migrationBuilder.DropTable(
                name: "CommandChannels");

            migrationBuilder.DropTable(
                name: "CommandsData");

            migrationBuilder.DropTable(
                name: "ExpContainers");

            migrationBuilder.DropTable(
                name: "Figures");

            migrationBuilder.DropTable(
                name: "IdempotencyRecords");

            migrationBuilder.DropTable(
                name: "IgnoredChannels");

            migrationBuilder.DropTable(
                name: "Items");

            migrationBuilder.DropTable(
                name: "LevelRoles");

            migrationBuilder.DropTable(
                name: "ModeratorRoles");

            migrationBuilder.DropTable(
                name: "MuteModifiers");

            migrationBuilder.DropTable(
                name: "MyLands");

            migrationBuilder.DropTable(
                name: "OwnedRoles");

            migrationBuilder.DropTable(
                name: "Raports");

            migrationBuilder.DropTable(
                name: "RaritysExcludedFromPacks");

            migrationBuilder.DropTable(
                name: "SelfRoles");

            migrationBuilder.DropTable(
                name: "SlotMachineConfigs");

            migrationBuilder.DropTable(
                name: "SystemData");

            migrationBuilder.DropTable(
                name: "TagCardRelations");

            migrationBuilder.DropTable(
                name: "TimeStatuses");

            migrationBuilder.DropTable(
                name: "TransferData");

            migrationBuilder.DropTable(
                name: "UserActivities");

            migrationBuilder.DropTable(
                name: "UserApiKeys");

            migrationBuilder.DropTable(
                name: "UsersData");

            migrationBuilder.DropTable(
                name: "UsersStats");

            migrationBuilder.DropTable(
                name: "WaifuCommandChannels");

            migrationBuilder.DropTable(
                name: "WaifuFightChannels");

            migrationBuilder.DropTable(
                name: "Wishes");

            migrationBuilder.DropTable(
                name: "WishlistCountData");

            migrationBuilder.DropTable(
                name: "WithoutExpChannels");

            migrationBuilder.DropTable(
                name: "WithoutSupervisionChannels");

            migrationBuilder.DropTable(
                name: "Penalties");

            migrationBuilder.DropTable(
                name: "BoosterPacks");

            migrationBuilder.DropTable(
                name: "Cards");

            migrationBuilder.DropTable(
                name: "Tags");

            migrationBuilder.DropTable(
                name: "Waifus");

            migrationBuilder.DropTable(
                name: "GameDecks");

            migrationBuilder.DropTable(
                name: "Guilds");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
