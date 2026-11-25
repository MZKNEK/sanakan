#pragma warning disable 1591

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Discord;

namespace Sanakan.Services
{
    // what the bot tidies up on Discord before it stops: messages that would be left waiting for reactions forever
    public static class ShutdownCleanup
    {
        private static readonly ConcurrentDictionary<long, Func<Task>> _tasks = new();
        private static long _nextId;
        private static int _stopping;

        public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

        public static bool IsStopping => Volatile.Read(ref _stopping) == 1;

        // disposing it before the bot stops takes the task back
        public static IDisposable Register(Func<Task> cleanup)
        {
            var id = Interlocked.Increment(ref _nextId);
            _tasks[id] = cleanup;
            return new Registration(id);
        }

        public static IDisposable DeleteOnShutdown(IMessage message) => Register(() => message.DeleteAsync());

        // runs once, before the bot logs out of Discord; what does not finish in time is left as it is
        public static async Task RunAsync(ILogger logger, TimeSpan timeout)
        {
            if (Interlocked.Exchange(ref _stopping, 1) == 1)
                return;

            var tasks = _tasks.Values.ToList();
            _tasks.Clear();
            if (tasks.Count == 0)
                return;

            var all = Task.WhenAll(tasks.Select(async cleanup =>
            {
                try
                {
                    await cleanup().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger?.LogError($"Shutdown: cleanup: {ex.Message}");
                }
            }));

            try
            {
                await all.WaitAsync(timeout).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                logger?.LogError($"Shutdown: cleanup did not finish in {timeout.TotalSeconds:0} s");
            }
        }

        // for tests
        internal static void Reset()
        {
            _tasks.Clear();
            Volatile.Write(ref _stopping, 0);
        }

        private sealed class Registration : IDisposable
        {
            private readonly long _id;

            public Registration(long id) => _id = id;

            public void Dispose() => _tasks.TryRemove(_id, out _);
        }
    }
}
