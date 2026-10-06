#pragma warning disable 1591

using System;

namespace Sanakan.Services
{
    // Czy zależność odpowiedziała poprawnie w bieżącej minucie i jak szybko (najkrótszy czas) - pozwala pominąć aktywne sprawdzanie.
    public class RecentLatency
    {
        private readonly Func<long> _clock;
        private readonly object _lock = new object();
        private long _minute = -1;
        private long _minMs = -1;
        private long _lastMs = -1;
        private bool _lastFailed;
        private long _failureMinute = -1;

        public RecentLatency(Func<long> clock = null)
        {
            _clock = clock ?? MinuteStats.CurrentMinute;
        }

        public void Success(long? ms)
        {
            var minute = _clock();
            lock (_lock)
            {
                if (_minute != minute)
                {
                    _minute = minute;
                    _minMs = -1;
                }

                if (ms.HasValue)
                {
                    _lastMs = ms.Value;
                    if (_minMs < 0 || ms.Value < _minMs)
                        _minMs = ms.Value;
                }

                _lastFailed = false;
            }
        }

        public void Failure()
        {
            var minute = _clock();
            lock (_lock)
            {
                _lastFailed = true;
                _failureMinute = minute;
            }
        }

        // ostatnia odpowiedź się nie udała i było to w bieżącej lub jednej z (minutes - 1) poprzednich minut
        public bool FailedWithin(int minutes)
        {
            lock (_lock)
            {
                return _lastFailed && _clock() - _failureMinute < minutes;
            }
        }

        // null - brak udanej odpowiedzi w tej minucie albo ostatnia się nie udała
        public long? Get()
        {
            lock (_lock)
            {
                if (_minute != _clock() || _lastFailed)
                    return null;

                if (_minMs >= 0) return _minMs;
                return _lastMs >= 0 ? _lastMs : null;
            }
        }
    }
}
