#pragma warning disable 1591

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Sanakan.Api
{
    public class ApiTraffic : IDisposable
    {
        private const int MaxReportLines = 25;

        public static readonly TimeSpan SlowRequest = TimeSpan.FromSeconds(2);

        private readonly ConcurrentDictionary<string, List<long>> _times = new ConcurrentDictionary<string, List<long>>();
        private readonly Timer _timer;
        private DateTime _lastReport = DateTime.UtcNow;

        public ApiTraffic(Action<string> sink, TimeSpan interval)
        {
            _lastReport = DateTime.UtcNow;
            _timer = new Timer(_ =>
            {
                var report = Flush();
                if (report != null) sink(report);
            }, null, interval, interval);
        }

        public static string GetKey(HttpContext context)
        {
            var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "(no route)";
            return $"{context.Request.Method} {route}";
        }

        public void Add(HttpContext context, long elapsedMs = 0)
        {
            var times = _times.GetOrAdd(GetKey(context), _ => new List<long>());
            lock (times) times.Add(elapsedMs);
        }

        private static long Percentile(List<long> sorted, double p) => sorted[Math.Max(0, (int)Math.Ceiling(p * sorted.Count) - 1)];

        public string Flush()
        {
            var now = DateTime.UtcNow;
            var seconds = Math.Max(1, (now - _lastReport).TotalSeconds);
            _lastReport = now;

            var snapshot = new List<(string Key, List<long> Times)>();
            foreach (var key in _times.Keys)
                if (_times.TryRemove(key, out var times))
                    lock (times) snapshot.Add((key, times.OrderBy(x => x).ToList()));

            if (snapshot.Count == 0)
                return null;

            var slowMs = (long)SlowRequest.TotalMilliseconds;
            var lines = snapshot.OrderByDescending(x => x.Times.Count).ThenBy(x => x.Key, StringComparer.Ordinal).ToList();
            var rows = new List<string[]> { new[] { "reqs", "p50", "p95", "max", "slow", "method", "route" } };
            rows.AddRange(lines.Take(MaxReportLines).Select(x =>
            {
                var slow = x.Times.Count(t => t >= slowMs);
                var space = x.Key.IndexOf(' ');
                return new[]
                {
                    x.Times.Count.ToString(), $"{Percentile(x.Times, 0.5)}ms", $"{Percentile(x.Times, 0.95)}ms", $"{x.Times[^1]}ms",
                    slow > 0 ? slow.ToString() : "-", space < 0 ? "" : x.Key.Substring(0, space), space < 0 ? x.Key : x.Key.Substring(space + 1)
                };
            }));
            var text = string.Join("\n", Services.TextTable.Render(rows, new[] { true, true, true, true, true, false, false }));
            if (lines.Count > MaxReportLines)
                text += $"\n(+{lines.Count - MaxReportLines} more routes, {lines.Skip(MaxReportLines).Sum(x => x.Times.Count)} requests)";

            var total = lines.Sum(x => x.Times.Count);
            var totalSlow = lines.Sum(x => x.Times.Count(t => t >= slowMs));
            return FormattableString.Invariant($"API traffic ({seconds / 60:0} min): {total} requests ({total / seconds:0.0}/s), {totalSlow} over {SlowRequest.TotalSeconds:0} s\n{text}");
        }

        public void Dispose() => _timer.Dispose();
    }

    public static class ApiStats
    {
        private const int MaxClients = 15;

        private static readonly ConcurrentDictionary<string, long> _clients = new ConcurrentDictionary<string, long>();

        public static void Add(HttpContext context) => _clients.AddOrUpdate(ApiAudit.GetClient(context), 1, (_, v) => v + 1);

        public static string FlushDaily()
        {
            var snapshot = new List<KeyValuePair<string, long>>();
            foreach (var key in _clients.Keys)
                if (_clients.TryRemove(key, out var count))
                    snapshot.Add(new KeyValuePair<string, long>(key, count));

            var total = snapshot.Sum(x => x.Value);
            if (total == 0)
                return "API: 0 requests";

            var clients = snapshot.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.Ordinal).ToList();
            var text = string.Join(", ", clients.Take(MaxClients).Select(x => $"{x.Key} {x.Value}"));
            if (clients.Count > MaxClients)
                text += $", +{clients.Count - MaxClients} others ({clients.Skip(MaxClients).Sum(x => x.Value)})";

            return $"API: {total} requests ({text})";
        }
    }
    // An anonymous endpoint that changes data: its calls are logged one by one like those of known callers.
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class AuditAnonymousAttribute : Attribute { }

    public static class ApiAudit
    {
        public const string Anonymous = "anonymous";
        // an endpoint that checks the app key by itself names the app here
        public const string AppItem = "AuditApp";
        private const int MaxUserAgentLength = 40;

        public static string Describe(HttpContext context, long elapsedMs)
        {
            if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)
                || HttpMethods.IsOptions(context.Request.Method))
                return null;

            var who = GetCaller(context.User);
            if (who == null && context.GetEndpoint()?.Metadata.GetMetadata<AuditAnonymousAttribute>() != null)
                who = context.Items[AppItem] is string app ? $"app:{app}" : GetClient(context);

            if (who == null)
                return null;

            return $"API: {who} {context.Request.Method} {context.Request.Path} -> {context.Response.StatusCode} ({elapsedMs}ms)";
        }

        public static string GetClient(HttpContext context)
        {
            var client = GetClient(context.User);
            return client == Anonymous ? $"{Anonymous}:{UserAgentKind(context.Request.Headers.UserAgent)}" : client;
        }

        public static string GetClient(ClaimsPrincipal user)
        {
            var app = user?.Claims.FirstOrDefault(x => x.Type == "UserKeyApp" || x.Type == AppKeyAuthenticationHandler.AppClaim)?.Value;
            if (app != null)
                return $"app:{app}";

            var site = user?.Claims.FirstOrDefault(x => x.Type == ClaimTypes.Webpage)?.Value;
            return site != null ? $"site:{site}" : Anonymous;
        }

        // Browsers differ only by version, so they are one group; any other client goes by its first product token.
        // The header is set by the caller, so only a short run of safe characters reaches the log.
        public static string UserAgentKind(string userAgent)
        {
            if (string.IsNullOrWhiteSpace(userAgent))
                return "none";

            if (userAgent.StartsWith("Mozilla/", StringComparison.Ordinal))
            {
                var lower = userAgent.ToLowerInvariant();
                return lower.Contains("bot") || lower.Contains("spider") || lower.Contains("crawl") ? "crawler" : "browser";
            }

            var token = userAgent.Trim().Split(' ', 2)[0];
            if (token.Length > MaxUserAgentLength)
                token = token.Substring(0, MaxUserAgentLength);

            return new string(token.Select(c => char.IsAsciiLetterOrDigit(c) || "._/-+".IndexOf(c) >= 0 ? c : '?').ToArray());
        }

        private static string GetCaller(ClaimsPrincipal user)
        {
            var discordId = user?.Claims.FirstOrDefault(x => x.Type == "DiscordId")?.Value;
            if (discordId != null)
            {
                var app = user.Claims.FirstOrDefault(x => x.Type == "UserKeyApp")?.Value;
                return $"u{discordId} app:{app ?? "token"}";
            }

            var appKey = user?.Claims.FirstOrDefault(x => x.Type == AppKeyAuthenticationHandler.AppClaim)?.Value;
            if (appKey != null)
                return $"app:{appKey}";

            var site = user?.Claims.FirstOrDefault(x => x.Type == ClaimTypes.Webpage)?.Value;
            return site != null ? $"site:{site}" : null;
        }
    }
}
