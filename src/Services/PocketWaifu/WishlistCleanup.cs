#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sanakan.Database.Models;

namespace Sanakan.Services.PocketWaifu
{
    public enum WishCheck
    {
        Exists,
        Missing,
        Unknown
    }

    public static class WishlistCleanup
    {
        public static List<WishlistObject> Duplicates(IEnumerable<WishlistObject> wishes)
            => wishes.GroupBy(x => (x.Type, x.ObjectId))
                .SelectMany(g => g.OrderByDescending(x => x.Entry == WishlistEntryType.Persistent).ThenBy(x => x.Id).Skip(1))
                .ToList();

        public static async Task<(List<WishlistObject> Missing, int Unknown)> FindMissingAsync(IEnumerable<WishlistObject> wishes,
            Func<WishlistObject, Task<WishCheck>> check, int parallel = 5)
        {
            using (var gate = new SemaphoreSlim(parallel))
            {
                var results = await Task.WhenAll(wishes.Select(async wish =>
                {
                    await gate.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        return (wish, result: await check(wish).ConfigureAwait(false));
                    }
                    catch (Exception)
                    {
                        return (wish, result: WishCheck.Unknown);
                    }
                    finally
                    {
                        gate.Release();
                    }
                }));

                return (results.Where(x => x.result == WishCheck.Missing).Select(x => x.wish).ToList(),
                    results.Count(x => x.result == WishCheck.Unknown));
            }
        }
    }
}
