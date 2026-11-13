#pragma warning disable 1591

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Sanakan.Config;
using Sanakan.Database.Models;
using Sanakan.Database.Models.Analytics;
using Sanakan.Database.Models.Configuration;
using Sanakan.Database.Models.Management;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;

namespace Sanakan.Database
{
    public class DatabaseContext : DbContext
    {
        private IConfig _config;

        // Wyłącza unieważnianie cache w CacheActivity dla danego zapisu (np. często zapisywany exp).
        public bool SuppressCacheInvalidation { get; set; }

        // Tagi cache wyliczone przed zapisem, unieważniane po commicie (używa CacheActivity).
        public List<string> PendingCacheTags { get; set; }

        // decks whose stored power is refreshed after the save (used by PowerActivity)
        public HashSet<ulong> PendingPowerDecks { get; set; }

        // what the save changes in the game, counted after it succeeds (GameTelemetry)
        public List<(string Metric, string Label, long Value, ulong NewPlayer)> PendingTelemetry { get; set; }

        public DatabaseContext(IConfig config) : base()
        {
            _config = config;
        }

        public DbSet<User> Users { get; set; }
        public DbSet<UserStats> UsersStats { get; set; }
        public DbSet<TimeStatus> TimeStatuses { get; set; }
        public DbSet<SlotMachineConfig> SlotMachineConfigs { get; set; }
        public DbSet<GameDeck> GameDecks { get; set; }
        public DbSet<ExpContainer> ExpContainers { get; set; }
        public DbSet<Card> Cards { get; set; }
        public DbSet<Item> Items { get; set; }
        public DbSet<BoosterPack> BoosterPacks { get; set; }
        public DbSet<CardPvPStats> CardPvPStats { get; set; }
        public DbSet<BoosterPackCharacter> BoosterPackCharacters { get; set; }
        public DbSet<WishlistObject> Wishes { get; set; }
        public DbSet<Figure> Figures { get; set; }
        public DbSet<RarityExcluded> RaritysExcludedFromPacks { get; set; }
        public DbSet<SelfRole> SelfRoles { get; set; }
        public DbSet<GuildOptions> Guilds { get; set; }
        public DbSet<LevelRole> LevelRoles { get; set; }
        public DbSet<CommandChannel> CommandChannels { get; set; }
        public DbSet<ModeratorRoles> ModeratorRoles { get; set; }
        public DbSet<WithoutExpChannel> WithoutExpChannels { get; set; }
        public DbSet<WithoutMsgCntChannel> IgnoredChannels { get; set; }
        public DbSet<WithoutSupervisionChannel> WithoutSupervisionChannels { get; set; }
        public DbSet<MyLand> MyLands { get; set; }
        public DbSet<Waifu> Waifus { get; set; }
        public DbSet<Raport> Raports { get; set; }
        public DbSet<WaifuCommandChannel> WaifuCommandChannels { get; set; }
        public DbSet<WaifuFightChannel> WaifuFightChannels { get; set; }
        public DbSet<PenaltyInfo> Penalties { get; set; }
        public DbSet<OwnedRole> OwnedRoles { get; set; }
        public DbSet<MuteModifier> MuteModifiers { get; set; }
        public DbSet<SystemAnalytics> SystemData { get; set; }
        public DbSet<TransferAnalytics> TransferData { get; set; }
        public DbSet<GameStat> GameStats { get; set; }
        public DbSet<PlayerDay> PlayerDays { get; set; }
        public DbSet<DuelResult> DuelResults { get; set; }
        public DbSet<WishlistCount> WishlistCountData { get; set; }
        public DbSet<UserActivity> UserActivities { get; set; }
        public DbSet<TagCardRelation> TagCardRelations { get; set; }
        public DbSet<Tag> Tags { get; set; }
        public DbSet<UserApiKey> UserApiKeys { get; set; }
        public DbSet<IdempotencyRecord> IdempotencyRecords { get; set; }

        public const string PictureKindSql = "CASE WHEN CustomImage IS NULL THEN (CASE WHEN Image IS NULL THEN 0 ELSE 1 END) ELSE (CASE WHEN IsAnimatedImage THEN 3 ELSE 2 END) END";
        public const string CursedSql = "CASE WHEN Curse <> 0 THEN 1 ELSE 0 END";
        public const string CardsRarityIndex = "IX_Cards_Rarity_Quality_BorderOverflow";

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            var config = _config.Get();
            var builder = config.DatabaseProvider == global::Sanakan.Config.Model.DatabaseProvider.Sqlite
                ? optionsBuilder.UseSqlite(config.ConnectionString)
                : optionsBuilder.UseMySql(config.ConnectionString,
                    new MySqlServerVersion(new System.Version(8, 0, 43)), opt => opt.EnableRetryOnFailure());

            builder.AddInterceptors(DbActivity.Commands, DbActivity.Saves, PowerActivity.Interceptor, CacheActivity.Interceptor,
                Services.GameTelemetry.Default.Interceptor);
            if (config.DatabaseProvider == global::Sanakan.Config.Model.DatabaseProvider.Sqlite)
                builder.AddInterceptors(SqliteText.Interceptor);
            else
                builder.AddInterceptors(CardListHint.Interceptor);
        }

        public static async Task TestConnectionAsync(global::Sanakan.Config.Model.DatabaseProvider provider, string connectionString)
        {
            // ReadWrite: a mistyped SQLite path must fail instead of creating an empty database
            await using DbConnection connection = provider == global::Sanakan.Config.Model.DatabaseProvider.Sqlite
                ? new SqliteConnection(new SqliteConnectionStringBuilder(connectionString) { Mode = SqliteOpenMode.ReadWrite }.ToString())
                : new MySqlConnector.MySqlConnection(connectionString);
            await connection.OpenAsync();
        }

        public const string BaselineMigration = "20261113000443_Baseline";
        private const int MySqlTriggerNeedsSuper = 1419;

        // SQLite gets its schema straight from the model; MySQL from the migrations
        public void EnsureReady(ILogger logger = null)
        {
            if (Database.IsSqlite())
            {
                Database.EnsureCreated();
                // WAL lets reads run while a write is in progress; the mode is stored in the database file
                Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
                return;
            }

            if (!_config.Get().MigrateOnStart)
            {
                var waiting = Database.GetPendingMigrations().ToList();
                if (waiting.Count > 0)
                    logger?.Log($"Database: {waiting.Count} migration(s) not applied, MigrateOnStart is off: {string.Join(", ", waiting)}");
                return;
            }

            MarkBaselineOnOldDatabase(logger);
            var pending = Database.GetPendingMigrations().ToList();
            if (pending.Count == 0)
                return;

            logger?.Log($"Database: applying migration(s) {string.Join(", ", pending)}");
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                Database.Migrate();
            }
            catch (MySqlConnector.MySqlException ex) when (ex.Number == MySqlTriggerNeedsSuper)
            {
                logger?.Log("Database: with binary logging on, MySQL lets an account without SUPER create triggers only after "
                    + "log_bin_trust_function_creators = 1 (my.cnf, [mysqld]); set it and start the bot again");
                throw;
            }
            logger?.Log($"Database: migration(s) applied in {watch.Elapsed.TotalSeconds:0.0} s");
        }

        // a database made before the migrations (EnsureCreated, a dump of one) already has the baseline's tables
        private void MarkBaselineOnOldDatabase(ILogger logger)
        {
            if (!Database.CanConnect())
                return;

            var history = this.GetService<IHistoryRepository>();
            if (history.Exists() && history.GetAppliedMigrations().Count > 0)
                return;

            var hasUsers = Database.SqlQueryRaw<int>("SELECT COUNT(*) AS `Value` FROM information_schema.tables WHERE table_schema = DATABASE() AND LOWER(table_name) = 'users'")
                .AsEnumerable().First() > 0;
            if (!hasUsers)
                return;

            Database.ExecuteSqlRaw(history.GetCreateIfNotExistsScript());
            Database.ExecuteSqlRaw(history.GetInsertScript(new HistoryRow(BaselineMigration, ProductInfo.GetVersion())));
            logger?.Log($"Database: the existing schema is taken as {BaselineMigration}");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // User
            modelBuilder.Entity<User>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.Shinden);
            });

            modelBuilder.Entity<UserStats>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.HasOne(e => e.User)
                    .WithOne(u => u.Stats);
            });

            modelBuilder.Entity<SlotMachineConfig>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.HasOne(e => e.User)
                    .WithOne(u => u.SMConfig);
            });

            modelBuilder.Entity<TimeStatus>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.Guild);
                entity.HasIndex(e => e.Type);
                entity.HasIndex(e => e.BValue);

                entity.HasOne(e => e.User)
                    .WithMany(u => u.TimeStatuses);
            });

            modelBuilder.Entity<GameDeck>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.DeckPower);
                entity.HasIndex(e => e.WishlistIsPrivate);

                entity.HasOne(e => e.User)
                    .WithOne(u => u.GameDeck);
            });

            modelBuilder.Entity<ExpContainer>(entity =>
            {
                entity.HasKey(e => e.Id);
                // the name of the existing index in the production database
                entity.HasIndex(e => e.GameDeckId).IsUnique().HasDatabaseName("IX_ExpConteiners_GameDeckId");

                entity.HasOne(e => e.GameDeck)
                    .WithOne(u => u.ExpContainer);
            });

            modelBuilder.Entity<Figure>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.HasOne(e => e.GameDeck)
                    .WithMany(u => u.Figures);
            });

            modelBuilder.Entity<Tag>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.GameDeckId).HasDatabaseName("GameDeckId");

                entity.HasOne(e => e.GameDeck)
                    .WithMany(u => u.Tags);
            });

            modelBuilder.Entity<Card>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.Character);
                entity.HasIndex(e => e.FromFigure);
                entity.HasIndex(e => e.Unique);
                entity.HasIndex(e => e.CardPower);
                entity.HasIndex(e => e.Affection);
                entity.HasIndex(e => e.Dere);
                entity.HasIndex(e => e.Health);
                entity.HasIndex(e => e.Expedition);
                // a player's active cards (duel, exchange); with the power the deck power refresh reads only the index.
                // The plain deck index stays for reading a player's cards in id order
                entity.HasIndex(e => new { e.GameDeckId, e.Active, e.CardPower });
                entity.HasIndex(e => e.GameDeckId);
                // the card game profile counts a player's cards by these columns from the index alone
                entity.HasIndex(e => new { e.GameDeckId, e.Rarity, e.FromFigure, e.Quality, e.RestartCnt });
                // the site's card list sorts by the count and then the character
                entity.HasIndex(e => new { e.WhoWantsCount, e.Character });
                entity.HasIndex(e => new { e.AWhoWantsCount, e.Character });
                // varchar instead of longtext keeps the text in the row, the text search counts its results from this index
                entity.Property(e => e.Name).HasMaxLength(Card.NameMaxLength);
                entity.Property(e => e.Title).HasMaxLength(Card.TextMaxLength);
                entity.Property(e => e.Image).HasMaxLength(Card.TextMaxLength);
                entity.Property(e => e.CustomImage).HasMaxLength(Card.TextMaxLength);
                entity.Property(e => e.CustomBorder).HasMaxLength(Card.TextMaxLength);
                entity.HasIndex(e => new { e.Name, e.Title });
                // the most used sorts of the site's card lists, for all cards and for one player's
                entity.Property(e => e.PictureKind).HasComputedColumnSql(PictureKindSql, stored: true);
                entity.Property(e => e.Cursed).HasComputedColumnSql(CursedSql, stored: true);
                entity.HasIndex(e => new { e.PictureKind, e.CustomImageDate, e.Id });
                entity.HasIndex(e => new { e.GameDeckId, e.PictureKind, e.CustomImageDate, e.Id });
                entity.HasIndex(e => new { e.GameDeckId, e.Character });
                entity.HasIndex(e => new { e.GameDeckId, e.Fatigue });
                entity.HasIndex(e => new { e.GameDeckId, e.Cursed });
                // all cards sorted by rarity, read backwards for the reverse order
                entity.HasIndex(e => new { e.Rarity, e.Quality, e.BorderOverflow }).IsDescending(true, false, false).HasDatabaseName(CardsRarityIndex);

                entity.HasOne(e => e.GameDeck)
                    .WithMany(d => d.Cards);

                entity.HasMany(e => e.Tags)
                    .WithMany(t => t.Cards)
                    .UsingEntity<TagCardRelation>(
                        l => l.HasOne<Tag>(e => e.Tag).WithMany(e => e.Relation),
                        r => r.HasOne<Card>(e => e.Card).WithMany(e => e.Relation));
            });

            modelBuilder.Entity<TagCardRelation>(entity =>
            {
                entity.HasKey("TagId", "CardId");
                // PK pokrywa zapytania po TagId; osobny indeks obsługuje ładowanie tagów po CardId
                entity.HasIndex(e => e.CardId);
            });

            modelBuilder.Entity<Item>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.HasOne(e => e.GameDeck)
                    .WithMany(d => d.Items);
            });

            modelBuilder.Entity<WishlistObject>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.HasIndex(e => new { e.ObjectId, e.Type });
                entity.HasIndex(e => e.Type);

                entity.HasOne(e => e.GameDeck)
                    .WithMany(d => d.Wishes);
            });

            modelBuilder.Entity<BoosterPack>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Season).HasMaxLength(32);

                entity.HasOne(e => e.GameDeck)
                    .WithMany(d => d.BoosterPacks);
            });

            modelBuilder.Entity<CardPvPStats>(entity =>
            {
                entity.HasKey(e => e.Id);
                // the site profile counts the fights from the index alone
                entity.HasIndex(e => new { e.GameDeckId, e.Type, e.Result });

                entity.HasOne(e => e.GameDeck)
                    .WithMany(d => d.PvPStats);
            });

            modelBuilder.Entity<BoosterPackCharacter>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.HasOne(e => e.BoosterPack)
                    .WithMany(p => p.Characters);
            });

            modelBuilder.Entity<RarityExcluded>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.HasOne(e => e.BoosterPack)
                    .WithMany(p => p.RarityExcludedFromPack);
            });

            // GuildConfig
            modelBuilder.Entity<GuildOptions>(entity =>
            {
                entity.HasKey(e => e.Id);
            });

            modelBuilder.Entity<Waifu>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.HasMany(e => e.CommandChannels)
                    .WithOne(w => w.Waifu);
                entity.HasMany(e => e.FightChannels)
                    .WithOne(w => w.Waifu);
                entity.HasOne(e => e.GuildOptions)
                    .WithOne(g => g.WaifuConfig);
            });

            modelBuilder.Entity<Raport>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.HasOne(e => e.GuildOptions)
                    .WithMany(g => g.Raports);
            });

            modelBuilder.Entity<SelfRole>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.HasOne(e => e.GuildOptions)
                    .WithMany(g => g.SelfRoles);
            });

            modelBuilder.Entity<CommandChannel>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.HasOne(e => e.GuildOptions)
                    .WithMany(g => g.CommandChannels);
            });

            modelBuilder.Entity<WithoutMsgCntChannel>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.HasOne(e => e.GuildOptions)
                    .WithMany(g => g.IgnoredChannels);
            });

            modelBuilder.Entity<LevelRole>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.HasOne(e => e.GuildOptions)
                    .WithMany(g => g.RolesPerLevel);
            });

            modelBuilder.Entity<ModeratorRoles>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.HasOne(e => e.GuildOptions)
                    .WithMany(g => g.ModeratorRoles);
            });

            modelBuilder.Entity<WithoutExpChannel>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.HasOne(e => e.GuildOptions)
                    .WithMany(g => g.ChannelsWithoutExp);
            });

            modelBuilder.Entity<WithoutSupervisionChannel>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.HasOne(e => e.GuildOptions)
                    .WithMany(g => g.ChannelsWithoutSupervision);
            });

            modelBuilder.Entity<MyLand>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.HasOne(e => e.GuildOptions)
                     .WithMany(g => g.Lands);
            });

            modelBuilder.Entity<WaifuCommandChannel>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.HasOne(e => e.Waifu)
                    .WithMany(w => w.CommandChannels);
            });

            modelBuilder.Entity<WaifuFightChannel>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.HasOne(e => e.Waifu)
                    .WithMany(w => w.FightChannels);
            });

            // Managment
            modelBuilder.Entity<PenaltyInfo>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => new { e.User, e.Guild, e.Type });
            });

            modelBuilder.Entity<OwnedRole>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.HasOne(e => e.PenaltyInfo)
                    .WithMany(p => p.Roles);
            });

            modelBuilder.Entity<MuteModifier>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => new { e.User, e.Guild });
            });

            // Analytics
            modelBuilder.Entity<SystemAnalytics>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.MeasureDate);
            });

            modelBuilder.Entity<TransferAnalytics>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => new { e.DiscordId, e.Date });
                entity.HasIndex(e => e.ShindenId);
            });

            modelBuilder.Entity<GameStat>(entity =>
            {
                entity.HasKey(e => new { e.Date, e.Metric, e.Label });
                entity.Property(e => e.Metric).HasMaxLength(64);
                entity.Property(e => e.Label).HasMaxLength(Services.GameTelemetry.LabelLength);
            });

            modelBuilder.Entity<PlayerDay>(entity =>
            {
                entity.HasKey(e => new { e.Date, e.UserId });
            });

            modelBuilder.Entity<DuelResult>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.Date);
                entity.Property(e => e.AttackerCards).HasMaxLength(100);
                entity.Property(e => e.DefenderCards).HasMaxLength(100);
            });

            // Other
            // add trigger after update
            // UPDATE `Cards` SET `WhoWantsCount` = NEW.Count WHERE `Character` = NEW.Id
            modelBuilder.Entity<WishlistCount>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.Count).HasDatabaseName("KCCount");
            });

            modelBuilder.Entity<UserActivity>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.ShindenId);
            });

            modelBuilder.Entity<UserApiKey>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.KeyHash).HasMaxLength(64).IsRequired();
                entity.Property(e => e.Application).HasMaxLength(100).IsRequired();
                entity.HasIndex(e => e.KeyHash).IsUnique();
                entity.HasIndex(e => new { e.UserId, e.Application }).IsUnique();

                entity.HasOne<User>()
                    .WithMany()
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<IdempotencyRecord>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Scope).HasMaxLength(32).IsRequired();
                entity.Property(e => e.Key).HasMaxLength(64).IsRequired();
                entity.Property(e => e.RequestHash).HasMaxLength(64).IsRequired();
                entity.HasIndex(e => new { e.Scope, e.Key }).IsUnique();
                entity.HasIndex(e => e.CreatedAt);
            });

            if (Database.IsSqlite())
            {
                foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(x => x.GetProperties()).Where(x => x.ClrType == typeof(string)))
                    property.SetCollation(SqliteText.Collation);
            }
        }
    }
}
