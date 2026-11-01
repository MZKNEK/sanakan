#pragma warning disable 1591

using System;
using System.Net;

namespace Sanakan.Services
{
    public class ShindenActivity
    {
        public static readonly ShindenActivity Default = new ShindenActivity();

        public ShindenActivity(Func<long> minute = null)
        {
            Requests = new MinuteStats(minute);
            Timeouts = new MinuteStats(minute);
            Recent = new RecentLatency(minute);
        }

        public MinuteStats Requests { get; }
        public MinuteStats Timeouts { get; }
        public RecentLatency Recent { get; }

        public void RequestCompleted(HttpStatusCode code, long elapsedMs)
        {
            var failed = IsFailure(code);
            Requests.Add(failed);

            if (failed) Recent.Failure();
            else Recent.Success(elapsedMs);
        }

        public void RequestFailed(bool timeout)
        {
            Requests.Add(true);
            if (timeout) Timeouts.Add(false);
            Recent.Failure();
        }

        public void ParsingFailed()
        {
            Requests.AddError();
            Recent.Failure();
        }

        private static bool IsFailure(HttpStatusCode code) => (int)code >= 500
            || code is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                or HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests;
    }
}
