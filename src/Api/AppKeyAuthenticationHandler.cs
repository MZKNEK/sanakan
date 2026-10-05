#pragma warning disable 1591

using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sanakan.Config;
using Sanakan.Config.Model;

namespace Sanakan.Api
{
    public class AppKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "AppKey";
        public const string HeaderName = "x-app-key";
        public const string AppClaim = "App";
        public const string PermissionClaim = "AppPermission";

        private readonly IConfig _config;

        public AppKeyAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
            UrlEncoder encoder, ISystemClock clock, IConfig config) : base(options, logger, encoder, clock)
        {
            _config = config;
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(HeaderName, out var values))
                return Task.FromResult(AuthenticateResult.NoResult());

            var app = FindApp(_config, values.ToString());
            if (app == null)
                return Task.FromResult(AuthenticateResult.Fail("App key is invalid"));

            var claims = new List<Claim> { new Claim(AppClaim, app.Bearer) };
            claims.AddRange(app.Permissions.ToString().Split(", ").Select(x => new Claim(PermissionClaim, x)));

            // aplikacja z uprawnieniem Site jest traktowana jak strona (np. dostaje tokeny użytkowników)
            if (app.Has(ApiAppPermission.Site))
                claims.Add(new Claim(ClaimTypes.Webpage, app.Bearer));

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }

        public static bool IsAllowed(ClaimsPrincipal user, ApiAppPermission permission)
        {
            if (user.HasClaim(c => c.Type == "Player")) return false;

            // token strony ma pierwszeństwo przed kluczem aplikacji
            if (user.Identities.Any(x => x.IsAuthenticated && x.AuthenticationType != SchemeName)) return true;

            return user.HasClaim(PermissionClaim, permission.ToString())
                || user.HasClaim(PermissionClaim, nameof(ApiAppPermission.Site));
        }

        public static ApiApp FindApp(IConfig config, string appKey)
        {
            if (string.IsNullOrEmpty(appKey)) return null;

            var provided = Encoding.UTF8.GetBytes(appKey);
            return config.Get().UserKeyApps?.FirstOrDefault(x => x.Key != null
                && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(x.Key), provided));
        }
    }
}
