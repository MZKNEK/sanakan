#pragma warning disable 1591

using System;

namespace Sanakan.Services
{
    public readonly record struct MinuteSnapshot(int Total, int Errors, int Timed, long SumMs, long MaxMs)
    {
        public long AvgMs => Timed > 0 ? SumMs / Timed : 0;
        public double ErrorRate => Total > 0 ? Math.Round(Math.Min(100.0, 100.0 * Errors / Total), 1) : 0;
    }

    public class MinuteStats
    {
        private const int Buckets = 60;
        private const long Empty = long.MinValue / 2;

        public static readonly MinuteStats Commands = new MinuteStats();
        public static readonly MinuteStats RejectedCommands = new MinuteStats();

        private readonly Func<long> _clock;
        private readonly object _lock = new object();
        private readonly long[] _minutes = new long[Buckets];
        private readonly int[] _total = new int[Buckets];
        private readonly int[] _errors = new int[Buckets];
        private readonly int[] _timed = new int[Buckets];
        private readonly long[] _sumMs = new long[Buckets];
        private readonly long[] _maxMs = new long[Buckets];

        public MinuteStats(Func<long> clock = null)
        {
            _clock = clock ?? CurrentMinute;
            Array.Fill(_minutes, Empty);
        }

        public void Add(bool failed) => Add(1, failed ? 1 : 0, null);

        public void AddError() => Add(0, 1, null);

        public void AddTimed(long ms) => Add(1, 0, ms);

        public void Add(int total, int errors) => Add(total, errors, null);

        private void Add(int total, int errors, long? ms)
        {
            var minute = _clock();
            var i = (int)(minute % Buckets);

            lock (_lock)
            {
                if (_minutes[i] != minute)
                {
                    _minutes[i] = minute;
                    _total[i] = 0;
                    _errors[i] = 0;
                    _timed[i] = 0;
                    _sumMs[i] = 0;
                    _maxMs[i] = 0;
                }

                _total[i] += total;
                _errors[i] += errors;
                if (ms.HasValue)
                {
                    _timed[i]++;
                    _sumMs[i] += ms.Value;
                    _maxMs[i] = Math.Max(_maxMs[i], ms.Value);
                }
            }
        }

        public MinuteSnapshot Get(int minutes)
        {
            var now = _clock();
            int total = 0, errors = 0, timed = 0;
            long sumMs = 0, maxMs = 0;

            lock (_lock)
            {
                for (int i = 0; i < Buckets; i++)
                {
                    if (now - _minutes[i] >= minutes) continue;

                    total += _total[i];
                    errors += _errors[i];
                    timed += _timed[i];
                    sumMs += _sumMs[i];
                    maxMs = Math.Max(maxMs, _maxMs[i]);
                }
            }

            return new MinuteSnapshot(total, errors, timed, sumMs, maxMs);
        }

        public static long CurrentMinute() => DateTime.UtcNow.Ticks / TimeSpan.TicksPerMinute;
    }
}
