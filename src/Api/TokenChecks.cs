#pragma warning disable 1591

using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sanakan.Config;
using Sanakan.Config.Model;

namespace Sanakan.Api
{
    public interface IPlayerBlacklist
    {
        Task<bool> IsBlacklistedAsync(ulong discordId);
    }

    public class DatabasePlayerBlacklist : IPlayerBlacklist
    {
        private readonly IConfig _config;

        public DatabasePlayerBlacklist(IConfig config) => _config = config;

        public async Task<bool> IsBlacklistedAsync(ulong discordId)
        {
            using var db = new Database.DatabaseContext(_config);
            return await db.Users.AsQueryable().AnyAsync(x => x.Id == discordId && x.IsBlacklisted);
        }
    }

    // A signed token is only a snapshot, so what it grants is checked again on every request.
    public static class TokenChecks
    {
        public static async Task OnTokenValidatedAsync(TokenValidatedContext context)
        {
            var user = context.Principal;
            var services = context.HttpContext.RequestServices;

            if (user.HasClaim(c => c.Type == "Player"))
            {
                if (!ulong.TryParse(user.FindFirst("DiscordId")?.Value, out var discordId))
                {
                    context.Fail("Player token without user");
                    return;
                }

                if (await services.GetRequiredService<IPlayerBlacklist>().IsBlacklistedAsync(discordId))
                    context.Fail("User on blacklist");
                return;
            }

            if (!IsSiteAppActive(services.GetRequiredService<IConfig>(), user.FindFirst(ClaimTypes.Webpage)?.Value))
                context.Fail("Application is no longer authorized");
        }

        public static bool IsSiteAppActive(IConfig config, string bearer)
        {
            var apps = config.Get().UserKeyApps;
            return bearer != null && apps != null && apps.Any(x => x.Bearer == bearer && x.Has(ApiAppPermission.Site));
        }
    }
}
