#pragma warning disable 1591

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace Sanakan
{
    public static class QueryCache
    {
        private static readonly MemoryCache Cache = new MemoryCache(new MemoryCacheOptions());
        private static readonly ConcurrentDictionary<string, CancellationTokenSource> Tags = new ConcurrentDictionary<string, CancellationTokenSource>();

        public static MemoryCacheEntryOptions DefaultOptions { get; } = new MemoryCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(60),
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(12)
        };

        public static Task<IEnumerable<T>> FromCacheAsync<T>(this IQueryable<T> query, DbContext db, string key, params string[] tags)
            => query.FromCacheAsync(db, key, null, tags);

        public static async Task<IEnumerable<T>> FromCacheAsync<T>(this IQueryable<T> query, DbContext db, string key, MemoryCacheEntryOptions options, params string[] tags)
        {
            var kind = KindOf(key);
            var fullKey = $"{db.Database.GetConnectionString()}|{key}";
            if (Cache.TryGetValue(fullKey, out var cached) && cached is List<T> hit)
            {
                QueryCacheStats.Hit(kind);
                return hit;
            }

            // tokens are taken before the query runs, so an expire that lands during the query discards its result
            var tokens = tags.Select(tag => Tags.GetOrAdd(tag, _ => new CancellationTokenSource()).Token).ToList();
            var timer = Stopwatch.StartNew();
            var result = await query.ToListAsync();
            timer.Stop();

            var discarded = tokens.Any(x => x.IsCancellationRequested);
            QueryCacheStats.Load(kind, timer.ElapsedMilliseconds, result.Count, discarded);
            if (discarded) return result;

            var entry = Copy(options ?? DefaultOptions);
            foreach (var token in tokens)
                entry.AddExpirationToken(new CancellationChangeToken(token));

            entry.RegisterPostEvictionCallback(OnEvicted, new EntryInfo(kind, Environment.TickCount64, result.Count));
            QueryCacheStats.Stored(kind, result.Count);
            Cache.Set(fullKey, result, entry);
            return result;
        }

        // "user-full-123" -> "user-full", so stats are grouped per query type instead of per id
        public static string KindOf(string key)
        {
            for (int i = 0; i < key.Length; i++)
            {
                if (key[i] == '-' && (i + 1 == key.Length || !char.IsLetter(key[i + 1])))
                    return key.Substring(0, i);
            }
            return key;
        }

        public static int EntryCount => Cache.Count;
        public static int TagCount => Tags.Count;

        private sealed record EntryInfo(string Kind, long LoadedAt, int Results);

        private static void OnEvicted(object key, object value, EvictionReason reason, object state)
        {
            if (state is EntryInfo info)
                QueryCacheStats.Evicted(info.Kind, reason, Environment.TickCount64 - info.LoadedAt, info.Results);
        }

        public static void ExpireTag(params string[] tags)
        {
            foreach (var tag in tags)
            {
                // not disposed: a reader may still be holding its token
                if (Tags.TryRemove(tag, out var source))
                    source.Cancel();
            }
        }

        private static MemoryCacheEntryOptions Copy(MemoryCacheEntryOptions options) => new MemoryCacheEntryOptions
        {
            AbsoluteExpiration = options.AbsoluteExpiration,
            AbsoluteExpirationRelativeToNow = options.AbsoluteExpirationRelativeToNow,
            SlidingExpiration = options.SlidingExpiration,
            Priority = options.Priority,
            Size = options.Size
        };
    }
}
