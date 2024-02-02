#pragma warning disable 1591

using System;
using AsyncKeyedLock;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Shinden.Logger;
using System.Linq;

namespace Sanakan.Services.Executor
{
    public class UserBasedExecutor : IExecutor
    {
        private IServiceProvider _provider;
        private ILogger _logger;
        private Timer _timer;

        private AsyncKeyedLocker<ulong> _semaphore = new AsyncKeyedLocker<ulong>(x =>
        {
            x.PoolSize = 200;
            x.PoolInitialFill = 10;
            x.MaxCount = 1;
        });

        private BlockingCollection<IExecutable> _queue = new BlockingCollection<IExecutable>(100);

        public UserBasedExecutor(ILogger logger)
        {
            _logger = logger;
            _timer = new Timer(_ =>
            {
                _ = Task.Run(async () => await RunWorker());
            },
            null,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(500));
        }

        public void Initialize(IServiceProvider provider)
        {
            _provider = provider;
        }

        public string WhatIsRunning()
        {
            return "---";
        }

        public async Task<bool> TryAdd(IExecutable task, TimeSpan timeout)
        {
            if (_queue.TryAdd(task, timeout))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10));
                _ = Task.Run(async () => await RunWorker());
                return true;
            }
            return false;
        }

        public async Task RunWorker()
        {
            if (_queue.TryTake(out var task))
            {
                if (!await ProcessCommandsAsync(task).ConfigureAwait(false))
                {
                    _queue.Add(task);
                }
            }
        }

        private bool IsInUse(AsyncKeyedLockReleaser<ulong> releaser)
        {
            return releaser.ReferenceCount > 0;
        }

        private async Task<bool> ProcessCommandsAsync(IExecutable cmd)
        {
            var taskName = cmd.GetName();
            var owners = cmd.GetOwners();
            var userId = owners.First();

            if (_semaphore.IsInUse(0) || owners.Any(x => _semaphore.IsInUse(x))
                || (userId == 0 && _semaphore.Index.Any(x => IsInUse(x.Value))))
            {
                return false;
            }

            using (var releaser = await _semaphore.LockAsync(userId, 0).ConfigureAwait(false))
            {
                if (releaser.EnteredSemaphore)
                {
                    try
                    {
                        _logger.Log($"Executor: running {taskName}");
                        var watch = Stopwatch.StartNew();
                        await cmd.ExecuteAsync(_provider).ConfigureAwait(false);
                        _logger.Log($"Executor: completed {taskName} in {watch.ElapsedMilliseconds}ms");
                    }
                    catch (Exception ex)
                    {
                        _logger.Log($"Executor: {taskName} - {ex}");
                    }
                }
                else
                {
                    return false;
                }
            }
            return true;
        }
    }
}
