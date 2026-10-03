#pragma warning disable 1591

using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Sanakan.Api
{
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
