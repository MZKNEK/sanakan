using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Sanakan.Api;
using Sanakan.Api.Controllers;
using Sanakan.Config.Model;
using Sanakan.Services.Time;
using Xunit;

namespace Artifacts
{
    public class ApiAppConfigTests
    {
        // config jest czytany zwykłym JsonSerializerem (JsonFileReader), więc tak samo tu
        private static ConfigModel Read(string json)
        {
            using var reader = new JsonTextReader(new StringReader(json));
            return new JsonSerializer().Deserialize<ConfigModel>(reader);
        }

        [Fact]
        public void EntryWithoutPermissions_DefaultsToUserKeys()
        {
            var config = Read("{\"UserKeyApps\":[{\"Key\":\"k\",\"Bearer\":\"old-app\"}]}");

            Assert.Equal(ApiAppPermission.UserKeys, config.UserKeyApps.Single().Permissions);
        }

        [Fact]
        public void Permissions_AreReadFromNames()
        {
            var config = Read("{\"UserKeyApps\":[{\"Key\":\"k\",\"Bearer\":\"app\",\"Permissions\":\"Info, Site\"}]}");

            Assert.Equal(ApiAppPermission.Info | ApiAppPermission.Site, config.UserKeyApps.Single().Permissions);
        }

        [Fact]
        public void Permissions_AreSavedAsNames()
        {
            var json = JsonConvert.SerializeObject(new ApiApp { Key = "k", Bearer = "app", Permissions = ApiAppPermission.UserKeys | ApiAppPermission.Info });

            Assert.Contains("\"Permissions\":\"UserKeys, Info\"", json);
        }

        [Fact]
        public void Permissions_SurviveSaveAndRead()
        {
            var model = new ConfigModel { UserKeyApps = new() { new ApiApp { Key = "k", Bearer = "app", Permissions = ApiAppPermission.None } } };

            var read = Read(JsonConvert.SerializeObject(model));

            Assert.Equal(ApiAppPermission.None, read.UserKeyApps.Single().Permissions);
        }

        [Theory]
        [InlineData(ApiAppPermission.UserKeys, ApiAppPermission.UserKeys, true)]
        [InlineData(ApiAppPermission.UserKeys, ApiAppPermission.Info, false)]
        [InlineData(ApiAppPermission.Info | ApiAppPermission.Site, ApiAppPermission.Site, true)]
        [InlineData(ApiAppPermission.None, ApiAppPermission.UserKeys, false)]
        public void Has_ChecksSingleFlag(ApiAppPermission permissions, ApiAppPermission check, bool expected)
        {
            Assert.Equal(expected, new ApiApp { Permissions = permissions }.Has(check));
        }
    }

    public class AppKeyHandlerTests
    {
        private static async Task<AuthenticateResult> AuthenticateAsync(FakeConfig config, string appKey)
        {
            var options = new Mock<IOptionsMonitor<AuthenticationSchemeOptions>>();
            options.Setup(x => x.Get(It.IsAny<string>())).Returns(new AuthenticationSchemeOptions());

            var handler = new AppKeyAuthenticationHandler(options.Object, NullLoggerFactory.Instance,
                UrlEncoder.Default, new SystemClock(), config);

            var context = new DefaultHttpContext();
            if (appKey != null) context.Request.Headers[AppKeyAuthenticationHandler.HeaderName] = appKey;

            var scheme = new AuthenticationScheme(AppKeyAuthenticationHandler.SchemeName, null, typeof(AppKeyAuthenticationHandler));
            await handler.InitializeAsync(scheme, context);
            return await handler.AuthenticateAsync();
        }

        private static FakeConfig Config(ApiAppPermission permissions)
        {
            var config = new FakeConfig();
            config.Model.UserKeyApps.Add(new ApiApp { Key = "app-key", Bearer = "app", Permissions = permissions });
            return config;
        }

        [Fact]
        public async Task NoHeader_ReturnsNoResult()
        {
            var result = await AuthenticateAsync(Config(ApiAppPermission.Info), null);

            Assert.True(result.None);
        }

        [Theory]
        [InlineData("")]
        [InlineData("wrong")]
        [InlineData("app")]
        public async Task InvalidKey_Fails(string key)
        {
            var result = await AuthenticateAsync(Config(ApiAppPermission.Info), key);

            Assert.False(result.Succeeded);
            Assert.NotNull(result.Failure);
        }

        [Fact]
        public async Task SiteApiKey_IsNotAcceptedAsAppKey()
        {
            var config = new FakeConfig();
            config.Model.ApiKeys.Add(new SanakanApiKey { Key = "site-key", Bearer = "site" });

            Assert.False((await AuthenticateAsync(config, "site-key")).Succeeded);
        }

        [Fact]
        public async Task ValidKey_HasAppAndPermissionClaims()
        {
            var result = await AuthenticateAsync(Config(ApiAppPermission.UserKeys | ApiAppPermission.Info), "app-key");

            Assert.True(result.Succeeded);
            var user = result.Principal;
            Assert.True(user.HasClaim(AppKeyAuthenticationHandler.AppClaim, "app"));
            Assert.True(user.HasClaim(AppKeyAuthenticationHandler.PermissionClaim, "UserKeys"));
            Assert.True(user.HasClaim(AppKeyAuthenticationHandler.PermissionClaim, "Info"));
            Assert.False(user.HasClaim(AppKeyAuthenticationHandler.PermissionClaim, "Site"));
            Assert.False(user.HasClaim(c => c.Type == "Player"));
        }

        [Fact]
        public async Task SitePermission_ActsAsWebpage()
        {
            var result = await AuthenticateAsync(Config(ApiAppPermission.Site), "app-key");

            Assert.True(result.Principal.HasClaim(ClaimTypes.Webpage, "app"));
        }

        [Fact]
        public async Task WithoutSitePermission_IsNotWebpage()
        {
            var result = await AuthenticateAsync(Config(ApiAppPermission.UserKeys | ApiAppPermission.Info), "app-key");

            Assert.False(result.Principal.HasClaim(c => c.Type == ClaimTypes.Webpage));
        }

        [Fact]
        public void FindApp_IgnoresEntriesWithoutKeyAndMissingSection()
        {
            var config = new FakeConfig();
            config.Model.UserKeyApps.Add(new ApiApp { Key = null, Bearer = "broken" });
            config.Model.UserKeyApps.Add(new ApiApp { Key = "good", Bearer = "app" });

            Assert.Equal("app", AppKeyAuthenticationHandler.FindApp(config, "good")?.Bearer);
            Assert.Null(AppKeyAuthenticationHandler.FindApp(config, null));

            config.Model.UserKeyApps = null;
            Assert.Null(AppKeyAuthenticationHandler.FindApp(config, "good"));
        }
    }

    public class AppKeyPolicyTests
    {
        private static ClaimsIdentity Site() => new ClaimsIdentity(new[] { new Claim(ClaimTypes.Webpage, "Shinden") }, "AuthenticationTypes.Federation");
        private static ClaimsIdentity PlayerToken() => new ClaimsIdentity(new[] { new Claim("DiscordId", "1"), new Claim("Player", "waifu_player") }, "AuthenticationTypes.Federation");
        private static ClaimsIdentity App(params string[] permissions) => new ClaimsIdentity(
            new[] { new Claim(AppKeyAuthenticationHandler.AppClaim, "app") }.Concat(permissions.Select(x => new Claim(AppKeyAuthenticationHandler.PermissionClaim, x))),
            AppKeyAuthenticationHandler.SchemeName);

        private static bool Allowed(ApiAppPermission permission, params ClaimsIdentity[] identities)
            => AppKeyAuthenticationHandler.IsAllowed(new ClaimsPrincipal(identities), permission);

        [Theory]
        [InlineData(ApiAppPermission.Site)]
        [InlineData(ApiAppPermission.Info)]
        public void SiteToken_IsAllowed(ApiAppPermission permission) => Assert.True(Allowed(permission, Site()));

        [Theory]
        [InlineData(ApiAppPermission.Site)]
        [InlineData(ApiAppPermission.Info)]
        public void PlayerToken_IsDenied(ApiAppPermission permission) => Assert.False(Allowed(permission, PlayerToken()));

        [Fact]
        public void InfoApp_OnlyInfo()
        {
            Assert.True(Allowed(ApiAppPermission.Info, App("Info")));
            Assert.False(Allowed(ApiAppPermission.Site, App("Info")));
        }

        [Fact]
        public void SiteApp_IncludesInfo()
        {
            Assert.True(Allowed(ApiAppPermission.Site, App("Site")));
            Assert.True(Allowed(ApiAppPermission.Info, App("Site")));
        }

        [Theory]
        [InlineData(ApiAppPermission.Site)]
        [InlineData(ApiAppPermission.Info)]
        public void UserKeysApp_IsDenied(ApiAppPermission permission) => Assert.False(Allowed(permission, App("UserKeys")));

        [Fact]
        public void SiteToken_WinsOverAppWithoutPermission()
        {
            Assert.True(Allowed(ApiAppPermission.Site, Site(), App("UserKeys")));
            Assert.True(Allowed(ApiAppPermission.Info, App("None"), Site()));
        }

        [Fact]
        public void PlayerToken_IsDeniedEvenWithSiteApp()
        {
            Assert.False(Allowed(ApiAppPermission.Site, PlayerToken(), App("Site")));
        }

        [Fact]
        public void Audit_ReportsAppName()
        {
            Assert.Equal("app:app", ApiAudit.GetClient(new ClaimsPrincipal(App("Site"))));
        }
    }

    public class UserKeyControllerPermissionTests
    {
        private static UserKeyController Controller(ApiAppPermission permissions)
        {
            var config = new FakeConfig();
            config.Model.UserKeyApps.Add(new ApiApp { Key = "app-key", Bearer = "app", Permissions = permissions });
            return new UserKeyController(config, new FixedTime())
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
            };
        }

        [Theory]
        [InlineData(ApiAppPermission.None)]
        [InlineData(ApiAppPermission.Info)]
        [InlineData(ApiAppPermission.Site)]
        [InlineData(ApiAppPermission.Info | ApiAppPermission.Site)]
        public async Task AppWithoutUserKeys_CannotGenerateOrRevoke(ApiAppPermission permissions)
        {
            Assert.Equal(403, (await Controller(permissions).GenerateUserKeyAsync(1, "app-key") as ObjectResult)?.StatusCode);
            Assert.Equal(403, (await Controller(permissions).RevokeUserKeyAsync(1, "app-key") as ObjectResult)?.StatusCode);
        }
    }

    // polityki Site/Info na prawdziwym pipeline (bez bazy i bez połączenia z Discordem)
    public class AppKeyApiTests : IDisposable
    {
        private const string PrivateCommands = "/api/info/commands/private";
        private const string Permissions = "/api/user/discord/1/permissions";

        private readonly ApiServer _api = new ApiServer();

        public AppKeyApiTests()
        {
            _api.Config.Model.UserKeyApps.Add(new ApiApp { Key = "userkeys-app", Bearer = "uk", Permissions = ApiAppPermission.UserKeys });
            _api.Config.Model.UserKeyApps.Add(new ApiApp { Key = "info-app", Bearer = "info", Permissions = ApiAppPermission.Info });
            _api.Config.Model.UserKeyApps.Add(new ApiApp { Key = "site-app", Bearer = "site", Permissions = ApiAppPermission.Site });
        }

        public void Dispose() => _api.Dispose();

        private HttpClient WithAppKey(string key)
        {
            _api.Client.DefaultRequestHeaders.Add(AppKeyAuthenticationHandler.HeaderName, key);
            return _api.Client;
        }

        private HttpClient AsPlayer()
        {
            var token = UserTokenBuilder.BuildUserToken(_api.Config, new Sanakan.Database.Models.User { Id = 1 }, new SystemTime());
            _api.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
            return _api.Client;
        }

        [Theory]
        [InlineData(PrivateCommands)]
        [InlineData(Permissions)]
        public async Task InfoEndpoint_WithoutAuth_Returns401(string url)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await _api.Client.GetAsync(url)).StatusCode);
        }

        [Fact]
        public async Task InfoEndpoint_InvalidAppKey_Returns401()
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await WithAppKey("wrong").GetAsync(PrivateCommands)).StatusCode);
        }

        [Fact]
        public async Task PrivateCommands_SiteToken_ReturnsModules()
        {
            var res = await (await _api.AsSiteAsync()).GetAsync(PrivateCommands);

            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            Assert.NotNull(JObject.Parse(await res.Content.ReadAsStringAsync())["modules"]);
        }

        [Theory]
        [InlineData("info-app")]
        [InlineData("site-app")]
        public async Task PrivateCommands_AppWithInfoOrSite_Returns200(string key)
        {
            Assert.Equal(HttpStatusCode.OK, (await WithAppKey(key).GetAsync(PrivateCommands)).StatusCode);
        }

        [Fact]
        public async Task PrivateCommands_UserKeysApp_Returns403()
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await WithAppKey("userkeys-app").GetAsync(PrivateCommands)).StatusCode);
        }

        [Fact]
        public async Task PrivateCommands_PlayerToken_Returns403()
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await AsPlayer().GetAsync(PrivateCommands)).StatusCode);
        }

        [Fact]
        public async Task PrivateCommands_PlayerTokenWithSiteApp_Returns403()
        {
            AsPlayer();
            Assert.Equal(HttpStatusCode.Forbidden, (await WithAppKey("site-app").GetAsync(PrivateCommands)).StatusCode);
        }

        [Theory]
        [InlineData("userkeys-app")]
        [InlineData("wrong")]
        public async Task SiteToken_WinsOverAppKey(string key)
        {
            await _api.AsSiteAsync();
            Assert.Equal(HttpStatusCode.OK, (await WithAppKey(key).GetAsync(PrivateCommands)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await _api.Client.GetAsync("/api/richmessage")).StatusCode);
        }

        // bot nie jest połączony z Discordem, więc 404 o serwerze oznacza, że polityka przepuściła zapytanie
        [Theory]
        [InlineData("info-app")]
        [InlineData("site-app")]
        public async Task Permissions_AppWithInfoOrSite_PassesPolicy(string key)
        {
            var res = await WithAppKey(key).GetAsync(Permissions + "?guildId=5");

            Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
            Assert.Equal("Guild not found!", (string)JObject.Parse(await res.Content.ReadAsStringAsync())["message"]);
        }

        [Fact]
        public async Task Permissions_SiteToken_PassesPolicy()
        {
            Assert.Equal(HttpStatusCode.NotFound, (await (await _api.AsSiteAsync()).GetAsync(Permissions)).StatusCode);
        }

        [Fact]
        public async Task Permissions_UserKeysApp_Returns403()
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await WithAppKey("userkeys-app").GetAsync(Permissions)).StatusCode);
        }

        [Fact]
        public async Task SiteEndpoint_SiteApp_Works()
        {
            var client = WithAppKey("site-app");

            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/richmessage")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/waifu/cards/character/5/update", ApiServer.Json("{}"))).StatusCode);
            Assert.Single(_api.Executor.Added);
        }

        [Theory]
        [InlineData("info-app")]
        [InlineData("userkeys-app")]
        public async Task SiteEndpoint_AppWithoutSite_Returns403(string key)
        {
            var client = WithAppKey(key);

            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/richmessage")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/waifu/cards/character/5/update", ApiServer.Json("{}"))).StatusCode);
            Assert.Empty(_api.Executor.Added);
        }

        [Fact]
        public async Task SiteEndpoint_InvalidAppKey_Returns401()
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await WithAppKey("wrong").GetAsync("/api/richmessage")).StatusCode);
        }

        [Fact]
        public async Task PlayerEndpoint_DoesNotAcceptAppKey()
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await WithAppKey("site-app").GetAsync("/api/userkey/me")).StatusCode);
        }
    }
}
