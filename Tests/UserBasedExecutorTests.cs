using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sanakan.Services.Executor;
using Xunit;

namespace Artifacts
{
    public class UserBasedExecutorTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan AddTimeout = TimeSpan.FromMilliseconds(200);

        private class Probe
        {
            public string Name { get; init; }
            public TaskCompletionSource Started { get; } = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource Release { get; } = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            public Executable Exe { get; set; }
        }

        private readonly ConcurrentQueue<string> _events = new ConcurrentQueue<string>();

        private UserBasedExecutor CreateExecutor()
        {
            var executor = new UserBasedExecutor(new ListLogger());
            executor.Initialize(new EmptyServiceProvider());
            return executor;
        }

        private Probe Gated(string name, ulong owner, params ulong[] extraOwners)
        {
            var probe = new Probe { Name = name };
            probe.Exe = new Executable(name, new Func<Task>(async () =>
            {
                _events.Enqueue($"start:{name}");
                probe.Started.TrySetResult();
                await probe.Release.Task;
                _events.Enqueue($"end:{name}");
            }), owner);

            foreach (var o in extraOwners)
                probe.Exe.AddOwner(o);

            return probe;
        }

        private static async Task AssertNotStarted(Probe probe)
        {
            await Task.Delay(150);
            Assert.False(probe.Started.Task.IsCompleted, $"{probe.Name} should not have started yet");
        }

        [Fact]
        public async Task SameOwner_RunsSequentiallyInFifoOrder()
        {
            var executor = CreateExecutor();
            var probes = Enumerable.Range(1, 5).Select(i => Gated($"u1-{i}", 1)).ToList();

            foreach (var p in probes)
                Assert.True(await executor.TryAdd(p.Exe, AddTimeout));

            for (int i = 0; i < probes.Count; i++)
            {
                var p = probes[i];
                await p.Started.Task.WaitAsync(Timeout);
                await Task.Delay(50);
                Assert.All(probes.Skip(i + 1), x => Assert.False(x.Started.Task.IsCompleted));
                p.Release.SetResult();
                await p.Exe.WaitAsync().WaitAsync(Timeout);
            }

            var starts = _events.Where(x => x.StartsWith("start:")).ToList();
            Assert.Equal(probes.Select(x => $"start:{x.Name}"), starts);
        }

        [Fact]
        public async Task DifferentOwners_RunConcurrently()
        {
            var executor = CreateExecutor();
            var a = Gated("a", 1);
            var b = Gated("b", 2);

            await executor.TryAdd(a.Exe, AddTimeout);
            await executor.TryAdd(b.Exe, AddTimeout);

            await Task.WhenAll(a.Started.Task, b.Started.Task).WaitAsync(Timeout);

            a.Release.SetResult();
            b.Release.SetResult();
            await Task.WhenAll(a.Exe.WaitAsync(), b.Exe.WaitAsync()).WaitAsync(Timeout);
        }

        [Fact]
        public async Task BlockedTask_DoesNotBlockOtherUsersBehindIt()
        {
            var executor = CreateExecutor();
            var first = Gated("u1-first", 1);
            var second = Gated("u1-second", 1);
            var other = Gated("u3", 3);

            await executor.TryAdd(first.Exe, AddTimeout);
            await first.Started.Task.WaitAsync(Timeout);

            await executor.TryAdd(second.Exe, AddTimeout);
            await executor.TryAdd(other.Exe, AddTimeout);

            await other.Started.Task.WaitAsync(Timeout);
            await AssertNotStarted(second);

            first.Release.SetResult();
            await second.Started.Task.WaitAsync(Timeout);

            second.Release.SetResult();
            other.Release.SetResult();
        }

        [Fact]
        public async Task MultiOwnerTask_LocksEveryOwner()
        {
            var executor = CreateExecutor();
            var trade = Gated("trade-1-2", 1, 2);
            var secondOwnerCmd = Gated("u2", 2);

            await executor.TryAdd(trade.Exe, AddTimeout);
            await trade.Started.Task.WaitAsync(Timeout);

            await executor.TryAdd(secondOwnerCmd.Exe, AddTimeout);
            await AssertNotStarted(secondOwnerCmd);

            trade.Release.SetResult();
            await secondOwnerCmd.Started.Task.WaitAsync(Timeout);
            secondOwnerCmd.Release.SetResult();
        }

        [Fact]
        public async Task MultiOwnerTask_WaitsForAnyBusyOwner()
        {
            var executor = CreateExecutor();
            var u2 = Gated("u2", 2);
            var trade = Gated("trade-1-2", 1, 2);

            await executor.TryAdd(u2.Exe, AddTimeout);
            await u2.Started.Task.WaitAsync(Timeout);

            await executor.TryAdd(trade.Exe, AddTimeout);
            await AssertNotStarted(trade);

            u2.Release.SetResult();
            await trade.Started.Task.WaitAsync(Timeout);
            trade.Release.SetResult();
        }

        [Fact]
        public async Task GlobalTask_WaitsForRunningAndBlocksOthersWhileRunning()
        {
            var executor = CreateExecutor();
            var user = Gated("u1", 1);
            var global = Gated("global", 0);
            var later = Gated("u2", 2);

            await executor.TryAdd(user.Exe, AddTimeout);
            await user.Started.Task.WaitAsync(Timeout);

            await executor.TryAdd(global.Exe, AddTimeout);
            await AssertNotStarted(global);

            user.Release.SetResult();
            await global.Started.Task.WaitAsync(Timeout);

            await executor.TryAdd(later.Exe, AddTimeout);
            await AssertNotStarted(later);

            global.Release.SetResult();
            await later.Started.Task.WaitAsync(Timeout);
            later.Release.SetResult();
        }

        [Fact]
        public async Task GlobalTask_DoesNotStallOtherUsersWhileWaiting()
        {
            var executor = CreateExecutor();
            var user = Gated("u1", 1);
            var global = Gated("global", 0);
            var other = Gated("u2", 2);

            await executor.TryAdd(user.Exe, AddTimeout);
            await user.Started.Task.WaitAsync(Timeout);

            await executor.TryAdd(global.Exe, AddTimeout);
            await executor.TryAdd(other.Exe, AddTimeout);

            await other.Started.Task.WaitAsync(Timeout);

            user.Release.SetResult();
            other.Release.SetResult();
            await global.Started.Task.WaitAsync(Timeout);
            global.Release.SetResult();
        }

        [Fact]
        public async Task FullQueue_TryAddTimesOutWithoutBlocking_AndFreesSlotWhenTaskStarts()
        {
            var executor = CreateExecutor();
            var running = Gated("running", 1);
            await executor.TryAdd(running.Exe, AddTimeout);
            await running.Started.Task.WaitAsync(Timeout);

            var queued = new List<Probe>();
            for (int i = 0; i < 100; i++)
            {
                var p = Gated($"q{i}", 1);
                queued.Add(p);
                Assert.True(await executor.TryAdd(p.Exe, AddTimeout));
            }

            var overflow = Gated("overflow", 2);
            var add = executor.TryAdd(overflow.Exe, AddTimeout);
            Assert.False(await add.WaitAsync(Timeout));

            running.Release.SetResult();
            await queued[0].Started.Task.WaitAsync(Timeout);

            Assert.True(await executor.TryAdd(overflow.Exe, AddTimeout));
            await overflow.Started.Task.WaitAsync(Timeout);

            overflow.Release.SetResult();
            foreach (var p in queued) p.Release.TrySetResult();
        }

        [Fact]
        public async Task FailingTask_DoesNotBreakExecutorOrKeepOwnerLocked()
        {
            var logger = new ListLogger();
            var executor = new UserBasedExecutor(logger);
            executor.Initialize(new EmptyServiceProvider());

            var failing = new Executable("fail", new Func<Task>(async () =>
            {
                await Task.Yield();
                throw new InvalidOperationException("boom");
            }), 1);
            var next = Gated("next", 1);

            await executor.TryAdd(failing, AddTimeout);
            await executor.TryAdd(next.Exe, AddTimeout);

            await next.Started.Task.WaitAsync(Timeout);
            next.Release.SetResult();

            Assert.Contains(logger.Messages, x => x.Contains("fail") && x.Contains("boom"));
        }

        [Fact]
        public async Task TasksAddedBeforeInitialize_RunAfterInitialize()
        {
            var executor = new UserBasedExecutor(new ListLogger());
            var probe = Gated("early", 1);

            Assert.True(await executor.TryAdd(probe.Exe, AddTimeout));
            await AssertNotStarted(probe);

            executor.Initialize(new EmptyServiceProvider());
            await probe.Started.Task.WaitAsync(Timeout);
            probe.Release.SetResult();
        }

        [Fact]
        public async Task ManyUsersManyTasks_AllCompleteAndNoOwnerOverlaps()
        {
            var executor = CreateExecutor();
            var active = new ConcurrentDictionary<ulong, int>();
            var overlap = false;
            var tasks = new List<Executable>();

            for (int i = 0; i < 90; i++)
            {
                var owner = (ulong)(i % 9) + 1;
                var exe = new Executable($"t{i}", new Func<Task>(async () =>
                {
                    if (active.AddOrUpdate(owner, 1, (_, v) => v + 1) > 1) overlap = true;
                    await Task.Delay(5);
                    active.AddOrUpdate(owner, 0, (_, v) => v - 1);
                }), owner);
                tasks.Add(exe);
                Assert.True(await executor.TryAdd(exe, TimeSpan.FromSeconds(1)));
            }

            await Task.WhenAll(tasks.Select(x => x.WaitAsync())).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(overlap);
        }
    }
}
