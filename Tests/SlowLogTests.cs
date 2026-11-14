using System;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Moq;
using Sanakan;
using Sanakan.Api;
using Sanakan.Database;
using Sanakan.Services;
using Xunit;

namespace Artifacts
{
    // slow database queries go to the log channel, very slow ones ping, at most once per key and interval;
    // slow API requests are only counted in the hourly traffic report
    public class SlowLogTests
    {
        private DateTime _now = new DateTime(2026, 1, 1);

        private SlowLog NewLog() => new SlowLog(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(10), () => _now);

        private static LogKind Kind(string message) => LoggerExtensions.Parse(message).Kind;

        [Fact]
        public void FastOperation_IsNotLogged()
        {
            var logger = new ListLogger();

            NewLog().Record(logger, "GET a", TimeSpan.FromMilliseconds(1999), () => "x");

            Assert.Empty(logger.Messages);
        }

        [Fact]
        public void SlowOperation_GoesToTheChannelWithoutPing()
        {
            var logger = new ListLogger();

            NewLog().Record(logger, "GET a", TimeSpan.FromSeconds(3), () => "slow");

            Assert.Equal(LogKind.Info, Kind(Assert.Single(logger.Messages)));
        }

        [Fact]
        public void VerySlowOperation_PingsOncePerKeyAndInterval()
        {
            var logger = new ListLogger();
            var log = NewLog();

            log.Record(logger, "GET a", TimeSpan.FromSeconds(12), () => "1");
            log.Record(logger, "GET a", TimeSpan.FromSeconds(12), () => "2");
            log.Record(logger, "GET b", TimeSpan.FromSeconds(12), () => "3");
            _now = _now.AddMinutes(10);
            log.Record(logger, "GET a", TimeSpan.FromSeconds(12), () => "4");

            Assert.Equal(new[] { LogKind.Error, LogKind.Info, LogKind.Error, LogKind.Error }, logger.Messages.Select(Kind));
        }

        [Fact]
        public void ApiTraffic_ShowsTimesPerRoute()
        {
            using var traffic = new ApiTraffic(_ => { }, TimeSpan.FromHours(1));
            foreach (var ms in new long[] { 10, 20, 30, 40, 2500 })
                traffic.Add(new DefaultHttpContext { Request = { Method = "POST" } }, ms);

            var lines = traffic.Flush().Split('\n');

            Assert.Matches(@"5 requests \(\d+\.\d/s\), 1 over 2 s$", lines[0]);
            Assert.Matches(@"^reqs +p50 +p95 +max +slow +method +route$", lines[1]);
            Assert.Matches(@"^ *5 +30ms +2500ms +2500ms +1 +POST +\(no route\)$", lines[2]);
        }

        [Fact]
        public void Sql_IsShortenedToOneLine()
        {
            var sql = "SELECT a,\n   b\r\n FROM `Cards`   WHERE x = @p0" + new string('x', 400);

            var text = DbActivity.DescribeSql(sql);

            Assert.StartsWith("SELECT a, b FROM `Cards` WHERE x = @p0", text);
            Assert.Equal(301, text.Length);
        }

        [Fact]
        public void SlowQuery_IsCountedInTheHourlyReport()
        {
            var command = new Mock<DbCommand>();
            command.SetupGet(x => x.CommandText).Returns("SELECT * FROM `Cards` WHERE `GameDeckId` = @p0");
            DbActivity.Flush();

            typeof(DbActivity).GetMethod("CheckSlow", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { command.Object, TimeSpan.FromMilliseconds(1500) });
            // the report needs any activity to be printed
            typeof(DbActivity).GetMethod("CountQuery", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { TimeSpan.FromMilliseconds(1500) });

            Assert.EndsWith(", 1 over 1 s, slowest 1500ms: SELECT * FROM `Cards` WHERE `GameDeckId` = @p0", DbActivity.Flush());
        }
    }
}
