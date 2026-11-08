#pragma warning disable 1591

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Caching.Memory;
using Sanakan.Services;
using static System.FormattableString;

namespace Sanakan
{
    public static class QueryCacheStats
    {
        private const int MaxKindsInReport = 8;
        // the daily report shares one Discord message (~2000 chars) with other sections
        private const int MaxReportLength = 1400;

        private sealed class Counters
        {
            public long Hits, Loads, Discarded, Invalidated, Expired, OverLimit, LoadMs, MaxLoadMs, Results, InvalidatedAgeMs;
        }

        private sealed class LiveCounters
        {
            public long Entries, Results;
        }

        private static readonly ConcurrentDictionary<string, LiveCounters> _live = new ConcurrentDictionary<string, LiveCounters>();
        private static ConcurrentDictionary<string, Counters> _hour = new ConcurrentDictionary<string, Counters>();
        private static ConcurrentDictionary<string, Counters> _day = new ConcurrentDictionary<string, Counters>();

        private static Timer _timer;
        private static DateTime _lastReport = DateTime.UtcNow;

        public static void Start(ILogger logger, TimeSpan interval)
        {
            _lastReport = DateTime.UtcNow;
            _timer = new Timer(_ =>
            {
                var report = Flush();
                if (report != null) logger.LogReport(report);
            }, null, interval, interval);
        }

        public static string Flush()
        {
            var stats = Interlocked.Exchange(ref _hour, new ConcurrentDictionary<string, Counters>());

            var now = DateTime.UtcNow;
            var minutes = (now - _lastReport).TotalMinutes;
            _lastReport = now;

            return Format(Invariant($"Cache ({minutes:0} min)"), stats);
        }

        public static string FlushDaily()
            => Format("Cache", Interlocked.Exchange(ref _day, new ConcurrentDictionary<string, Counters>())) ?? "Cache: no reads";

        public static void Hit(string kind)
        {
            Interlocked.Increment(ref Hour(kind).Hits);
            Interlocked.Increment(ref Day(kind).Hits);
        }

        public static void Load(string kind, long ms, int results, bool discarded)
        {
            Load(Hour(kind), ms, results, discarded);
            Load(Day(kind), ms, results, discarded);
        }

        public static void Stored(string kind, int results)
        {
            var live = Live(kind);
            Interlocked.Increment(ref live.Entries);
            Interlocked.Add(ref live.Results, results);
        }

        public static void Evicted(string kind, EvictionReason reason, long ageMs, int results)
        {
            var live = Live(kind);
            Interlocked.Decrement(ref live.Entries);
            Interlocked.Add(ref live.Results, -results);

            if (reason != EvictionReason.TokenExpired && reason != EvictionReason.Expired && reason != EvictionReason.Capacity)
                return;

            Evicted(Hour(kind), reason, ageMs);
            Evicted(Day(kind), reason, ageMs);
        }

        private static LiveCounters Live(string kind) => _live.GetOrAdd(kind, _ => new LiveCounters());
        private static Counters Hour(string kind) => _hour.GetOrAdd(kind, _ => new Counters());
        private static Counters Day(string kind) => _day.GetOrAdd(kind, _ => new Counters());

        private static void Load(Counters c, long ms, int results, bool discarded)
        {
            Interlocked.Increment(ref c.Loads);
            Interlocked.Add(ref c.LoadMs, ms);
            Interlocked.Add(ref c.Results, results);
            if (discarded) Interlocked.Increment(ref c.Discarded);

            var max = Interlocked.Read(ref c.MaxLoadMs);
            while (ms > max)
            {
                var seen = Interlocked.CompareExchange(ref c.MaxLoadMs, ms, max);
                if (seen == max) break;
                max = seen;
            }
        }

        private static void Evicted(Counters c, EvictionReason reason, long ageMs)
        {
            if (reason == EvictionReason.Expired)
            {
                Interlocked.Increment(ref c.Expired);
                return;
            }

            if (reason == EvictionReason.Capacity)
            {
                Interlocked.Increment(ref c.OverLimit);
                return;
            }

            Interlocked.Increment(ref c.Invalidated);
            Interlocked.Add(ref c.InvalidatedAgeMs, ageMs);
        }

        private static readonly string[] Header = { "query", "hit%", "reqs", "loads", "avg", "max", "rows", "exp", "inv", "inv age" };
        private static readonly bool[] RightAlign = { false, true, true, true, true, true, true, true, true, true };

        private static string Format(string title, ConcurrentDictionary<string, Counters> stats)
        {
            if (stats.IsEmpty) return null;

            var all = stats.ToList();
            var hits = all.Sum(x => x.Value.Hits);
            var loads = all.Sum(x => x.Value.Loads);
            var loadMs = all.Sum(x => x.Value.LoadMs);
            var overLimit = all.Sum(x => x.Value.OverLimit);

            var summary = Invariant($"{title}: hits {hits}/{hits + loads} ({Percent(hits, hits + loads)}), loads {loads} ({loadMs / 1000.0:0.0} s), ")
                + Invariant($"invalidated {all.Sum(x => x.Value.Invalidated)}, discarded {all.Sum(x => x.Value.Discarded)}")
                + (overLimit > 0 ? Invariant($", over limit {overLimit}") : "");

            var memory = Memory();
            var rows = new List<string[]> { Header };
            var top = all.OrderByDescending(x => x.Value.LoadMs).ThenByDescending(x => x.Value.Loads + x.Value.Hits).Take(MaxKindsInReport).ToList();
            foreach (var (kind, c) in top)
            {
                rows.Add(KindRow(kind, c));
                var length = summary.Length + memory.Length + TextTable.Render(rows, RightAlign).Sum(x => x.Length + 1);
                if (length > MaxReportLength)
                {
                    rows.RemoveAt(rows.Count - 1);
                    break;
                }
            }

            var sb = new StringBuilder(summary);
            if (rows.Count > 1)
                foreach (var line in TextTable.Render(rows, RightAlign))
                    sb.Append('\n').Append(line);

            if (all.Count > rows.Count - 1)
                sb.Append(Invariant($"\n(+{all.Count - (rows.Count - 1)} more)"));

            sb.Append(memory);
            return sb.ToString();
        }

        private static string[] KindRow(string kind, Counters c) => new[]
        {
            kind,
            Percent(c.Hits, c.Hits + c.Loads),
            Invariant($"{c.Hits + c.Loads}"),
            Invariant($"{c.Loads}"),
            c.Loads > 0 ? Invariant($"{c.LoadMs / c.Loads}ms") : "-",
            c.Loads > 0 ? Invariant($"{c.MaxLoadMs}ms") : "-",
            c.Loads > 0 ? Invariant($"{(double)c.Results / c.Loads:0.#}") : "-",
            c.Expired > 0 ? Invariant($"{c.Expired}") : "-",
            c.Invalidated > 0 ? Invariant($"{c.Invalidated}") : "-",
            c.Invalidated > 0 ? Age(c.InvalidatedAgeMs / c.Invalidated) : "-",
        };

        public static (long Entries, long Results) GetLive(string kind)
            => _live.TryGetValue(kind, out var live) ? (Interlocked.Read(ref live.Entries), Interlocked.Read(ref live.Results)) : (0, 0);

        private static string Memory()
        {
            var live = _live.Select(x => (Kind: x.Key, Entries: Interlocked.Read(ref x.Value.Entries), Results: Interlocked.Read(ref x.Value.Results)))
                .Where(x => x.Entries > 0).ToList();

            var sb = new StringBuilder();
            sb.Append(Invariant($"\nIn memory: {QueryCache.EntryCount} entries, {live.Sum(x => x.Results)} rows, {QueryCache.TagCount} tags, "))
              .Append(Invariant($"size {QueryCache.CurrentSize}/{QueryCache.SizeLimit}"));

            var biggest = live.OrderByDescending(x => x.Results).Take(MaxKindsInReport)
                .Select(x => Invariant($"{x.Kind} {x.Results} rows ({x.Entries} {(x.Entries == 1 ? "entry" : "entries")})"));
            if (live.Count > 0)
                sb.Append($"\nLargest: {string.Join(", ", biggest)}");

            return sb.ToString();
        }

        private static string Percent(long part, long total) => total == 0 ? "-" : Invariant($"{100.0 * part / total:0}%");

        private static string Age(long ms) => ms < 60_000 ? Invariant($"{ms / 1000.0:0}s") : Invariant($"{ms / 60_000.0:0.0}min");
    }
}
