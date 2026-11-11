using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Moq;
using Sanakan;
using Sanakan.Services;
using Sanakan.Services.Commands;
using Sanakan.Services.Executor;
using Xunit;

namespace Artifacts
{
    public class LoggingThrowModule : ModuleBase<ICommandContext>
    {
        [Command("logsync")]
        public Task SyncAsync() => Task.CompletedTask;

        [Command("logasyncfail", RunMode = RunMode.Async)]
        public Task AsyncFailAsync() => throw new InvalidOperationException("async boom");
    }

    public class LoggingFormatTests
    {
        private static readonly DateTime Now = new DateTime(2026, 11, 8, 1, 54, 37);

        [Theory]
        [InlineData("API: app:Shinden POST /api/user/find -> 200 (15ms)", "[2026-11-08 01:54:37] API: app:Shinden POST /api/user/find -> 200 (15ms)")]
        [InlineData(LoggerExtensions.ErrorPrefix + "Cmd: u1 harem (3ms) - boom", "[2026-11-08 01:54:37] ERROR Cmd: u1 harem (3ms) - boom")]
        [InlineData(LoggerExtensions.ReportPrefix + "DB (60 min): 5 queries", "[2026-11-08 01:54:37] REPORT DB (60 min): 5 queries")]
        [InlineData(LoggerExtensions.RoutinePrefix + "Cmd: u1 lvlbadge (412ms)", "[2026-11-08 01:54:37] Cmd: u1 lvlbadge (412ms)")]
        public void Console_HasDateAndLevel(string message, string expected)
        {
            Assert.Equal(expected, ConsoleLogger.Format(message, Now));
        }

        [Fact]
        public void CommandLine_DescribesSuccessAndFailure()
        {
            Assert.Equal((false, "Cmd: u303 lvlbadge (412ms)"), CommandLog.Describe("lvlbadge", 303, 412, ExecuteResult.FromSuccess()));

            var failed = CommandLog.Describe("harem", 303, 38, ExecuteResult.FromError(new InvalidOperationException("boom")));
            Assert.True(failed.IsError);
            Assert.StartsWith("Cmd: u303 harem (38ms) - System.InvalidOperationException: boom", failed.Text);

            Assert.Equal((true, "Cmd: u303 x - Unsuccessful: nope"), CommandLog.Describe("x", 303, -1, ExecuteResult.FromError(CommandError.Unsuccessful, "nope")));
        }

        private static async Task<ListLogger> RunAsync(string name)
        {
            var logger = new ListLogger();
            var service = new CommandService();
            await service.AddModuleAsync<LoggingThrowModule>(null);
            var done = new TaskCompletionSource<bool>();
            service.CommandExecuted += (cmd, ctx, result) =>
            {
                CommandLog.Finished(logger, cmd.Value.Name, ctx, result);
                done.TrySetResult(true);
                return Task.CompletedTask;
            };

            var user = new Mock<IUser>();
            user.SetupGet(x => x.Id).Returns(303UL);
            var context = new Mock<ICommandContext>();
            context.SetupGet(x => x.User).Returns(user.Object);

            var search = service.Search(name);
            var match = search.Commands[0];
            var parse = await match.ParseAsync(context.Object, search, null, null);
            CommandLog.Started(context.Object);
            await match.ExecuteAsync(context.Object, parse, null);
            await Task.WhenAny(done.Task, Task.Delay(5000));
            return logger;
        }

        [Fact]
        public async Task SuccessfulCommand_IsOneRoutineLine()
        {
            var logger = await RunAsync("logsync");

            var line = Assert.Single(logger.Messages);
            Assert.Matches(@"^\[ROUTINE\] Cmd: u303 logsync \(\d+ms\)$", line);
        }

        [Fact]
        public async Task AsyncCommandException_IsLoggedWithStackTrace()
        {
            var logger = await RunAsync("logasyncfail");

            var line = Assert.Single(logger.Messages);
            Assert.StartsWith(LoggerExtensions.ErrorPrefix + "Cmd: u303 logasyncfail (", line);
            Assert.Contains("InvalidOperationException: async boom", line);
            Assert.Contains("LoggingThrowModule", line);
        }

        [Fact]
        public async Task Executor_LogsTasksAsOneRoutineLine()
        {
            var logger = new ListLogger();
            var executor = new UserBasedExecutor(logger);
            executor.Initialize(new EmptyServiceProvider());
            var ran = new TaskCompletionSource<bool>();

            await executor.TryAdd(new Executable("api-test u1", () => { ran.TrySetResult(true); return Task.CompletedTask; }, 1), TimeSpan.FromSeconds(1));
            await Task.WhenAny(ran.Task, Task.Delay(5000));
            for (int i = 0; i < 100 && logger.Messages.IsEmpty; i++)
                await Task.Delay(20);

            var line = Assert.Single(logger.Messages);
            Assert.Matches(@"^\[ROUTINE\] Exec: api-test u1 \(\d+ms\)$", line);
        }

        [Fact]
        public void TextTable_AlignsColumns()
        {
            var lines = TextTable.Render(new[]
            {
                new[] { "query", "hits" },
                new[] { "user-full", "5" },
                new[] { "guild", "1908" },
            }, new[] { false, true });

            Assert.Equal(new[] { "query      hits", "user-full     5", "guild      1908" }, lines);
        }

        [Fact]
        public void Reports_UseDotsEvenWithPolishCulture()
        {
            var old = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = new CultureInfo("pl-PL");
            try
            {
                using var traffic = new Sanakan.Api.ApiTraffic(_ => { }, TimeSpan.FromHours(1));
                for (int i = 0; i < 3; i++)
                    traffic.Add(new Microsoft.AspNetCore.Http.DefaultHttpContext { Request = { Method = "GET" } });

                var report = traffic.Flush();
                Assert.Matches(@"^API traffic \(\d+ min\): 3 requests \(\d+\.\d/s\), 0 over 2 s\n", report);
                Assert.DoesNotMatch(@"\d,\d", report);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = old;
            }
        }
    }
}
