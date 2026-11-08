#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime;
using System.Text;
using System.Threading;
using Discord.WebSocket;
using static System.FormattableString;

namespace Sanakan.Services
{
    public static class MemoryReport
    {
        private const long MiB = 1024 * 1024;

        private static long _hourPeak;
        private static long _dayPeak;
        private static Timer _sampler;
        private static Timer _reporter;
        private static Timer _store;

        // keeps the RAM history (working set in MiB) in the SystemData table
        public static void StartStoring(Config.IConfig config, Time.ISystemTime time, ILogger logger, TimeSpan interval)
        {
            _store = new Timer(async _ =>
            {
                try
                {
                    using var db = new Database.DatabaseContext(config);
                    db.SystemData.Add(new Database.Models.Analytics.SystemAnalytics
                    {
                        MeasureDate = time.Now(),
                        Value = ProcessBytes() / MiB,
                        Type = Database.Models.Analytics.SystemAnalyticsEventType.Ram,
                    });
                    await db.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    logger.LogError($"Report: memory sample: {ex}");
                }
            }, null, TimeSpan.FromMinutes(1), interval);
        }

        public static void Start(ILogger logger, Func<string> discord, TimeSpan interval)
        {
            _sampler = new Timer(_ => Sample(), null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
            _reporter = new Timer(_ =>
            {
                try
                {
                    logger.LogReport(Flush(discord));
                }
                catch (Exception ex)
                {
                    logger.LogError($"Report: memory: {ex.Message}");
                }
            }, null, interval, interval);
        }

        public static void Sample()
        {
            var current = ProcessBytes();
            RaiseTo(ref _hourPeak, current);
            RaiseTo(ref _dayPeak, current);
        }

        public static string Flush(Func<string> discord = null)
            => $"Memory (60 min):\n{Describe(Interlocked.Exchange(ref _hourPeak, 0), discord)}";

        public static string FlushDaily(Func<string> discord = null)
            => $"Memory:\n{Describe(Interlocked.Exchange(ref _dayPeak, 0), discord)}";

        public static string Describe(long peak, Func<string> discord)
        {
            var process = ProcessBytes();
            var gc = GC.GetGCMemoryInfo();
            var rows = new List<string[]>
            {
                new[] { "process", Invariant($"{Mb(process)} MiB (peak {Mb(Math.Max(peak, process))} MiB)") },
                new[] { "GC heap", Invariant($"{Mb(gc.HeapSizeBytes)} MiB, committed {Mb(gc.TotalCommittedBytes)} MiB, fragmented {Mb(gc.FragmentedBytes)} MiB") },
            };

            var gens = gc.GenerationInfo;
            if (gens.Length >= 5)
            {
                rows.Add(new[] { "generations", Invariant($"gen0 {Mb(gens[0].SizeAfterBytes)}, gen1 {Mb(gens[1].SizeAfterBytes)}, gen2 {Mb(gens[2].SizeAfterBytes)}, ")
                    + Invariant($"LOH {Mb(gens[3].SizeAfterBytes)}, POH {Mb(gens[4].SizeAfterBytes)} MiB") });
            }

            // working set and committed GC memory are measured differently, so this is only an estimate
            rows.Add(new[] { "outside GC", Invariant($"~{Mb(Math.Max(0, process - gc.TotalCommittedBytes))} MiB (incl. image pool, limit {ImageProcessing.MemoryPoolMegabytes} MiB)") });
            rows.Add(new[] { "GC", Invariant($"{(GCSettings.IsServerGC ? "server" : "workstation")}, collections gen0/1/2: {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}") });
            rows.Add(new[] { "query cache", Invariant($"{QueryCache.EntryCount} entries, size {QueryCache.CurrentSize}/{QueryCache.SizeLimit}") });

            var discordLine = TryDescribe(discord);
            if (!string.IsNullOrEmpty(discordLine))
                rows.Add(new[] { "Discord", discordLine });

            return string.Join("\n", TextTable.Render(rows, new[] { false, false }));
        }

        public static string DescribeDiscord(DiscordSocketClient client)
        {
            if (client == null) return null;

            var guilds = client.Guilds;
            var channels = guilds.SelectMany(x => x.TextChannels).ToList();
            var members = guilds.Sum(x => (long)x.DownloadedMemberCount);
            var messages = channels.Sum(x => (long)x.CachedMessages.Count);
            return Invariant($"{guilds.Count} guilds, {members} members cached, {channels.Count} text channels, {messages} messages cached");
        }

        private static string TryDescribe(Func<string> discord)
        {
            try
            {
                return discord?.Invoke();
            }
            catch (Exception ex)
            {
                return $"no data ({ex.Message})";
            }
        }

        private static long ProcessBytes()
        {
            using var process = Process.GetCurrentProcess();
            return process.WorkingSet64;
        }

        private static void RaiseTo(ref long target, long value)
        {
            var current = Interlocked.Read(ref target);
            while (value > current)
            {
                var seen = Interlocked.CompareExchange(ref target, value, current);
                if (seen == current) break;
                current = seen;
            }
        }

        private static long Mb(long bytes) => bytes / MiB;
    }
}
