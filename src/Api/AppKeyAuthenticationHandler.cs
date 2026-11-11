#pragma warning disable 1591

using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Net.Http.Headers;
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
        private readonly ITokenAttemptGuard _guard;

        public AppKeyAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
            UrlEncoder encoder, IConfig config, ITokenAttemptGuard guard) : base(options, logger, encoder)
        {
            _config = config;
            _guard = guard;
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(HeaderName, out var values))
                return AuthenticateResult.NoResult();

            // a valid site token wins anyway, so a stale app key sent along with it must not count as a guess
            if (Request.Headers.ContainsKey(HeaderNames.Authorization)
                && (await Context.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme)).Succeeded)
                return AuthenticateResult.NoResult();

            var clientKey = TokenAttemptGuard.ClientKey(Context);
            if (_guard.IsLocked(clientKey, out _))
                return AuthenticateResult.Fail("Too many failed attempts");

            // success does not reset the counter here, otherwise one known key would allow guessing the others
            var app = FindApp(_config, values.ToString());
            if (app == null)
            {
                _guard.RegisterFailure(clientKey);
                return AuthenticateResult.Fail("App key is invalid");
            }

            var claims = new List<Claim> { new Claim(AppClaim, app.Bearer) };
            claims.AddRange(app.Permissions.ToString().Split(", ").Select(x => new Claim(PermissionClaim, x)));

            // aplikacja z uprawnieniem Site jest traktowana jak strona (np. dostaje tokeny użytkowników)
            if (app.Has(ApiAppPermission.Site))
                claims.Add(new Claim(ClaimTypes.Webpage, app.Bearer));

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
            return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
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
