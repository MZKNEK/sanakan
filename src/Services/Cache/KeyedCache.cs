#pragma warning disable 1591

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Shinden.Logger;

namespace Sanakan.Services.Cache
{
    // Same rules as CachedValue, per key; entries not refreshed for keepFor are dropped.
    public class KeyedCache<TKey, TValue> where TValue : class
    {
        private static readonly TimeSpan SweepEvery = TimeSpan.FromHours(1);
        private static readonly TimeSpan SaveEvery = TimeSpan.FromMinutes(10);

        private class Entry
        {
            public TValue Value { get; set; }
            public DateTime UpdatedAt { get; set; }
            [JsonIgnore]
            public DateTime FailedAt { get; set; }
        }

        private ConcurrentDictionary<TKey, Entry> _entries = new ConcurrentDictionary<TKey, Entry>();
        private readonly SemaphoreSlim _saveLock = new SemaphoreSlim(1, 1);
        private readonly object _loadLock = new object();
        private readonly TimeSpan _maxAge;
        private readonly TimeSpan _retryAfter;
        private readonly TimeSpan _keepFor;
        private readonly ILogger _logger;
        private readonly string _file;
        private DateTime _failedAt = DateTime.MinValue;
        private DateTime _lastSweep = DateTime.MinValue;
        private DateTime _lastSave = DateTime.MinValue;
        private volatile bool _dirty;
        private bool _loaded;

        public KeyedCache(TimeSpan maxAge, TimeSpan retryAfter, TimeSpan keepFor, string file = null, ILogger logger = null)
        {
            _maxAge = maxAge;
            _retryAfter = retryAfter;
            _keepFor = keepFor;
            _logger = logger;
            _file = file;
            _loaded = file == null;
        }

        public int Count => _entries.Count;

        public TValue Peek(TKey key)
        {
            EnsureLoaded();
            return _entries.TryGetValue(key, out var entry) ? entry.Value : null;
        }

        // fetch returns null when there is no value for the key and throws when the source is unreachable,
        // which pauses refreshing of all stale entries; the exception is rethrown only when there is nothing stale to return
        public async Task<TValue> GetAsync(TKey key, Func<Task<TValue>> fetch, DateTime now)
        {
            EnsureLoaded();
            _entries.TryGetValue(key, out var entry);
            if (entry != null && (now - entry.UpdatedAt < _maxAge || now - entry.FailedAt < _retryAfter || now - _failedAt < _retryAfter))
                return entry.Value;

            TValue value;
            try
            {
                value = await fetch().ConfigureAwait(false);
            }
            catch (Exception)
            {
                _failedAt = now;
                if (entry == null) throw;

                return entry.Value;
            }

            if (value == null)
            {
                if (entry != null) entry.FailedAt = now;
                return entry?.Value;
            }

            _entries[key] = new Entry { Value = value, UpdatedAt = now };
            _dirty = true;
            Sweep(now);

            if (_file != null && now - _lastSave >= SaveEvery)
                await SaveAsync(now).ConfigureAwait(false);

            return value;
        }

        public async Task SaveAsync(DateTime now)
        {
            if (_file == null || !_dirty) return;

            await _saveLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!_dirty) return;
                _dirty = false;
                _lastSave = now;

                var snapshot = _entries.ToArray().ToDictionary(x => x.Key, x => x.Value);
                await CacheFile.WriteAsync(_file, snapshot).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _dirty = true;
                _logger?.LogError($"Cache: saving {_file}: {ex.Message}");
            }
            finally
            {
                _saveLock.Release();
            }
        }

        private void Sweep(DateTime now)
        {
            if (now - _lastSweep < SweepEvery) return;
            _lastSweep = now;

            foreach (var item in _entries.Where(x => now - x.Value.UpdatedAt >= _keepFor).ToList())
                _entries.TryRemove(item.Key, out _);
        }

        private void EnsureLoaded()
        {
            if (_loaded) return;

            lock (_loadLock)
            {
                if (_loaded) return;
                _loaded = true;

                try
                {
                    if (!File.Exists(_file)) return;

                    var saved = JsonConvert.DeserializeObject<Dictionary<TKey, Entry>>(File.ReadAllText(_file));
                    if (saved != null)
                        _entries = new ConcurrentDictionary<TKey, Entry>(saved.Where(x => x.Value?.Value != null));
                }
                catch (Exception ex)
                {
                    _logger?.LogError($"Cache: loading {_file}: {ex.Message}");
                }
            }
        }
    }
}
