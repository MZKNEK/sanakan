using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Sanakan;
using Sanakan.Config.Model;
using Sanakan.Database.Models.Management;
using Sanakan.Services;
using Xunit;

namespace Artifacts
{
    public class ReportHighlightTests
    {
        private static readonly DateTime Now = new DateTime(2026, 1, 1, 12, 0, 0);
        private const string ReportColor = "\u001b[1;36m";
        private const string ErrorColor = "\u001b[0;31m";

        [Fact]
        public void Report_IsColoredAndLabeled()
        {
            var entries = DiscordChannelLogger.Format(LoggerExtensions.ReportPrefix + "DB (60 min): 5 queries\nline 2", false, Now);

            var entry = Assert.Single(entries);
            Assert.Equal($"{ReportColor}[12:00:00] 📊 REPORT DB (60 min): 5 queries\n{ReportColor}line 2", entry);
        }

        [Fact]
        public void ErrorAndPlainLogs_KeepTheirLook()
        {
            Assert.Equal($"{ErrorColor}[12:00:00] ERROR boom", Assert.Single(DiscordChannelLogger.Format(LoggerExtensions.ErrorPrefix + "boom", false, Now)));
            Assert.Equal("\u001b[0m[12:00:00] info", Assert.Single(DiscordChannelLogger.Format("info", false, Now)));
        }

        [Fact]
        public void LongReport_IsSplitByLinesInsteadOfCut()
        {
            var lines = Enumerable.Range(0, 60).Select(i => $"wiersz {i:00} " + new string('x', 60)).ToList();
            var entries = DiscordChannelLogger.Format(LoggerExtensions.ReportPrefix + string.Join("\n", lines), false, Now);

            Assert.True(entries.Count > 1);
            var messages = DiscordChannelLogger.Pack(new Queue<string>(entries), null, 10);
            Assert.All(messages, x => Assert.True(x.Length <= DiscordChannelLogger.MaxMessageLength));
            Assert.DoesNotContain(messages, x => x.Contains('…'));
            var text = string.Join("\n", messages);
            Assert.All(lines, x => Assert.Contains(x, text));
        }

        [Fact]
        public void Pack_KeepsReportsApartFromOrdinaryLogs()
        {
            var report1 = DiscordChannelLogger.Format(LoggerExtensions.ReportPrefix + "DB", false, Now).Single();
            var report2 = DiscordChannelLogger.Format(LoggerExtensions.ReportPrefix + "Cache", false, Now).Single();
            var pending = new Queue<string>(new[] { "a", report1, report2, "b" });

            var messages = DiscordChannelLogger.Pack(pending, null, 5);

            Assert.Equal(3, messages.Count);
            Assert.Equal("```ansi\na\n```", messages[0]);
            Assert.Equal($"```ansi\n{report1}\n{report2}\n```", messages[1]);
            Assert.Equal("```ansi\nb\n```", messages[2]);
        }

        [Fact]
        public void ErrorPing_EveryErrorBatchIsPingedWithItsCount()
        {
            var limiter = new ErrorPingLimiter();
            Assert.False(limiter.TryTake(Now, out _, out _));

            limiter.Record(Now);
            limiter.Record(Now);
            Assert.True(limiter.TryTake(Now, out var errors, out var flood));
            Assert.Equal(2, errors);
            Assert.False(flood);

            Assert.False(limiter.TryTake(Now.AddSeconds(5), out _, out _));
            limiter.Record(Now.AddMinutes(1));
            Assert.True(limiter.TryTake(Now.AddMinutes(1), out errors, out _));
            Assert.Equal(1, errors);
        }

        [Fact]
        public void ErrorPing_DuringFloodPingsAtMostEvery30MinutesAndCountsTheRest()
        {
            var limiter = new ErrorPingLimiter();
            for (int i = 0; i <= ErrorPingLimiter.FloodThreshold; i++)
                limiter.Record(Now);

            Assert.True(limiter.TryTake(Now, out var errors, out var flood));
            Assert.Equal(ErrorPingLimiter.FloodThreshold + 1, errors);
            Assert.True(flood);

            for (int i = 1; i <= 25; i++)
            {
                var at = Now.AddMinutes(i);
                limiter.Record(at);
                Assert.False(limiter.TryTake(at, out _, out _));
            }

            // the flood is over (no errors in the last 10 minutes), the held back count goes out with no new error
            Assert.True(limiter.TryTake(Now.AddMinutes(36), out errors, out flood));
            Assert.Equal(25, errors);
            Assert.False(flood);
        }

        [Fact]
        public void ErrorPing_OngoingFloodPingsAgainAfter30Minutes()
        {
            var limiter = new ErrorPingLimiter();
            var pingedAt = new List<int>();
            for (int minute = 0; minute <= 40; minute++)
            {
                for (int i = 0; i < 3; i++)
                    limiter.Record(Now.AddMinutes(minute));

                if (limiter.TryTake(Now.AddMinutes(minute), out _, out _))
                    pingedAt.Add(minute);
            }

            // 3 errors a minute: pinged each minute until it counts as a flood, then once per 30 minutes
            Assert.Equal(new[] { 0, 1, 2, 32 }, pingedAt);
        }

        [Fact]
        public void ErrorPingText_MentionsRole()
        {
            Assert.Equal("<@&612125835721179146> ⚠️ Errors in logs: 3", DiscordChannelLogger.ErrorPingText(612125835721179146, 3, false));
            Assert.Contains("next ping in 30 min", DiscordChannelLogger.ErrorPingText(1, 40, true));
        }

        [Fact]
        public void Config_WithoutErrorRole_KeepsDefaultRole_AndZeroDisablesIt()
        {
            var old = Read<ConfigModel>("{\"LogChannel\":{\"GuildId\":5,\"ChannelId\":6}}");
            Assert.Equal(612125835721179146UL, old.LogChannel.ErrorRoleId);
            Assert.Equal(6UL, old.LogChannel.ChannelId);

            var off = Read<ConfigModel>("{\"LogChannel\":{\"GuildId\":5,\"ChannelId\":6,\"ErrorRoleId\":0}}");
            Assert.Equal(0UL, off.LogChannel.ErrorRoleId);
        }

        private static T Read<T>(string json) => new JsonSerializer().Deserialize<T>(new JsonTextReader(new StringReader(json)));

        [Theory]
        [InlineData("Automatyczny ban: raid - wykryto 3 kont", true)]
        [InlineData("Automatyczny mute: spam/flood", true)]
        [InlineData("łamanie regulaminu", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void AutomaticReason_IsRecognized(string reason, bool automatic)
        {
            Assert.Equal(automatic, Moderator.IsAutomaticReason(reason));
        }

        [Fact]
        public void AutoPenaltyLabel_NamesThePenalty()
        {
            Assert.Equal("🤖 Automatyczny ban", Moderator.AutoPenaltyLabel(PenaltyType.Ban));
            Assert.Equal("🤖 Automatyczny mute", Moderator.AutoPenaltyLabel(PenaltyType.Mute));
        }

        [Fact]
        public void MemoryReport_ShowsProcessGcAndCaches()
        {
            MemoryReport.Sample();
            var report = MemoryReport.Flush(() => "1 guilds");

            Assert.StartsWith("Memory (60 min):", report);
            foreach (var part in new[] { "process ", "peak ", "GC heap ", "generations ", "gen0 ", "outside GC ", "workstation", "query cache ", "Discord " })
                Assert.Contains(part, report);
            Assert.Matches(@"(?m)^Discord +1 guilds$", report);
        }

        [Fact]
        public void MemoryReport_SurvivesBrokenDiscordStats()
        {
            var report = MemoryReport.Flush(() => throw new InvalidOperationException("no client"));
            Assert.Matches(@"(?m)^Discord +no data \(no client\)$", report);
            Assert.Null(MemoryReport.DescribeDiscord(null));
        }
    }
}
