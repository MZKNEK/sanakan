#pragma warning disable 1591

using System;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sanakan.Config;
using Sanakan.Config.Model;

namespace Sanakan.Api
{
    public class UserKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "UserKey";
        public const string HeaderName = "x-user-key";
        public const string KeyPrefix = "snk_";
        public const string AppKeyPrefix = "snka_";

        private readonly IConfig _config;

        public UserKeyAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
            UrlEncoder encoder, ISystemClock clock, IConfig config) : base(options, logger, encoder, clock)
        {
            _config = config;
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(HeaderName, out var values))
                return AuthenticateResult.NoResult();

            var key = values.ToString();
            if (string.IsNullOrWhiteSpace(key))
                return AuthenticateResult.Fail("User key is empty");

            var hash = HashKey(key);
            using (var db = new Database.DatabaseContext(_config))
            {
                var userKey = await db.UserApiKeys.AsQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.KeyHash == hash);
                if (userKey == null)
                    return AuthenticateResult.Fail("User key is invalid");

                var apps = _config.Get().UserKeyApps;
                if (apps == null || !apps.Any(x => x.Bearer == userKey.Application && x.Has(ApiAppPermission.UserKeys)))
                    return AuthenticateResult.Fail("Application is no longer authorized");

                if (await db.Users.AsQueryable().AnyAsync(x => x.Id == userKey.UserId && x.IsBlacklisted))
                    return AuthenticateResult.Fail("User on blacklist");

                var claims = new[] {
                    new Claim("DiscordId", userKey.UserId.ToString()),
                    new Claim("Player", "waifu_player"),
                    new Claim("UserKeyApp", userKey.Application),
                };

                var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
                return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
            }
        }

        public static string GenerateKey(string prefix = KeyPrefix)
        {
            var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');

            return prefix + key;
        }

        public static string HashKey(string key)
        {
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
        }
    }
}
