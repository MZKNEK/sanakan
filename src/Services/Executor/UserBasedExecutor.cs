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

        private IServiceProvider _provider;
        private ILogger _logger;
        private Timer _timer;

        private readonly object _lock = new object();
        private readonly List<IExecutable> _queue = new List<IExecutable>();
        private readonly HashSet<ulong> _busyOwners = new HashSet<ulong>();
        private readonly SemaphoreSlim _freeSlots = new SemaphoreSlim(QueueLength, QueueLength);
        private int _running = 0;
        private bool _globalRunning = false;

        public UserBasedExecutor(ILogger logger)
        {
            _logger = logger;
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
                _queue.Add(task);
            }

            Pump();
            return true;
        }

        public Task RunWorker()
        {
            Pump();
            return Task.CompletedTask;
        }

        private void Pump()
        {
            if (_provider == null)
                return;

            var toStart = new List<IExecutable>();
            lock (_lock)
            {
                for (int i = 0; i < _queue.Count && !_globalRunning; i++)
                {
                    var task = _queue[i];
                    var owners = task.GetOwners().Distinct().ToList();

                    if (owners.First() == 0)
                    {
                        // zadanie globalne wymaga, by nic innego nie działało
                        if (_running > 0) continue;
                        _globalRunning = true;
                    }
                    else if (owners.Any(x => _busyOwners.Contains(x)))
                    {
                        continue;
                    }

                    foreach (var owner in owners)
                        _busyOwners.Add(owner);

                    ++_running;
                    _queue.RemoveAt(i--);
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
                _logger.Log($"Executor: {taskName} - {ex}");
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
