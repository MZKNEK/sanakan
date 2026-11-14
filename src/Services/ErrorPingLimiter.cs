#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.Threading;

namespace Sanakan.Services
{
    // Pings on every error, but during an error flood only once per FloodPingInterval; errors in between are counted.
    public class ErrorPingLimiter
    {
        public static readonly TimeSpan FloodWindow = TimeSpan.FromMinutes(10);
        public static readonly TimeSpan FloodPingInterval = TimeSpan.FromMinutes(30);
        public const int FloodThreshold = 10;

        private readonly Lock _lock = new();
        private readonly Queue<DateTime> _recent = new Queue<DateTime>();
        private DateTime _lastPing = DateTime.MinValue;
        private int _sinceLastPing;

        public void Record(DateTime now)
        {
            lock (_lock)
            {
                _recent.Enqueue(now);
                ++_sinceLastPing;
                Trim(now);
            }
        }

        public bool TryTake(DateTime now, out int errors, out bool flood)
        {
            lock (_lock)
            {
                errors = _sinceLastPing;
                Trim(now);
                flood = _recent.Count > FloodThreshold;
                if (errors == 0)
                    return false;

                if (flood && now - _lastPing < FloodPingInterval)
                    return false;

                _sinceLastPing = 0;
                _lastPing = now;
                return true;
            }
        }

        private void Trim(DateTime now)
        {
            while (_recent.Count > 0 && now - _recent.Peek() > FloodWindow)
                _recent.Dequeue();
        }
    }
}
