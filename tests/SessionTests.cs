using System;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Moq;
using Sanakan.Services.Session;
using Xunit;

namespace Artifacts
{
    public class SessionTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

        private static IUser User(ulong id)
        {
            var mock = new Mock<IUser>();
            mock.SetupGet(x => x.Id).Returns(id);
            return mock.Object;
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Executable_ReturnsResultOfAsyncHandler(bool value)
        {
            var session = new Session(User(1))
            {
                OnExecute = async (ctx, s) =>
                {
                    await Task.Delay(20);
                    return value;
                }
            };

            var exe = session.GetExecutable(null);
            Assert.Equal(value, await exe.ExecuteAsync(new EmptyServiceProvider()).WaitAsync(Timeout));
        }

        [Fact]
        public async Task ManyWaitingSessions_DoNotStarveThreadPool()
        {
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var count = Environment.ProcessorCount * 8;

            var runs = Enumerable.Range(0, count).Select(i =>
            {
                var session = new Session(User((ulong)i + 1)) { OnExecute = (ctx, s) => gate.Task };
                return session.GetExecutable(null).ExecuteAsync(new EmptyServiceProvider());
            }).ToList();

            // przy blokującym GetResult() każda sesja trzyma wątek z puli i nowa praca czeka na wstrzyknięcie wątków
            var watch = System.Diagnostics.Stopwatch.StartNew();
            await Task.Run(() => 1).WaitAsync(Timeout);
            Assert.True(watch.ElapsedMilliseconds < 1000, $"thread pool response took {watch.ElapsedMilliseconds}ms");

            gate.SetResult(true);
            Assert.All(await Task.WhenAll(runs).WaitAsync(Timeout), Assert.True);
        }

        [Fact]
        public async Task Executable_HandlerThrows_ReturnsFalseAndLogs()
        {
            var logger = new ListLogger();
            var session = new Session(User(1))
            {
                Id = "test-session",
                OnExecute = async (ctx, s) =>
                {
                    await Task.Yield();
                    throw new InvalidOperationException("handler boom");
                }
            };
            session.WithLogger(logger);

            Assert.False(await session.GetExecutable(null).ExecuteAsync(new EmptyServiceProvider()).WaitAsync(Timeout));
            Assert.Contains(logger.Messages, x => x.Contains("test-session") && x.Contains("handler boom"));
        }

        [Fact]
        public async Task Executable_NoHandler_ReturnsTrue()
        {
            var session = new Session(User(1));
            Assert.True(await session.GetExecutable(null).ExecuteAsync(new EmptyServiceProvider()).WaitAsync(Timeout));
        }

        [Fact]
        public async Task Executable_SyncSessionEndingWithTrue_CallsDestroyer()
        {
            var destroyed = new TaskCompletionSource();
            var session = new Session(User(1))
            {
                RunMode = RunMode.Sync,
                OnExecute = (ctx, s) => Task.FromResult(true),
            };
            session.SetDestroyer(s =>
            {
                destroyed.TrySetResult();
                return Task.CompletedTask;
            });

            await session.GetExecutable(null).ExecuteAsync(new EmptyServiceProvider());
            await destroyed.Task.WaitAsync(Timeout);
        }

        [Fact]
        public void Executable_HasAllParticipantsAsOwners()
        {
            var session = new Session(User(1));
            session.AddParticipant(User(2));

            var owners = session.GetExecutable(null).GetOwners().Distinct().OrderBy(x => x);
            Assert.Equal(new ulong[] { 1, 2 }, owners);
        }
    }
}
