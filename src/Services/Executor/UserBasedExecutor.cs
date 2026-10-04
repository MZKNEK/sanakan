#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Shinden.Logger;
using System.Linq;

namespace Sanakan.Services.Executor
{
    public class UserBasedExecutor : IExecutor
    {
        private const int QueueLength = 100;
        private static readonly TimeSpan DefaultGlobalMaxWait = TimeSpan.FromSeconds(5);

        private class QueuedTask
        {
            public IExecutable Task { get; init; }
            public Stopwatch Waiting { get; init; }
        }

        private IServiceProvider _provider;
        private ILogger _logger;
        private Timer _timer;
        private readonly TimeSpan _globalMaxWait;

        private readonly object _lock = new object();
        private readonly List<QueuedTask> _queue = new List<QueuedTask>();
        private readonly HashSet<ulong> _busyOwners = new HashSet<ulong>();
        private readonly SemaphoreSlim _freeSlots = new SemaphoreSlim(QueueLength, QueueLength);
        private int _running = 0;
        private bool _globalRunning = false;

        public UserBasedExecutor(ILogger logger) : this(logger, DefaultGlobalMaxWait) {}

        public UserBasedExecutor(ILogger logger, TimeSpan globalMaxWait)
        {
            _logger = logger;
            _globalMaxWait = globalMaxWait;
            _timer = new Timer(_ => Pump(), null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1));
        }

        public void Initialize(IServiceProvider provider)
        {
            _provider = provider;
            Pump();
        }

        public string WhatIsRunning()
        {
            return "---";
        }

        public async Task<bool> TryAdd(IExecutable task, TimeSpan timeout)
        {
            if (!await _freeSlots.WaitAsync(timeout).ConfigureAwait(false))
                return false;

            lock (_lock)
            {
                _queue.Add(new QueuedTask { Task = task, Waiting = Stopwatch.StartNew() });
            }

            Pump();
            return true;
        }

        public Task RunWorker()
        {
            Pump();
            return Task.CompletedTask;
        }

        private static int Order(Priority priority) => priority switch
        {
            Priority.High => 0,
            Priority.Low => 2,
            _ => 1,
        };

        private void Pump()
        {
            if (_provider == null)
                return;

            var toStart = new List<IExecutable>();
            lock (_lock)
            {
                var waitingOwners = new HashSet<ulong>();
                var waitingForGlobal = false;

                foreach (var queued in _queue.OrderBy(x => Order(x.Task.GetPriority())).ToList())
                {
                    if (_globalRunning || waitingForGlobal)
                        break;

                    var task = queued.Task;
                    var owners = task.GetOwners().Distinct().ToList();
                    if (owners.First() == 0)
                    {
                        if (_running > 0 || toStart.Count > 0)
                        {
                            waitingForGlobal = queued.Waiting.Elapsed >= _globalMaxWait;
                            continue;
                        }
                        _globalRunning = true;
                    }
                    else if (owners.Any(x => _busyOwners.Contains(x) || waitingOwners.Contains(x)))
                    {
                        foreach (var owner in owners)
                            waitingOwners.Add(owner);

                        continue;
                    }

                    foreach (var owner in owners)
                        _busyOwners.Add(owner);

                    ++_running;
                    _queue.Remove(queued);
                    toStart.Add(task);
                }
            }

            foreach (var task in toStart)
            {
                _freeSlots.Release();
                _ = Task.Run(() => RunAsync(task));
            }
        }

        private async Task RunAsync(IExecutable cmd)
        {
            var taskName = cmd.GetName();
            try
            {
                _logger.Log($"Executor: running {taskName}");
                var watch = Stopwatch.StartNew();
                await cmd.ExecuteAsync(_provider).ConfigureAwait(false);
                _logger.Log($"Executor: completed {taskName} in {watch.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Executor: {taskName} - {ex}");
            }
            finally
            {
                lock (_lock)
                {
                    foreach (var owner in cmd.GetOwners())
                        _busyOwners.Remove(owner);

                    --_running;
                    _globalRunning = false;
                }
                Pump();
            }
        }
    }
}
