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

        public ApiTraffic(Action<string> sink, TimeSpan interval)
        {
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

            return $"API traffic ({lines.Sum(x => x.Value)}):\n{text}";
        }

        public void Dispose() => _timer.Dispose();
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

        private static string GetCaller(ClaimsPrincipal user)
        {
            var discordId = user?.Claims.FirstOrDefault(x => x.Type == "DiscordId")?.Value;
            if (discordId != null)
            {
                var app = user.Claims.FirstOrDefault(x => x.Type == "UserKeyApp")?.Value;
                return $"u{discordId} app:{app ?? "token"}";
            }

            var site = user?.Claims.FirstOrDefault(x => x.Type == ClaimTypes.Webpage)?.Value;
            return site != null ? $"site:{site}" : null;
        }
    }
}
