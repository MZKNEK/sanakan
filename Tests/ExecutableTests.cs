using System;
using System.Threading.Tasks;
using Sanakan.Services.Executor;
using Xunit;

namespace Artifacts
{
    public class ExecutableTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

        [Fact]
        public async Task WaitAsync_CompletesOnlyAfterTaskFinished()
        {
            var gate = new TaskCompletionSource();
            var finished = false;
            var exe = new Executable("t", new Func<Task>(async () =>
            {
                await gate.Task;
                finished = true;
            }));

            var wait = exe.WaitAsync();
            var run = exe.ExecuteAsync(new EmptyServiceProvider());

            await Task.Delay(100);
            Assert.False(wait.IsCompleted);

            gate.SetResult();
            await wait.WaitAsync(Timeout);
            Assert.True(finished);
            Assert.True(await run.WaitAsync(Timeout));
        }

        [Fact]
        public async Task WaitAsync_BeforeStart_DoesNotSpinAndCompletesAfterExecute()
        {
            var exe = new Executable("t", new Func<Task>(() => Task.CompletedTask));

            var wait = exe.WaitAsync();
            await Task.Delay(100);
            Assert.False(wait.IsCompleted);

            await exe.ExecuteAsync(new EmptyServiceProvider());
            await wait.WaitAsync(Timeout);
        }

        [Fact]
        public async Task SyncWait_ReturnsAfterExecuteFromOtherThread()
        {
            var exe = new Executable("t", new Func<Task>(() => Task.Delay(50)));
            _ = Task.Run(async () =>
            {
                await Task.Delay(100);
                await exe.ExecuteAsync(new EmptyServiceProvider());
            });

            await Task.Run(() => exe.Wait()).WaitAsync(Timeout);
        }

        [Fact]
        public async Task WaitAsync_SynchronousThrow_PropagatesInsteadOfHanging()
        {
            var exe = new Executable("t", new Func<Task>(() => throw new InvalidOperationException("boom")));

            var wait = exe.WaitAsync();
            await Assert.ThrowsAsync<Exception>(() => exe.ExecuteAsync(new EmptyServiceProvider()));

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => wait.WaitAsync(Timeout));
            Assert.Equal("boom", ex.Message);
        }

        [Fact]
        public async Task WaitAsync_AsyncThrow_Propagates()
        {
            var exe = new Executable("t", new Func<Task>(async () =>
            {
                await Task.Yield();
                throw new ArgumentException("async boom");
            }));

            var wait = exe.WaitAsync();
            await Assert.ThrowsAsync<Exception>(() => exe.ExecuteAsync(new EmptyServiceProvider()));
            await Assert.ThrowsAsync<ArgumentException>(() => wait.WaitAsync(Timeout));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task ExecuteAsync_ReturnsBoolFromTaskOfBool(bool value)
        {
            var exe = new Executable("t", new Func<Task<bool>>(async () =>
            {
                await Task.Yield();
                return value;
            }));

            Assert.Equal(value, await exe.ExecuteAsync(new EmptyServiceProvider()));
        }

        [Fact]
        public async Task TaskConstructor_StartsOnExecuteAndWaitCompletes()
        {
            var exe = new Executable("t", new Task<bool>(() => false));

            var wait = exe.WaitAsync();
            Assert.False(await exe.ExecuteAsync(new EmptyServiceProvider()));
            await wait.WaitAsync(Timeout);
        }
    }
}
