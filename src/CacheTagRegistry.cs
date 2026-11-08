#pragma warning disable 1591

using System.Collections.Concurrent;
using System.Linq;
using System.Threading;

namespace Sanakan
{
    // Tags are never just dropped: removing one always cancels it, so no cache entry can outlive its invalidation.
    public sealed class CacheTagRegistry
    {
        private sealed class TagState
        {
            public readonly CancellationTokenSource Source = new CancellationTokenSource();
            public long LastUsed;
        }

        private readonly ConcurrentDictionary<string, TagState> _tags = new ConcurrentDictionary<string, TagState>();
        private int _sweeping;
        private long _lastSweep;

        public int Count => _tags.Count;

        public CancellationToken Use(string tag, long now)
        {
            var state = _tags.GetOrAdd(tag, _ => new TagState());
            Volatile.Write(ref state.LastUsed, now);
            return state.Source.Token;
        }

        public void Expire(string tag)
        {
            // not disposed: a reader may still be holding its token
            if (_tags.TryRemove(tag, out var state))
                state.Source.Cancel();
        }

        public bool ShouldSweep(long now, long intervalMs, int maxTags)
            => _tags.Count > maxTags || now - Volatile.Read(ref _lastSweep) >= intervalMs;

        // Removes tags idle for longer than any cache entry can live (no cached data is affected),
        // then, if still over the limit, the least recently used half.
        public int Sweep(long now, long idleMs, int maxTags)
        {
            if (Interlocked.Exchange(ref _sweeping, 1) == 1)
                return 0;

            try
            {
                Volatile.Write(ref _lastSweep, now);

                var removed = 0;
                foreach (var tag in _tags)
                {
                    if (now - Volatile.Read(ref tag.Value.LastUsed) > idleMs && Remove(tag.Key, tag.Value))
                        ++removed;
                }

                if (_tags.Count <= maxTags)
                    return removed;

                var oldest = _tags.ToArray()
                    .OrderBy(x => Volatile.Read(ref x.Value.LastUsed))
                    .Take(_tags.Count - (maxTags / 2))
                    .ToList();

                foreach (var tag in oldest)
                {
                    if (Remove(tag.Key, tag.Value))
                        ++removed;
                }

                return removed;
            }
            finally
            {
                Volatile.Write(ref _sweeping, 0);
            }
        }

        private bool Remove(string tag, TagState state)
        {
            if (!_tags.TryRemove(new System.Collections.Generic.KeyValuePair<string, TagState>(tag, state)))
                return false;

            state.Source.Cancel();
            return true;
        }
    }
}
