#pragma warning disable 1591

using System;
using System.Linq;
using System.Threading;
using Shinden.Logger;

namespace Sanakan.Services
{
    public static class DailyReport
    {
        private static Timer _timer;
        private static DateTime _since;

        public static void Start(ILogger logger, params Func<string>[] sections)
        {
            _since = DateTime.Now;
            _timer = new Timer(_ => Run(logger, sections), null, UntilMidnight(), Timeout.InfiniteTimeSpan);
        }

        private static void Run(ILogger logger, Func<string>[] sections)
        {
            try
            {
                var now = DateTime.Now;
                var body = string.Join("\n", sections.Select(x => x()));
                logger.Log($"Raport dobowy ({_since:yyyy-MM-dd HH:mm} - {now:yyyy-MM-dd HH:mm}):\n{body}");
                _since = now;
            }
            catch (Exception ex)
            {
                logger.LogError($"Raport dobowy: {ex.Message}");
            }
            finally
            {
                _timer.Change(UntilMidnight(), Timeout.InfiniteTimeSpan);
            }
        }

        private static TimeSpan UntilMidnight() => DateTime.Today.AddDays(1) - DateTime.Now + TimeSpan.FromSeconds(1);
    }
}
