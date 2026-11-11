#pragma warning disable 1591

using System;
using System.Collections.Concurrent;

namespace Sanakan.Services
{
    // a slow operation goes to the log channel; a very slow one is posted like an error, which pings the role,
    // at most once per key and interval
    public class SlowLog
    {
        private readonly ConcurrentDictionary<string, DateTime> _lastAlert = new ConcurrentDictionary<string, DateTime>();
        private readonly Func<DateTime> _now;

        public SlowLog(TimeSpan logAfter, TimeSpan alertAfter, TimeSpan alertEvery, Func<DateTime> now = null)
        {
            LogAfter = logAfter;
            AlertAfter = alertAfter;
            AlertEvery = alertEvery;
            _now = now ?? (() => DateTime.UtcNow);
        }

        public TimeSpan LogAfter { get; }
        public TimeSpan AlertAfter { get; }
        public TimeSpan AlertEvery { get; }

        public bool IsSlow(TimeSpan duration) => duration >= LogAfter;

        public void Record(ILogger logger, string key, TimeSpan duration, Func<string> describe)
        {
            if (logger == null || duration < LogAfter)
                return;

            if (duration >= AlertAfter && AlertIsDue(key))
                logger.LogError(describe());
            else
                logger.Log(describe());
        }

        private bool AlertIsDue(string key)
        {
            var now = _now();
            var due = true;
            _lastAlert.AddOrUpdate(key, now, (_, last) =>
            {
                if (now - last < AlertEvery)
                {
                    due = false;
                    return last;
                }
                return now;
            });
            return due;
        }
    }
}
