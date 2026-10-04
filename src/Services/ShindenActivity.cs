#pragma warning disable 1591

using System;
using System.Net;

namespace Sanakan.Services
{
    public class ShindenActivity
    {
        private const long StaleRequestMs = 30_000;

        public static readonly ShindenActivity Default = new ShindenActivity();

        private readonly Func<long> _nowMs;
        private readonly object _lock = new object();
        private int _inFlight;
        private bool _overlapped;
        private long _soloStart;
        private long _lastStart;

        public ShindenActivity(Func<long> nowMs = null, Func<long> minute = null)
        {
            _nowMs = nowMs ?? (() => Environment.TickCount64);
            Requests = new MinuteStats(minute);
            Timeouts = new MinuteStats(minute);
            Recent = new RecentLatency(minute);
        }

        public MinuteStats Requests { get; }
        public MinuteStats Timeouts { get; }
        public RecentLatency Recent { get; }

        public void Track(string message)
        {
            if (message == null) return;

            if (message.StartsWith("Processing request: ", StringComparison.Ordinal))
            {
                Requests.Add(false);
                Started();
            }
            else if (message.StartsWith("Timeout while sending request", StringComparison.Ordinal))
            {
                Requests.AddError();
                Timeouts.Add(false);
                Completed(false);
            }
            else if (message.StartsWith("Error while sending request", StringComparison.Ordinal))
            {
                Requests.AddError();
                Completed(false);
            }
            else if (message.StartsWith("In parsing: ", StringComparison.Ordinal))
            {
                Requests.AddError();
                Recent.Failure();
            }
            else if (message.StartsWith("Response code: ", StringComparison.Ordinal))
            {
                var failed = Enum.TryParse<HttpStatusCode>(message.Substring(15).Trim(), out var code) && IsFailure(code);
                if (failed) Requests.AddError();
                Completed(!failed);
            }
        }

        private void Started()
        {
            var now = _nowMs();
            lock (_lock)
            {
                if (_inFlight > 0 && now - _lastStart > StaleRequestMs)
                    _inFlight = 0;

                if (_inFlight == 0)
                {
                    _soloStart = now;
                    _overlapped = false;
                }
                else _overlapped = true;

                _inFlight++;
                _lastStart = now;
            }
        }

        // Czas odpowiedzi znamy tylko dla zapytań, które nie nakładały się na inne - log nie mówi, której odpowiedzi dotyczy.
        private void Completed(bool ok)
        {
            var now = _nowMs();
            lock (_lock)
            {
                long? measured = _inFlight == 1 && !_overlapped ? now - _soloStart : null;
                if (_inFlight > 0) _inFlight--;

                if (ok) Recent.Success(measured);
                else Recent.Failure();
            }
        }

        private static bool IsFailure(HttpStatusCode code) => (int)code >= 500
            || code is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                or HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests;
    }
}
