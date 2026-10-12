#pragma warning disable 1591

using System;
using System.Threading;
using System.Threading.Tasks;
using Sanakan.Config;
using Sanakan.Extensions;
using Sanakan.Services.Time;
using Shinden.Logger;

namespace Sanakan.Services.PocketWaifu
{
    public static class WishlistRecount
    {
        private static readonly TimeSpan RunAt = TimeSpan.FromHours(4);
        private static Timer _timer;

        public static void Start(IConfig config, ISystemTime time, ILogger logger)
        {
            _timer = new Timer(_ => _ = RunAsync(config, time, logger), null, UntilNextRun(time.Now()), Timeout.InfiniteTimeSpan);
        }

        private static async Task RunAsync(IConfig config, ISystemTime time, ILogger logger)
        {
            try
            {
                using var db = new Database.DatabaseContext(config);
                var updated = await db.RecalculateWishlistCountsAsync(time.Now());
                logger.Log($"KC recount: updated {updated} characters");
            }
            catch (Exception ex)
            {
                logger.LogError($"KC recount: {ex}");
            }
            finally
            {
                _timer.Change(UntilNextRun(time.Now()), Timeout.InfiniteTimeSpan);
            }
        }

        public static TimeSpan UntilNextRun(DateTime now)
        {
            var next = now.Date + RunAt;
            if (next <= now) next = next.AddDays(1);
            return next - now;
        }
    }
}
