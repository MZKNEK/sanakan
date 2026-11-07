#pragma warning disable 1591

using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime;
using System.Text;
using System.Threading;
using Discord.WebSocket;

namespace Sanakan.Services
{
    public static class MemoryReport
    {
        private const long MiB = 1024 * 1024;

        private static long _hourPeak;
        private static long _dayPeak;
        private static Timer _sampler;
        private static Timer _reporter;

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
                    logger.LogError($"Raport pamięci: {ex.Message}");
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
            => $"Pamięć (60min):\n{Describe(Interlocked.Exchange(ref _hourPeak, 0), discord)}";

        public static string FlushDaily(Func<string> discord = null)
            => $"Pamięć:\n{Describe(Interlocked.Exchange(ref _dayPeak, 0), discord)}";

        public static string Describe(long peak, Func<string> discord)
        {
            var process = ProcessBytes();
            var gc = GC.GetGCMemoryInfo();
            var sb = new StringBuilder();

            sb.Append($"- proces {Mb(process)} MiB (maks. {Mb(Math.Max(peak, process))} MiB)\n")
              .Append($"- sterta GC {Mb(gc.HeapSizeBytes)} MiB, zarezerwowane {Mb(gc.TotalCommittedBytes)} MiB, fragmentacja {Mb(gc.FragmentedBytes)} MiB\n");

            var gens = gc.GenerationInfo;
            if (gens.Length >= 5)
            {
                sb.Append($"- generacje: gen0 {Mb(gens[0].SizeAfterBytes)}, gen1 {Mb(gens[1].SizeAfterBytes)}, gen2 {Mb(gens[2].SizeAfterBytes)}, ")
                  .Append($"LOH {Mb(gens[3].SizeAfterBytes)}, POH {Mb(gens[4].SizeAfterBytes)} MiB\n");
            }

            // working set and committed GC memory are measured differently, so this is only an estimate
            sb.Append($"- poza GC ok. {Mb(Math.Max(0, process - gc.TotalCommittedBytes))} MiB (m.in. pula obrazów, limit {ImageProcessing.MemoryPoolMegabytes} MiB)\n")
              .Append($"- GC {(GCSettings.IsServerGC ? "serwerowy" : "stacja robocza")}, odśmiecania od startu gen0/1/2: ")
              .Append($"{GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}\n")
              .Append($"- cache zapytań: wpisów {QueryCache.EntryCount}, rozmiar {QueryCache.CurrentSize}/{QueryCache.SizeLimit}");

            var discordLine = TryDescribe(discord);
            if (!string.IsNullOrEmpty(discordLine))
                sb.Append($"\n- {discordLine}");

            return sb.ToString();
        }

        public static string DescribeDiscord(DiscordSocketClient client)
        {
            if (client == null) return null;

            var guilds = client.Guilds;
            var channels = guilds.SelectMany(x => x.TextChannels).ToList();
            var members = guilds.Sum(x => (long)x.DownloadedMemberCount);
            var messages = channels.Sum(x => (long)x.CachedMessages.Count);
            return $"Discord: serwerów {guilds.Count}, członków w pamięci {members}, kanałów tekstowych {channels.Count}, wiadomości w cache {messages}";
        }

        private static string TryDescribe(Func<string> discord)
        {
            try
            {
                return discord?.Invoke();
            }
            catch (Exception ex)
            {
                return $"Discord: brak danych ({ex.Message})";
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
