#pragma warning disable 1591

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Sanakan.Database;
using Sanakan.Database.Models;

namespace Sanakan.Services
{
    public static class Idempotency
    {
        public const string KeyHeader = "Idempotency-Key";
        public const string ReplayedHeader = "Idempotent-Replayed";
        public const int MaxKeyLength = 64;

        public static readonly TimeSpan Retention = TimeSpan.FromHours(48);
        private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);

        // Requests still running; a record reaches the database only together with the change it guards.
        private static readonly ConcurrentDictionary<(string Scope, string Key), IdempotencyRecord> _inFlight
            = new ConcurrentDictionary<(string Scope, string Key), IdempotencyRecord>();

        private static long _lastCleanupTicks;

        public static bool IsValidKey(string key)
            => !string.IsNullOrEmpty(key) && key.Length <= MaxKeyLength && key.All(c => c > ' ' && c < 127);

        public static string Hash(object request)
            => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(request))));

        public static bool SameRequest(IdempotencyRecord a, IdempotencyRecord b)
            => a.OwnerId == b.OwnerId && a.RequestHash == b.RequestHash;

        /// <summary>Returns null when the caller now holds the key, otherwise the request already holding it.</summary>
        public static IdempotencyRecord TryBegin(IdempotencyRecord record)
        {
            var current = _inFlight.GetOrAdd((record.Scope, record.Key), record);
            return ReferenceEquals(current, record) ? null : current;
        }

        public static void End(IdempotencyRecord record)
            => _inFlight.TryRemove(new KeyValuePair<(string Scope, string Key), IdempotencyRecord>((record.Scope, record.Key), record));

        public static async Task<IdempotencyRecord> FindAsync(DatabaseContext db, string scope, string key, DateTime now)
        {
            var record = await db.IdempotencyRecords.AsQueryable().FirstOrDefaultAsync(x => x.Scope == scope && x.Key == key);
            if (record == null || record.CreatedAt > now - Retention)
                return record;

            // the unique index would reject reusing the key
            db.IdempotencyRecords.Remove(record);
            await db.SaveChangesAsync();
            return null;
        }

        public static async Task CleanupIfDueAsync(DatabaseContext db, DateTime now)
        {
            var last = Interlocked.Read(ref _lastCleanupTicks);
            if (now.Ticks - last < CleanupInterval.Ticks || Interlocked.CompareExchange(ref _lastCleanupTicks, now.Ticks, last) != last)
                return;

            await DeleteExpiredAsync(db, now);
        }

        public static Task<int> DeleteExpiredAsync(DatabaseContext db, DateTime now)
        {
            var cutoff = now - Retention;
            return db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM IdempotencyRecords WHERE CreatedAt < {cutoff}");
        }
    }
}
