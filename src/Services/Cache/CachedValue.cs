#pragma warning disable 1591

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Shinden.Logger;

namespace Sanakan.Services.Cache
{
    // A failed refresh keeps the last good value and the next attempt waits, so an outage does not hit every request.
    public class CachedValue<T> where T : class
    {
        private static readonly TimeSpan EmptyRetryAfter = TimeSpan.FromMinutes(1);

        private class Snapshot
        {
            public DateTime UpdatedAt { get; set; }
            public T Value { get; set; }
        }

        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
        private readonly object _loadLock = new object();
        private readonly TimeSpan _maxAge;
        private readonly TimeSpan _retryAfter;
        private readonly ILogger _logger;
        private readonly string _file;
        private DateTime _failedAt = DateTime.MinValue;
        private bool _loaded;

        public CachedValue(TimeSpan maxAge, TimeSpan retryAfter, string file = null, ILogger logger = null)
        {
            _maxAge = maxAge;
            _retryAfter = retryAfter;
            _logger = logger;
            _file = file;
            _loaded = file == null;
        }

        public T Value { get; private set; }
        public DateTime UpdatedAt { get; private set; } = DateTime.MinValue;
        public string LastError { get; private set; }

        // fetch returns null or throws when there is nothing usable
        public async Task<T> GetAsync(Func<Task<T>> fetch, DateTime now)
        {
            EnsureLoaded();
            if (!NeedsRefresh(now)) return Value;

            await _lock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!NeedsRefresh(now)) return Value;

                try
                {
                    var value = await fetch().ConfigureAwait(false);
                    if (value != null)
                    {
                        Value = value;
                        UpdatedAt = now;
                        LastError = null;
                        await SaveAsync().ConfigureAwait(false);
                        return value;
                    }
                    LastError = "Empty response";
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                }

                _failedAt = now;
                return Value;
            }
            finally
            {
                _lock.Release();
            }
        }

        private bool NeedsRefresh(DateTime now)
            => now - UpdatedAt >= _maxAge && now - _failedAt >= (Value == null ? EmptyRetryAfter : _retryAfter);

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

                    var snapshot = JsonConvert.DeserializeObject<Snapshot>(File.ReadAllText(_file));
                    if (snapshot?.Value == null) return;

                    Value = snapshot.Value;
                    UpdatedAt = snapshot.UpdatedAt;
                }
                catch (Exception ex)
                {
                    _logger?.LogError($"Cache: loading {_file}: {ex.Message}");
                }
            }
        }

        private async Task SaveAsync()
        {
            if (_file == null) return;

            try
            {
                await CacheFile.WriteAsync(_file, new Snapshot { UpdatedAt = UpdatedAt, Value = Value }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Cache: saving {_file}: {ex.Message}");
            }
        }
    }
}
