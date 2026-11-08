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
using Sanakan.Database.Models;

namespace Sanakan
{
    public static class QueryCache
    {
        // in rows, a user or deck counts its cards too; ~1 KB per card, so roughly 2 GB
        public const long SizeLimit = 2_000_000;

        // ~180 B per tag, so the limit is roughly 180 MB
        public const int MaxTags = 1_000_000;
        // no entry lives longer than this, so a tag idle for longer has no cached data left
        public static readonly TimeSpan MaxEntryLifetime = TimeSpan.FromHours(12);
        private static readonly long TagIdleMs = (long)(MaxEntryLifetime + TimeSpan.FromHours(1)).TotalMilliseconds;
        private static readonly long TagSweepIntervalMs = (long)TimeSpan.FromHours(1).TotalMilliseconds;

        private static readonly MemoryCache Cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = SizeLimit });
        private static readonly CacheTagRegistry Tags = new CacheTagRegistry();
        private static long _currentSize;

        public static MemoryCacheEntryOptions DefaultOptions { get; } = new MemoryCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(60),
            AbsoluteExpirationRelativeToNow = MaxEntryLifetime
        };

        public static Task<IEnumerable<T>> FromCacheAsync<T>(this IQueryable<T> query, DbContext db, string key, params string[] tags)
            => query.FromCacheAsync(db, key, null, tags);

        public static async Task<IEnumerable<T>> FromCacheAsync<T>(this IQueryable<T> query, DbContext db, string key, MemoryCacheEntryOptions options, params string[] tags)
            => await GetOrLoadAsync(db, key, () => query.ToListAsync(), options, tags);

        public static async Task<List<T>> GetOrLoadAsync<T>(DbContext db, string key, Func<Task<List<T>>> load, MemoryCacheEntryOptions options, params string[] tags)
        {
            var kind = KindOf(key);
            var fullKey = $"{db.Database.GetConnectionString()}|{key}";
            if (Cache.TryGetValue(fullKey, out var cached) && cached is List<T> hit)
            {
                QueryCacheStats.Hit(kind);
                return hit;
            }

            // tokens are taken before the query runs, so an expire that lands during the query discards its result
            var now = Environment.TickCount64;
            var tokens = tags.Select(tag => Tags.Use(tag, now)).ToList();
            if (Tags.ShouldSweep(now, TagSweepIntervalMs, MaxTags))
                _ = Task.Run(() => Tags.Sweep(now, TagIdleMs, MaxTags));

            var timer = Stopwatch.StartNew();
            var result = await load();
            timer.Stop();

            var discarded = tokens.Any(x => x.IsCancellationRequested);
            QueryCacheStats.Load(kind, timer.ElapsedMilliseconds, result.Count, discarded);
            if (discarded) return result;

            var size = SizeOf(result);
            var entry = Copy(options ?? DefaultOptions);
            entry.Size = size;
            foreach (var token in tokens)
                entry.AddExpirationToken(new CancellationChangeToken(token));

            entry.RegisterPostEvictionCallback(OnEvicted, new EntryInfo(kind, Environment.TickCount64, result.Count, size));
            QueryCacheStats.Stored(kind, result.Count);
            Interlocked.Add(ref _currentSize, size);
            Cache.Set(fullKey, result, entry);
            return result;
        }

        public static long SizeOf<T>(List<T> result)
        {
            long size = 0;
            foreach (var item in result)
            {
                size += item switch
                {
                    User user => 1 + (user.GameDeck?.Cards?.Count ?? 0),
                    GameDeck deck => 1 + (deck.Cards?.Count ?? 0),
                    _ => 1
                };
            }
            return Math.Max(1, size);
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
        public static long CurrentSize => Interlocked.Read(ref _currentSize);

        private sealed record EntryInfo(string Kind, long LoadedAt, int Results, long Size);

        private static void OnEvicted(object key, object value, EvictionReason reason, object state)
        {
            if (state is EntryInfo info)
            {
                Interlocked.Add(ref _currentSize, -info.Size);
                QueryCacheStats.Evicted(info.Kind, reason, Environment.TickCount64 - info.LoadedAt, info.Results);
            }
        }

        public static void ExpireTag(params string[] tags)
        {
            foreach (var tag in tags)
                Tags.Expire(tag);
        }

        public static MemoryCacheEntryOptions Copy(MemoryCacheEntryOptions options)
        {
            var copy = new MemoryCacheEntryOptions
            {
                AbsoluteExpiration = options.AbsoluteExpiration,
                AbsoluteExpirationRelativeToNow = options.AbsoluteExpirationRelativeToNow,
                SlidingExpiration = options.SlidingExpiration,
                Priority = options.Priority
            };

            // the tag sweep relies on entries never outliving MaxEntryLifetime
            if (copy.AbsoluteExpirationRelativeToNow > MaxEntryLifetime)
                copy.AbsoluteExpirationRelativeToNow = MaxEntryLifetime;

            if (copy.AbsoluteExpiration > DateTimeOffset.Now + MaxEntryLifetime)
                copy.AbsoluteExpiration = DateTimeOffset.Now + MaxEntryLifetime;

            if (copy.AbsoluteExpiration == null && copy.AbsoluteExpirationRelativeToNow == null)
                copy.AbsoluteExpirationRelativeToNow = MaxEntryLifetime;

            return copy;
        }
    }
}
