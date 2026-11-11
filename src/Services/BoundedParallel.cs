#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Sanakan.Services
{
    public static class BoundedParallel
    {
        // Shinden lookups a command makes at once; the client's rate limiter still caps the minute
        public const int ShindenLookups = 5;

        // runs the lookup for every item, at most maxParallel at a time; the results keep the items' order
        public static async Task<List<TResult>> SelectAsync<T, TResult>(IEnumerable<T> items, int maxParallel, Func<T, Task<TResult>> lookup)
        {
            using var gate = new SemaphoreSlim(maxParallel);
            var tasks = items.Select(async item =>
            {
                await gate.WaitAsync().ConfigureAwait(false);
                try { return await lookup(item).ConfigureAwait(false); }
                finally { gate.Release(); }
            }).ToList();
            return (await Task.WhenAll(tasks).ConfigureAwait(false)).ToList();
        }
    }
}
