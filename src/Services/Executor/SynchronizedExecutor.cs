#pragma warning disable 1591

using System;
using AsyncKeyedLock;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Shinden.Logger;

namespace Sanakan.Services.Executor
{
    public class SynchronizedExecutor : IExecutor
    {
        private const int QueueLength = 100;

        private IServiceProvider _provider;
        private string _currentTaskName;
        private ILogger _logger;

        private AsyncNonKeyedLocker _semaphore = new AsyncNonKeyedLocker(1);
        private BlockingCollection<IExecutable> _queue = new BlockingCollection<IExecutable>(QueueLength);
        private BlockingCollection<IExecutable> _hiQueue = new BlockingCollection<IExecutable>(QueueLength);

        private Timer _timer;
        private CancellationTokenSource _cts { get; set; }

        public SynchronizedExecutor(ILogger logger)
        {
            _logger = logger;
            _currentTaskName = "";
            _cts = new CancellationTokenSource();

            _timer = new Timer(async _ =>
            {
                await RunWorker();
            },
            null,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(1));
        }

        public void Initialize(IServiceProvider provider)
        {
            _provider = provider;
        }

        public string WhatIsRunning()
        {
            return _currentTaskName;
        }

        public Task<bool> TryAdd(IExecutable task, TimeSpan timeout)
        {
            if (AddToQueue(task, timeout))
            {
                return Task.FromResult(true);
            }
            _logger.Log($"Executor: adding new task, on pool: {_queue.Count} /hi: {_hiQueue.Count}");
            return Task.FromResult(false);
        }

        public async Task RunWorker()
        {
            if (_queue.Count < 1 && _hiQueue.Count < 1)
                return;

            using (var releaser = await _semaphore.LockAsync(0).ConfigureAwait(false))
            {
                if (releaser.EnteredSemaphore)
                {
                    _ = Task.Run(async () => await ProcessCommandsAsync()).ContinueWith(_ =>
                    {
                        _cts.Cancel();
                        _cts = new CancellationTokenSource();
                    }).ConfigureAwait(false);

                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(90), _cts.Token);
                    }
                    catch (Exception) { }
                }
            }
        }

        private bool AddToQueue(IExecutable task, TimeSpan timeout)
        {
            if (task.GetPriority() == Priority.High)
            {
                return _hiQueue.TryAdd(task, timeout);
            }
            return _queue.TryAdd(task, timeout);
        }

        private BlockingCollection<IExecutable> SelectQueue()
        {
            if (_hiQueue.Count > 0)
                return _hiQueue;

            return _queue;
        }

        private async Task ProcessCommandsAsync()
        {
            if (SelectQueue().TryTake(out var cmd, 100))
            {
                var taskName = cmd.GetName();
                 _currentTaskName = taskName;

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
                finally
                {
                    _currentTaskName = "";
                }
            }
        }
    }
}
