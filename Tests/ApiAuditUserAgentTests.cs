using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Sanakan.Api;
using Xunit;

namespace Artifacts
{
    public class ApiAuditUserAgentTests : IDisposable
    {
        private readonly ApiServer _api = new ApiServer();

        public void Dispose() => _api.Dispose();

        [Theory]
        [InlineData(null, "none")]
        [InlineData("  ", "none")]
        [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/131.0", "browser")]
        [InlineData("Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)", "crawler")]
        [InlineData("curl/8.4.0", "curl/8.4.0")]
        [InlineData("python-requests/2.31.0 extra", "python-requests/2.31.0")]
        [InlineData("evil\n@everyone`x`", "evil??everyone?x?")]
        [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
        public void UserAgent_IsGroupedAndCut(string userAgent, string kind)
        {
            Assert.Equal(kind, ApiAudit.UserAgentKind(userAgent));
        }

        private static DefaultHttpContext Context(string method, string userAgent, bool audited, params Claim[] claims)
        {
            var ctx = new DefaultHttpContext();
            ctx.Request.Method = method;
            ctx.Request.Path = "/api/UserKey/discord/1";
            ctx.Request.Headers.UserAgent = userAgent;
            ctx.Response.StatusCode = 403;
            if (claims.Length > 0)
                ctx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
            if (audited)
                ctx.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(new AuditAnonymousAttribute()), "test"));
            return ctx;
        }

        [Fact]
        public void Client_OfAnonymousCall_HasItsUserAgent_KnownCallersStayAsTheyWere()
        {
            Assert.Equal("anonymous:curl/8.4.0", ApiAudit.GetClient(Context("GET", "curl/8.4.0", false)));
            Assert.Equal("site:Shinden", ApiAudit.GetClient(Context("GET", "curl/8.4.0", false, new Claim(ClaimTypes.Webpage, "Shinden"))));
        }

        [Fact]
        public void AuditedAnonymousEndpoint_IsLogged_WithTheAppOrTheUserAgent()
        {
            Assert.Equal("API: anonymous:curl/8.4.0 POST /api/UserKey/discord/1 -> 403 (5ms)", ApiAudit.Describe(Context("POST", "curl/8.4.0", true), 5));

            var withApp = Context("DELETE", "curl/8.4.0", true);
            withApp.Items[ApiAudit.AppItem] = "Shinden";
            Assert.Equal("API: app:Shinden DELETE /api/UserKey/discord/1 -> 403 (5ms)", ApiAudit.Describe(withApp, 5));

            Assert.Null(ApiAudit.Describe(Context("POST", "curl/8.4.0", false), 5));
            Assert.Null(ApiAudit.Describe(Context("GET", "curl/8.4.0", true), 5));
        }

        [Theory]
        [InlineData("POST")]
        [InlineData("DELETE")]
        public async Task UserKeyChange_WithoutAppKey_IsLoggedWithTheUserAgent(string method)
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), "/api/UserKey/discord/1");
            request.Headers.TryAddWithoutValidation("User-Agent", "curl/8.4.0");

            var res = await _api.Client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
            Assert.Contains($"API: anonymous:curl/8.4.0 {method} /api/UserKey/discord/1 -> 401", string.Join("\n", _api.Logger.Messages));
        }

        // an app key without the UserKeys permission is a real app, but not let in, so the caller stays unknown
        [Fact]
        public async Task UserKeyChange_WithAppWithoutPermission_IsLoggedAsAnonymous()
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/UserKey/discord/1");
            request.Headers.TryAddWithoutValidation("User-Agent", "curl/8.4.0");
            request.Headers.Add("x-app-key", ApiServer.SiteKey);

            var res = await _api.Client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
            Assert.Contains("API: anonymous:curl/8.4.0 POST /api/UserKey/discord/1 -> 403", string.Join("\n", _api.Logger.Messages));
        }
    }
}
