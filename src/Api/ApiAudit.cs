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

        private readonly ConcurrentDictionary<string, int> _counts = new ConcurrentDictionary<string, int>();
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

        public void Add(HttpContext context) => _counts.AddOrUpdate(GetKey(context), 1, (_, v) => v + 1);

        public string Flush()
        {
            var now = DateTime.UtcNow;
            var seconds = Math.Max(1, (now - _lastReport).TotalSeconds);
            _lastReport = now;

            var snapshot = new List<KeyValuePair<string, int>>();
            foreach (var key in _counts.Keys)
                if (_counts.TryRemove(key, out var count))
                    snapshot.Add(new KeyValuePair<string, int>(key, count));

            if (snapshot.Count == 0)
                return null;

            var lines = snapshot.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.Ordinal).ToList();
            var text = string.Join("\n", lines.Take(MaxReportLines).Select(x => $"{x.Key} - {x.Value}"));
            if (lines.Count > MaxReportLines)
                text += $"\n... +{lines.Count - MaxReportLines} more ({lines.Skip(MaxReportLines).Sum(x => x.Value)})";

            var total = lines.Sum(x => x.Value);
            return $"API traffic ({seconds / 60:0}min): {total} ({total / seconds:0.0}/s)\n{text}";
        }

        public void Dispose() => _timer.Dispose();
    }

    public static class ApiStats
    {
        private const int MaxClients = 15;

        private static readonly ConcurrentDictionary<string, long> _clients = new ConcurrentDictionary<string, long>();

        public static void Add(HttpContext context) => _clients.AddOrUpdate(ApiAudit.GetClient(context.User), 1, (_, v) => v + 1);

        public static string FlushDaily()
        {
            var snapshot = new List<KeyValuePair<string, long>>();
            foreach (var key in _clients.Keys)
                if (_clients.TryRemove(key, out var count))
                    snapshot.Add(new KeyValuePair<string, long>(key, count));

            var total = snapshot.Sum(x => x.Value);
            if (total == 0)
                return "API: 0 zapytań";

            var clients = snapshot.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.Ordinal).ToList();
            var text = string.Join(", ", clients.Take(MaxClients).Select(x => $"{x.Key} {x.Value}"));
            if (clients.Count > MaxClients)
                text += $", +{clients.Count - MaxClients} innych ({clients.Skip(MaxClients).Sum(x => x.Value)})";

            return $"API: {total} zapytań ({text})";
        }
    }
    public static class ApiAudit
    {
        public static string Describe(HttpContext context, long elapsedMs)
        {
            if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)
                || HttpMethods.IsOptions(context.Request.Method))
                return null;

            var who = GetCaller(context.User);
            if (who == null)
                return null;

            return $"API: {who} {context.Request.Method} {context.Request.Path} -> {context.Response.StatusCode} ({elapsedMs}ms)";
        }

        public static string GetClient(ClaimsPrincipal user)
        {
            var app = user?.Claims.FirstOrDefault(x => x.Type == "UserKeyApp" || x.Type == AppKeyAuthenticationHandler.AppClaim)?.Value;
            if (app != null)
                return $"app:{app}";

            var site = user?.Claims.FirstOrDefault(x => x.Type == ClaimTypes.Webpage)?.Value;
            return site != null ? $"site:{site}" : "anonymous";
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
