#pragma warning disable 1591

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Sanakan.Database;
using Sanakan.Database.Models;
using Sanakan.Database.Models.Analytics;
using Sanakan.Services.PocketWaifu;
using Sanakan.Services.Time;

namespace Sanakan.Services
{
    // Game statistics for charts. Counters are kept in memory and added to GameStats every 10 minutes; who played on which
    // day goes to PlayerDays and PvP duels to DuelResults. Once a day a snapshot of the economy is taken from the
    // database. Currency, cards, items and packs are counted from what a save really writes (GameTelemetry.Interceptor),
    // labelled with the command or API call the save belongs to (Reason).
    public class GameTelemetry
    {
        public static GameTelemetry Default { get; } = new GameTelemetry();

        public const int LabelLength = 128;
        public static readonly TimeSpan Retention = TimeSpan.FromDays(365);
        private const int CleanupBatch = 5000;

        private static readonly AsyncLocal<string> _reason = new AsyncLocal<string>();

        // the command or API call the current code runs for
        public static string Reason
        {
            get => _reason.Value ?? "other";
            set => _reason.Value = value;
        }

        private sealed class Day
        {
            public int Commands;
            public int IsNew;
        }

        private ConcurrentDictionary<(DateTime Date, string Metric, string Label), long> _counters = new();
        private ConcurrentDictionary<(DateTime Date, ulong User), Day> _days = new();
        private ConcurrentQueue<DuelResult> _duels = new();
        private readonly SemaphoreSlim _writing = new SemaphoreSlim(1, 1);
        private static readonly TimeSpan FirstRun = TimeSpan.FromMinutes(1);
        // a crash loses at most this much
        private static readonly TimeSpan FlushEvery = TimeSpan.FromMinutes(10);
        private Timer _flush;
        private Timer _nightly;
        private volatile bool _failing;

        public Func<DateTime> Now { get; set; } = () => DateTime.Now;
        public ILogger Logger { get; set; }
        // the staff's own accounts (Dev in the config): what they have and do is not counted
        public Func<IReadOnlyCollection<ulong>> ExcludedUsers { get; set; } = () => Array.Empty<ulong>();

        private IReadOnlyCollection<ulong> Excluded() => ExcludedUsers?.Invoke() ?? Array.Empty<ulong>();
        public bool IsExcluded(ulong user) => Excluded().Contains(user);

        public SaveChangesInterceptor Interceptor { get; }

        public GameTelemetry()
        {
            Interceptor = new SaveInterceptor(this);
        }

        public void Add(string metric, string label, long value = 1)
        {
            if (value == 0) return;
            label ??= "";
            if (label.Length > LabelLength) label = label.Substring(0, LabelLength);
            _counters.AddOrUpdate((Now().Date, metric, label), value, (_, old) => old + value);
        }

        public void Command(ulong user, string name, bool failed)
        {
            if (IsExcluded(user)) return;
            Interlocked.Increment(ref DayOf(user).Commands);
            Add("command", name);
            if (failed) Add("command_failed", name);
        }

        public void ErrorReply(ulong user, string command, string text = null)
        {
            if (IsExcluded(user)) return;
            Add("command_error_reply", command);
            if (!string.IsNullOrWhiteSpace(text))
                Add("command_refusal", $"{command}:{RefusalText(text)}");
        }

        private static readonly Regex DiscordTokens = new Regex(@"<a?:\w+:\d+>|<[@#][!&]?\d+>|<t:\d+(:\w)?>", RegexOptions.Compiled);
        private static readonly Regex Marked = new Regex(@"\*\*[^*]*\*\*|`[^`]*`", RegexOptions.Compiled);
        private static readonly Regex Numbers = new Regex(@"\d+([.,]\d+)?", RegexOptions.Compiled);
        private static readonly Regex Spaces = new Regex(@"\s+", RegexOptions.Compiled);

        // the same refusal for every player: without mentions, names in bold or code, and numbers
        public static string RefusalText(string text)
        {
            var line = text.Split('\n')[0];
            line = DiscordTokens.Replace(line, "");
            line = Marked.Replace(line, "*");
            line = Numbers.Replace(line, "#");
            return Spaces.Replace(line, " ").Trim();
        }

        public void NewPlayer(ulong user)
        {
            Interlocked.Exchange(ref DayOf(user).IsNew, 1);
            Add("players_new", "");
        }

        public void Duel(DuelResult duel, ulong attacker, ulong defender)
        {
            if (IsExcluded(attacker) || IsExcluded(defender)) return;
            duel.Date = Now();
            _duels.Enqueue(duel);
        }

        private Day DayOf(ulong user) => _days.GetOrAdd((Now().Date, user), _ => new Day());

        public void Start(Func<DatabaseContext> createDb, ISystemTime time, ILogger logger, Func<IEnumerable<ShopPrice>> prices)
        {
            Now = time.Now;
            Logger = logger;
            _flush = new Timer(_ => _ = Run("flush", () => FlushAsync(createDb)), null, FirstRun, FlushEvery);
            _nightly = new Timer(_ => _ = RunNightlyAsync(createDb, prices), null, UntilNight(), Timeout.InfiniteTimeSpan);

            // a bot that was down at midnight, e.g. for an update, makes up for that night's snapshot
            _ = Task.Delay(FirstRun).ContinueWith(_ => Run("snapshot", async () =>
            {
                var yesterday = Now().Date.AddDays(-1);
                if (!await HasSnapshotAsync(createDb, yesterday))
                    await SnapshotAsync(createDb, yesterday, prices?.Invoke());
            })).Unwrap();
        }

        public static async Task<bool> HasSnapshotAsync(Func<DatabaseContext> createDb, DateTime day)
        {
            using var db = createDb();
            return await db.GameStats.AnyAsync(x => x.Date == day.Date && x.Metric == "players_active" && x.Label == "");
        }

        // five minutes after midnight, so the last minutes of the day are in
        private TimeSpan UntilNight() => Now().Date.AddDays(1).AddMinutes(5) - Now();

        private async Task RunNightlyAsync(Func<DatabaseContext> createDb, Func<IEnumerable<ShopPrice>> prices)
        {
            await Run("snapshot", async () =>
            {
                await FlushAsync(createDb);
                await SnapshotAsync(createDb, Now().Date.AddDays(-1), prices?.Invoke());
                await CleanupAsync(createDb);
            });
            _nightly?.Change(UntilNight(), Timeout.InfiniteTimeSpan);
        }

        private async Task Run(string what, Func<Task> work)
        {
            try
            {
                await work();
                if (_failing)
                {
                    _failing = false;
                    Logger?.Log("Telemetry: works again");
                }
            }
            catch (Exception ex)
            {
                // only a change is logged; the counters wait in memory for the next try
                if (!_failing)
                {
                    _failing = true;
                    Logger?.LogError($"Telemetry: {what}: {ex.Message}");
                }
            }
        }

        // adds what was counted since the last flush; on failure everything goes back for the next one
        public async Task FlushAsync(Func<DatabaseContext> createDb)
        {
            await _writing.WaitAsync();
            var counters = Interlocked.Exchange(ref _counters, new ConcurrentDictionary<(DateTime Date, string Metric, string Label), long>());
            var days = Interlocked.Exchange(ref _days, new ConcurrentDictionary<(DateTime Date, ulong User), Day>());
            var duels = Interlocked.Exchange(ref _duels, new ConcurrentQueue<DuelResult>());
            try
            {
                if (counters.IsEmpty && days.IsEmpty && duels.IsEmpty)
                    return;

                using var db = createDb();
                var dates = counters.Keys.Select(x => x.Date).Concat(days.Keys.Select(x => x.Date)).Distinct().ToList();

                var stats = (await db.GameStats.Where(x => dates.Contains(x.Date)).ToListAsync())
                    .ToDictionary(x => (x.Date, x.Metric, x.Label));
                foreach (var ((date, metric, label), value) in counters)
                {
                    if (stats.TryGetValue((date, metric, label), out var row))
                        row.Value += value;
                    else
                        db.GameStats.Add(new GameStat { Date = date, Metric = metric, Label = label, Value = value });
                }

                var users = days.Keys.Select(x => x.User).Distinct().ToList();
                var known = (await db.PlayerDays.Where(x => dates.Contains(x.Date) && users.Contains(x.UserId)).ToListAsync())
                    .ToDictionary(x => (x.Date, x.UserId));
                foreach (var ((date, user), day) in days)
                {
                    if (!known.TryGetValue((date, user), out var row))
                        db.PlayerDays.Add(row = new PlayerDay { Date = date, UserId = user });
                    row.Commands += day.Commands;
                    row.IsNew |= day.IsNew != 0;
                }

                db.DuelResults.AddRange(duels);
                await db.SaveChangesAsync();
            }
            catch
            {
                foreach (var (key, value) in counters)
                    _counters.AddOrUpdate(key, value, (_, old) => old + value);
                foreach (var (key, day) in days)
                {
                    var now = _days.GetOrAdd(key, _ => new Day());
                    Interlocked.Add(ref now.Commands, day.Commands);
                    if (day.IsNew != 0) Interlocked.Exchange(ref now.IsNew, 1);
                }
                foreach (var duel in duels)
                    _duels.Enqueue(duel);
                throw;
            }
            finally
            {
                _writing.Release();
            }
        }

        public sealed class ShopPrice
        {
            // shop:number:name, see Waifu.GetShopItemLabel
            public string Label { get; init; }
            public CurrencyType Currency { get; init; }
            public long Cost { get; init; }
        }

        // the state at the end of the day, stored under that day; running it again replaces the values
        public async Task SnapshotAsync(Func<DatabaseContext> createDb, DateTime day, IEnumerable<ShopPrice> prices = null)
        {
            day = day.Date;
            var next = day.AddDays(1);
            var values = new Dictionary<(DateTime Date, string Metric, string Label), long>();
            void Set(DateTime date, string metric, string label, long value) => values[(date, metric, label)] = value;
            var excluded = Excluded().ToList();

            using (var db = createDb())
            {
                var recent = await db.PlayerDays.AsNoTracking().Where(x => x.Date >= day.AddDays(-30) && x.Date < next && !excluded.Contains(x.UserId))
                    .Select(x => new { x.Date, x.UserId, x.Commands, x.IsNew }).ToListAsync();
                var played = recent.Where(x => x.Commands > 0).ToList();
                bool PlayedOn(DateTime date, ulong user) => played.Any(x => x.Date == date && x.UserId == user);

                Set(day, "players_active", "", played.Count(x => x.Date == day));
                Set(day, "players_active", "30d", played.Select(x => x.UserId).Distinct().Count());

                // of the new players who used a command on their first day, how many came back after N days
                foreach (var after in new[] { 1, 7, 30 })
                {
                    var start = day.AddDays(-after);
                    var cohort = played.Where(x => x.Date == start && x.IsNew).Select(x => x.UserId).ToList();
                    if (cohort.Count > 0)
                        Set(start, "retention", $"d{after}", Permille(cohort.Count(x => PlayedOn(day, x)), cohort.Count));
                }

                // where the new players of a week ago got to
                var weekAgo = day.AddDays(-7);
                var newbies = played.Where(x => x.Date == weekAgo && x.IsNew).Select(x => x.UserId).ToList();
                if (newbies.Count > 0)
                {
                    var progress = await db.Users.AsNoTracking().Where(x => newbies.Contains(x.Id)).Select(x => new
                    {
                        Cards = x.GameDeck.Cards.Count,
                        Packs = x.Stats.OpenedBoosterPacks + x.Stats.OpenedBoosterPacksActivity,
                        x.Level,
                    }).ToListAsync();
                    Set(weekAgo, "newbies_d7", "players", newbies.Count);
                    Set(weekAgo, "newbies_d7", "cards_p50", Percentile(progress.Select(x => (long)x.Cards), 50));
                    Set(weekAgo, "newbies_d7", "packs_opened_p50", Percentile(progress.Select(x => x.Packs), 50));
                    Set(weekAgo, "newbies_d7", "level_p50", Percentile(progress.Select(x => x.Level), 50));
                    Set(weekAgo, "newbies_d7", "active_days_avg_x10",
                        newbies.Sum(u => played.Count(x => x.UserId == u && x.Date >= weekAgo)) * 10L / newbies.Count);
                }

                // what the players of the last 30 days have and what they can buy
                var active = played.Select(x => x.UserId).Distinct().ToList();
                var wallets = active.Count == 0 ? new List<Dictionary<CurrencyType, long>>()
                    : (await db.Users.AsNoTracking().Where(x => active.Contains(x.Id))
                        .Select(x => new { x.ScCnt, x.TcCnt, x.AcCnt, x.GameDeck.CTCnt, x.GameDeck.PVPCoins }).ToListAsync())
                        .Select(x => new Dictionary<CurrencyType, long>
                        {
                            [CurrencyType.SC] = x.ScCnt, [CurrencyType.TC] = x.TcCnt, [CurrencyType.AC] = x.AcCnt,
                            [CurrencyType.CT] = x.CTCnt, [CurrencyType.PC] = x.PVPCoins,
                        }).ToList();
                foreach (var currency in Enum.GetValues<CurrencyType>())
                {
                    var amounts = wallets.Select(x => x[currency]).ToList();
                    foreach (var p in new[] { 50, 90, 99 })
                        Set(day, "balance", $"{currency}:p{p}", Percentile(amounts, p));
                }
                foreach (var price in prices ?? Enumerable.Empty<ShopPrice>())
                    Set(day, "can_afford", price.Label,Permille(wallets.Count(x => x[price.Currency] >= price.Cost), wallets.Count));

                var users = db.Users.Where(x => !excluded.Contains(x.Id));
                var decks = db.GameDecks.Where(x => !excluded.Contains(x.UserId));
                Set(day, "circulation", "SC", await users.SumAsync(x => x.ScCnt));
                Set(day, "circulation", "TC", await users.SumAsync(x => x.TcCnt));
                Set(day, "circulation", "AC", await users.SumAsync(x => x.AcCnt));
                Set(day, "circulation", "CT", await decks.SumAsync(x => x.CTCnt));
                Set(day, "circulation", "PC", await decks.SumAsync(x => x.PVPCoins));

                // the staff's cards are taken away afterwards, so counting all cards can still use the rarity index
                var staffCards = excluded.Count == 0 ? new Dictionary<Rarity, long>()
                    : (await db.Cards.Where(x => excluded.Contains(x.GameDeckId)).GroupBy(x => x.Rarity).Select(g => new { g.Key, Count = g.LongCount() }).ToListAsync())
                        .ToDictionary(x => x.Key, x => x.Count);
                foreach (var x in await db.Cards.GroupBy(x => x.Rarity).Select(g => new { g.Key, Count = g.LongCount() }).ToListAsync())
                    Set(day, "cards_total", x.Key.ToString(), x.Count - staffCards.GetValueOrDefault(x.Key));
                foreach (var x in await db.Items.Where(x => !excluded.Contains(x.GameDeckId)).GroupBy(x => x.Type).Select(g => new { g.Key, Count = g.Sum(i => i.Count) }).ToListAsync())
                    Set(day, "items_total", x.Key.ToString(), x.Count);
                foreach (var x in await db.BoosterPacks.Where(x => !excluded.Contains(x.GameDeckId)).GroupBy(x => x.CardSourceFromPack).Select(g => new { g.Key, Count = g.LongCount() }).ToListAsync())
                    Set(day, "packs_unopened", x.Key.ToString(), x.Count);

                // TC sent from the site
                var transfers = await db.TransferData.AsNoTracking().Where(x => x.Date >= day && x.Date < next && !excluded.Contains(x.DiscordId))
                    .Select(x => new { x.Value, x.DiscordId, x.Source }).ToListAsync();
                foreach (var g in transfers.GroupBy(x => x.Source))
                {
                    Set(day, "tc_transfer", $"{g.Key}:sum", g.Sum(x => x.Value));
                    Set(day, "tc_transfer", $"{g.Key}:count", g.Count());
                    Set(day, "tc_transfer", $"{g.Key}:players", g.Select(x => x.DiscordId).Distinct().Count());
                }

                var exchanges = await db.CardExchanges.AsNoTracking()
                    .Where(x => x.Date >= day && x.Date < next && !excluded.Contains(x.User1Id) && !excluded.Contains(x.User2Id))
                    .Select(x => new { x.User1Id, x.User2Id, x.User1Gave, x.User2Gave }).ToListAsync();
                Set(day, "exchanges", "count", exchanges.Count);
                Set(day, "exchanges", "cards", exchanges.Sum(x => (long)x.User1Gave + x.User2Gave));
                // only one side gave cards: a gift, or cards moved to another account
                Set(day, "exchanges", "one_sided", exchanges.Count(x => x.User1Gave == 0 || x.User2Gave == 0));
                Set(day, "exchanges", "players", exchanges.SelectMany(x => new[] { x.User1Id, x.User2Id }).Distinct().Count());
                Set(day, "exchanges", "pairs", exchanges.Select(x => Math.Min(x.User1Id, x.User2Id) + ":" + Math.Max(x.User1Id, x.User2Id)).Distinct().Count());

                var gifts = await db.ScGifts.AsNoTracking()
                    .Where(x => x.Date >= day && x.Date < next && !excluded.Contains(x.FromUserId) && !excluded.Contains(x.ToUserId))
                    .Select(x => new { x.FromUserId, x.ToUserId, x.Sent, x.Received }).ToListAsync();
                Set(day, "sc_gift", "count", gifts.Count);
                Set(day, "sc_gift", "sent", gifts.Sum(x => x.Sent));
                Set(day, "sc_gift", "received", gifts.Sum(x => x.Received));
                Set(day, "sc_gift", "givers", gifts.Select(x => x.FromUserId).Distinct().Count());
                Set(day, "sc_gift", "recipients", gifts.Select(x => x.ToUserId).Distinct().Count());
            }

            await _writing.WaitAsync();
            try
            {
                using var db = createDb();
                var dates = values.Keys.Select(x => x.Date).Distinct().ToList();
                var metrics = values.Keys.Select(x => x.Metric).Distinct().ToList();
                var stats = (await db.GameStats.Where(x => dates.Contains(x.Date) && metrics.Contains(x.Metric)).ToListAsync())
                    .ToDictionary(x => (x.Date, x.Metric, x.Label));
                foreach (var ((date, metric, label), value) in values)
                {
                    if (stats.TryGetValue((date, metric, label), out var row))
                        row.Value = value;
                    else
                        db.GameStats.Add(new GameStat { Date = date, Metric = metric, Label = label, Value = value });
                }
                await db.SaveChangesAsync();
            }
            finally
            {
                _writing.Release();
            }
        }

        // PlayerDays, DuelResults and the RAM measurements are kept for a year, the daily values stay
        public async Task CleanupAsync(Func<DatabaseContext> createDb)
        {
            var cutoff = Now().Date - Retention;
            using var db = createDb();
            await db.PlayerDays.Where(x => x.Date < cutoff).ExecuteDeleteAsync();
            await db.DuelResults.Where(x => x.Date < cutoff).ExecuteDeleteAsync();

            // in batches: the first run after years of measurements removes hundreds of thousands of rows
            while (true)
            {
                var ids = await db.SystemData.Where(x => x.MeasureDate < cutoff).Select(x => x.Id).Take(CleanupBatch).ToListAsync();
                if (ids.Count == 0) break;
                await db.SystemData.Where(x => ids.Contains(x.Id)).ExecuteDeleteAsync();
            }
        }

        private static long Permille(int part, int whole) => whole == 0 ? 0 : part * 1000L / whole;

        private static long Percentile(IEnumerable<long> values, int p)
        {
            var sorted = values.OrderBy(x => x).ToList();
            if (sorted.Count == 0) return 0;
            return sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(p / 100.0 * sorted.Count) - 1)];
        }

        public static string Rarities(IEnumerable<Card> cards) => string.Join(",", cards.OrderBy(x => x.Rarity).Select(x => x.Rarity));

        // in the same order as Rarities, so the n-th dere belongs to the n-th rarity
        public static string Deres(IEnumerable<Card> cards) => string.Join(",", cards.OrderBy(x => x.Rarity).Select(x => x.Dere));

        // collects before a save what it changes, counts it only after the save succeeded
        private sealed class SaveInterceptor : SaveChangesInterceptor
        {
            private readonly GameTelemetry _telemetry;

            public SaveInterceptor(GameTelemetry telemetry) => _telemetry = telemetry;

            public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
            {
                Remember(eventData.Context);
                return base.SavingChanges(eventData, result);
            }

            public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
                InterceptionResult<int> result, CancellationToken cancellationToken = default)
            {
                Remember(eventData.Context);
                return base.SavingChangesAsync(eventData, result, cancellationToken);
            }

            public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
            {
                Commit(eventData.Context);
                return base.SavedChanges(eventData, result);
            }

            public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
                CancellationToken cancellationToken = default)
            {
                Commit(eventData.Context);
                return base.SavedChangesAsync(eventData, result, cancellationToken);
            }

            private void Remember(DbContext context)
            {
                if (context is DatabaseContext db)
                    db.PendingTelemetry = Capture(context, _telemetry.Excluded());
            }

            private void Commit(DbContext context)
            {
                if (context is not DatabaseContext db || db.PendingTelemetry == null)
                    return;

                var pending = db.PendingTelemetry;
                db.PendingTelemetry = null;
                db.TelemetryNotes.Clear();
                foreach (var (metric, label, value, newPlayer) in pending)
                {
                    if (newPlayer != 0)
                        _telemetry.NewPlayer(newPlayer);
                    else
                        _telemetry.Add(metric, label, value);
                }
            }
        }

        public static List<(string Metric, string Label, long Value, ulong NewPlayer)> Capture(DbContext context, IReadOnlyCollection<ulong> excluded = null)
        {
            var found = new List<(string, string, long, ulong)>();
            var reason = Reason;
            excluded ??= Array.Empty<ulong>();

            // a removed row is told by its owner as it was loaded
            bool OfExcluded(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry, string ownerProperty)
            {
                if (excluded.Count == 0) return false;
                var owner = entry.Property(ownerProperty);
                return excluded.Contains(Convert.ToUInt64(entry.State == EntityState.Deleted ? owner.OriginalValue : owner.CurrentValue));
            }

            void Flow(string what, string label, long change)
            {
                if (change > 0) found.Add(($"{what}_in", label, change, 0));
                else if (change < 0) found.Add(($"{what}_out", label, -change, 0));
            }

            // a removed row takes all it had with it
            double Change(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry, string property)
            {
                var value = entry.Property(property);
                var now = entry.State == EntityState.Deleted ? 0 : Convert.ToDouble(value.CurrentValue);
                var before = entry.State == EntityState.Added ? 0 : Convert.ToDouble(value.OriginalValue);
                return now - before;
            }

            void Currency(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry, string property, CurrencyType currency)
                => Flow("currency", $"{currency}:{reason}", (long)Change(entry, property));

            // karma and exp are fractions, kept in hundredths
            void Hundredths(string what, string label, double change) => Flow(what, label, (long)Math.Round(change * 100));

            foreach (var entry in context.ChangeTracker.Entries())
            {
                if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                    continue;

                var owner = entry.Entity switch
                {
                    User => nameof(User.Id),
                    GameDeck => nameof(GameDeck.UserId),
                    UserStats => nameof(UserStats.UserId),
                    Card or Item or BoosterPack or ExpContainer or Figure => nameof(Card.GameDeckId),
                    _ => null,
                };
                if (owner != null && OfExcluded(entry, owner))
                    continue;

                switch (entry.Entity)
                {
                    case User user:
                        if (entry.State == EntityState.Added)
                            found.Add(("", "", 0, user.Id));
                        Currency(entry, nameof(User.ScCnt), CurrencyType.SC);
                        Currency(entry, nameof(User.TcCnt), CurrencyType.TC);
                        Currency(entry, nameof(User.AcCnt), CurrencyType.AC);
                        break;

                    case GameDeck:
                        Currency(entry, nameof(GameDeck.CTCnt), CurrencyType.CT);
                        Currency(entry, nameof(GameDeck.PVPCoins), CurrencyType.PC);
                        if (entry.State != EntityState.Deleted)
                            Hundredths("karma", reason, Change(entry, nameof(GameDeck.Karma)));
                        break;

                    case UserStats when entry.State != EntityState.Deleted:
                        foreach (var stat in entry.Properties.Where(x => x.Metadata.ClrType == typeof(long)))
                        {
                            var statChange = Convert.ToInt64(stat.CurrentValue) - (entry.State == EntityState.Added ? 0 : Convert.ToInt64(stat.OriginalValue));
                            if (statChange != 0)
                                found.Add(("user_stat", $"{stat.Metadata.Name}:{reason}", statChange, 0));
                        }
                        break;

                    case ExpContainer:
                        Hundredths("exp", $"Chest:{reason}", Change(entry, nameof(ExpContainer.ExpCount)));
                        break;

                    case Figure:
                        Hundredths("exp", $"Figure:{reason}", Change(entry, nameof(Figure.ExpCnt)));
                        break;

                    case Card card:
                        Hundredths("exp", $"Card:{reason}", Change(entry, nameof(Card.ExpCnt)));
                        if (entry.State == EntityState.Added)
                            found.Add(("card_in", $"{card.Rarity}:{card.Source}", 1, 0));
                        else if (entry.State == EntityState.Deleted)
                            found.Add(("card_out", $"{entry.Property(nameof(Card.Rarity)).OriginalValue}:{reason}", 1, 0));
                        else
                        {
                            var rarity = entry.Property(nameof(Card.Rarity));
                            if (rarity.IsModified && !Equals(rarity.OriginalValue, card.Rarity))
                            {
                                var restarted = card.RestartCnt > Convert.ToInt32(entry.Property(nameof(Card.RestartCnt)).OriginalValue);
                                found.Add((restarted ? "card_restart" : "card_upgrade", $"{rarity.OriginalValue}>{card.Rarity}", 1, 0));
                            }
                        }
                        break;

                    case Item item:
                        var count = entry.Property(nameof(Item.Count));
                        var change = entry.State switch
                        {
                            EntityState.Added => item.Count,
                            EntityState.Deleted => -(long)count.OriginalValue,
                            _ => item.Count - (long)count.OriginalValue,
                        };
                        Flow("item", $"{item.Type}:{reason}", change);
                        break;

                    case BoosterPack pack when entry.State == EntityState.Added:
                        found.Add(("pack_in", $"{pack.CardSourceFromPack}:{reason}", 1, 0));
                        break;

                    case BoosterPack pack when entry.State == EntityState.Deleted:
                        found.Add(("pack_out", $"{pack.CardSourceFromPack}:{reason}", 1, 0));
                        break;
                }
            }

            if (context is DatabaseContext db)
            {
                foreach (var (noteOwner, metric, label, value) in db.TelemetryNotes)
                {
                    if (!excluded.Contains(noteOwner))
                        found.Add((metric, label, value, 0));
                }
            }
            return found;
        }

        // a drawn card discarded before it was ever saved: it came and went with the next save
        public static void CountDiscardedNewCard(DatabaseContext db, Card card, ulong owner)
        {
            db.TelemetryNotes.Add((owner, "card_in", $"{card.Rarity}:{card.Source}", 1));
            db.TelemetryNotes.Add((owner, "card_out", $"{card.Rarity}:{Reason}", 1));
        }

        public static void CountAfterSave(DatabaseContext db, ulong owner, string metric, string label, long value = 1)
            => db.TelemetryNotes.Add((owner, metric, label, value));
    }
}
