#pragma warning disable 1591

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
            var fullKey = $"{db.Database.GetConnectionString()}|{key}";
            if (Cache.TryGetValue(fullKey, out var cached) && cached is List<T> hit)
                return hit;

            // tokens are taken before the query runs, so an expire that lands during the query discards its result
            var tokens = tags.Select(tag => Tags.GetOrAdd(tag, _ => new CancellationTokenSource()).Token).ToList();
            var result = await query.ToListAsync();

            var entry = Copy(options ?? DefaultOptions);
            foreach (var token in tokens)
                entry.AddExpirationToken(new CancellationChangeToken(token));

            Cache.Set(fullKey, result, entry);
            return result;
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
