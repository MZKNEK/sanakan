#pragma warning disable 1591

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Caching.Memory;

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
                if (report != null) logger.Log(report);
            }, null, interval, interval);
        }

        public static string Flush()
        {
            var stats = Interlocked.Exchange(ref _hour, new ConcurrentDictionary<string, Counters>());

            var now = DateTime.UtcNow;
            var minutes = (now - _lastReport).TotalMinutes;
            _lastReport = now;

            return Format($"Cache ({minutes:0}min)", stats);
        }

        public static string FlushDaily()
            => Format("Cache", Interlocked.Exchange(ref _day, new ConcurrentDictionary<string, Counters>())) ?? "Cache: brak odczytów";

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

        private static string Format(string title, ConcurrentDictionary<string, Counters> stats)
        {
            if (stats.IsEmpty) return null;

            var all = stats.ToList();
            var hits = all.Sum(x => x.Value.Hits);
            var loads = all.Sum(x => x.Value.Loads);
            var loadMs = all.Sum(x => x.Value.LoadMs);

            var sb = new StringBuilder();
            sb.Append($"{title}: trafienia {hits}/{hits + loads} ({Percent(hits, hits + loads)}), ")
              .Append($"ładowania {loads} ({loadMs / 1000.0:0.0}s), ")
              .Append($"unieważnione {all.Sum(x => x.Value.Invalidated)}, odrzucone {all.Sum(x => x.Value.Discarded)}");

            var memory = Memory();
            var shown = 0;
            var top = all.OrderByDescending(x => x.Value.LoadMs).ThenByDescending(x => x.Value.Loads + x.Value.Hits).Take(MaxKindsInReport);
            foreach (var (kind, c) in top)
            {
                var line = KindLine(kind, c);
                if (sb.Length + line.Length + memory.Length > MaxReportLength)
                    break;

                sb.Append(line);
                ++shown;
            }

            if (all.Count > shown)
                sb.Append($"\n(+{all.Count - shown} innych)");

            sb.Append(memory);
            return sb.ToString();
        }

        private static string KindLine(string kind, Counters c)
        {
            var sb = new StringBuilder($"\n- {kind}: trafienia {Percent(c.Hits, c.Hits + c.Loads)} z {c.Hits + c.Loads}");
            if (c.Loads > 0)
            {
                sb.Append($", ładowania {c.Loads} ({c.LoadMs / 1000.0:0.0}s, śr. {c.LoadMs / c.Loads}ms, maks. {c.MaxLoadMs}ms, ")
                  .Append($"śr. {c.Results / c.Loads} wyników)");
            }
            if (c.Invalidated > 0)
                sb.Append($", unieważnione {c.Invalidated} (śr. po {Age(c.InvalidatedAgeMs / c.Invalidated)})");
            if (c.Expired > 0)
                sb.Append($", wygasłe {c.Expired}");
            if (c.Discarded > 0)
                sb.Append($", odrzucone {c.Discarded}");
            if (c.OverLimit > 0)
                sb.Append($", ponad limit {c.OverLimit}");
            return sb.ToString();
        }

        public static (long Entries, long Results) GetLive(string kind)
            => _live.TryGetValue(kind, out var live) ? (Interlocked.Read(ref live.Entries), Interlocked.Read(ref live.Results)) : (0, 0);

        private static string Memory()
        {
            var live = _live.Select(x => (Kind: x.Key, Entries: Interlocked.Read(ref x.Value.Entries), Results: Interlocked.Read(ref x.Value.Results)))
                .Where(x => x.Entries > 0).ToList();

            long workingSet;
            using (var process = Process.GetCurrentProcess())
                workingSet = process.WorkingSet64;

            var sb = new StringBuilder();
            sb.Append($"\nW pamięci: wpisów {QueryCache.EntryCount}, wyników {live.Sum(x => x.Results)}, tagów {QueryCache.TagCount}, ")
              .Append($"rozmiar {QueryCache.CurrentSize}/{QueryCache.SizeLimit}; ")
              .Append($"sterta GC {GC.GetGCMemoryInfo().HeapSizeBytes / 1048576} MiB, proces {workingSet / 1048576} MiB");

            var biggest = live.OrderByDescending(x => x.Results).Take(MaxKindsInReport).Select(x => $"{x.Kind} {x.Entries} ({x.Results} wyn.)");
            if (live.Count > 0)
                sb.Append($"\nNajwięcej w pamięci: {string.Join(", ", biggest)}");

            return sb.ToString();
        }

        private static string Percent(long part, long total) => total == 0 ? "-" : $"{100.0 * part / total:0}%";

        private static string Age(long ms) => ms < 60_000 ? $"{ms / 1000.0:0}s" : $"{ms / 60_000.0:0.0}min";
    }
}
