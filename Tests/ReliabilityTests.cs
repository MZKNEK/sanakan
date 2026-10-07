using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Discord.WebSocket;
using Sanakan.Services;
using Sanakan.Services.Executor;
using Sanakan.Services.Session;
using Xunit;

namespace Artifacts
{
    public class SessionDisposeTests
    {
        [Fact]
        public async Task DoubleDispose_RunsOnDisposeOnce()
        {
            int count = 0;
            var session = new Session(Users.Discord(1));
            session.OnDispose = () => { Interlocked.Increment(ref count); return Task.CompletedTask; };

            await session.DisposeAsync();
            await session.DisposeAsync();

            Assert.Equal(1, count);
            Assert.False(session.IsValid());
        }

        [Fact]
        public async Task ConcurrentDispose_RunsOnDisposeOnce()
        {
            int count = 0;
            var session = new Session(Users.Discord(1));
            session.OnDispose = async () => { Interlocked.Increment(ref count); await Task.Delay(20); };

            await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => session.DisposeAsync()));

            Assert.Equal(1, count);
            Assert.False(session.IsValid());
        }

        [Fact]
        public async Task DisposeAfterUse_KeepsDisposedState()
        {
            var session = new Session(Users.Discord(1));
            session.RunMode = Discord.Commands.RunMode.Async;
            session.MarkAsAdded();

            await session.DisposeAsync();

            Assert.False(session.IsValid());
        }
    }

    public class EmoteCounterSnapshotTests
    {
        [Fact]
        public void GetEmotesStats_ReturnsIndependentSnapshot()
        {
            var counter = new EmoteCounter(new DiscordSocketClient(), new FixedTime(), new ListLogger());

            var first = counter.GetEmotesStats();
            var second = counter.GetEmotesStats();

            Assert.NotSame(first, second);
            Assert.NotSame(first.Counter, second.Counter);
            Assert.Equal(first.Start, second.Start);
        }
    }

    public class ExecutableWaitTests
    {
        [Fact]
        public async Task WaitAsync_CompletedTask_ReturnsTrue()
        {
            var exe = new Executable("test", new Func<Task>(() => Task.CompletedTask));

            await exe.ExecuteAsync(new EmptyServiceProvider());

            Assert.True(await exe.WaitAsync(TimeSpan.FromSeconds(1)));
        }

        [Fact]
        public async Task WaitAsync_Timeout_ReturnsFalse()
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var exe = new Executable("test", new Func<Task>(async () => await gate.Task));

            var running = exe.ExecuteAsync(new EmptyServiceProvider());
            var completed = await exe.WaitAsync(TimeSpan.FromMilliseconds(50));

            Assert.False(completed);

            gate.SetResult();
            await running;
        }

        [Fact]
        public async Task ConstructorWithAlreadyStartedTask_DoesNotThrow()
        {
            var exe = new Executable("test", Task.CompletedTask);

            await exe.ExecuteAsync(new EmptyServiceProvider());

            Assert.True(await exe.WaitAsync(TimeSpan.FromSeconds(1)));
        }
    }

    public class UserBasedExecutorLifecycleTests
    {
        [Fact]
        public void Dispose_DoesNotThrow()
        {
            var executor = new UserBasedExecutor(new ListLogger());

            executor.Dispose();
        }
    }
}
