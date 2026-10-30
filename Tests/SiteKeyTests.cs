using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Sanakan.Api;
using Sanakan.Config.Model;
using Sanakan.Services.Time;
using Xunit;

namespace Artifacts
{
    public class LegacySiteKeysTests
    {
        private static ConfigModel Read(string json)
        {
            using var reader = new JsonTextReader(new StringReader(json));
            return new JsonSerializer().Deserialize<ConfigModel>(reader);
        }

        [Fact]
        public void NoLegacySection_NothingToDo()
        {
            var config = new ConfigModel { UserKeyApps = new List<ApiApp>() };

            Assert.Null(LegacySiteKeys.Migrate(config));
            Assert.Empty(config.UserKeyApps);
        }

        [Fact]
        public void Entries_BecomeSiteApps_WithSameKeys()
        {
            var config = Read("{\"ApiKeys\":[{\"Key\":\"k1\",\"Bearer\":\"Shinden\"},{\"Key\":\"k2\",\"Bearer\":\"Other\"}],"
                + "\"UserKeyApps\":[{\"Key\":\"app\",\"Bearer\":\"app\"}]}");

            Assert.Equal(new[] { "Shinden", "Other" }, LegacySiteKeys.Migrate(config));

            Assert.Null(config.ApiKeys);
            Assert.Equal(3, config.UserKeyApps.Count);
            Assert.Equal(ApiAppPermission.UserKeys, config.UserKeyApps.Single(x => x.Bearer == "app").Permissions);

            var shinden = config.UserKeyApps.Single(x => x.Bearer == "Shinden");
            Assert.Equal("k1", shinden.Key);
            Assert.Equal(ApiAppPermission.Site, shinden.Permissions);
            Assert.Equal("k2", config.UserKeyApps.Single(x => x.Bearer == "Other").Key);
        }

        [Fact]
        public void MissingUserKeyAppsSection_IsCreated()
        {
            var config = Read("{\"ApiKeys\":[{\"Key\":\"k1\",\"Bearer\":\"Shinden\"}]}");

            LegacySiteKeys.Migrate(config);

            Assert.Equal("Shinden", config.UserKeyApps.Single().Bearer);
        }

        [Fact]
        public void NameTakenByOtherApp_GetsSuffix()
        {
            var config = new ConfigModel
            {
                ApiKeys = new List<SanakanApiKey>
                {
                    new SanakanApiKey { Key = "k1", Bearer = "Shinden" },
                    new SanakanApiKey { Key = "k2", Bearer = "Shinden" },
                },
                UserKeyApps = new List<ApiApp> { new ApiApp { Key = "app", Bearer = "Shinden" } },
            };

            Assert.Equal(new[] { "Shinden-site", "Shinden-site2" }, LegacySiteKeys.Migrate(config));

            Assert.Equal(ApiAppPermission.UserKeys, config.UserKeyApps.Single(x => x.Key == "app").Permissions);
            Assert.Equal("Shinden-site", config.UserKeyApps.Single(x => x.Key == "k1").Bearer);
            Assert.Equal("Shinden-site2", config.UserKeyApps.Single(x => x.Key == "k2").Bearer);
        }

        [Fact]
        public void KeyAlreadyUsedByApp_AddsSitePermissionInsteadOfDuplicate()
        {
            var config = new ConfigModel
            {
                ApiKeys = new List<SanakanApiKey> { new SanakanApiKey { Key = "same", Bearer = "Shinden" } },
                UserKeyApps = new List<ApiApp> { new ApiApp { Key = "same", Bearer = "strona", Permissions = ApiAppPermission.UserKeys } },
            };

            Assert.Equal(new[] { "strona" }, LegacySiteKeys.Migrate(config));

            Assert.Equal(ApiAppPermission.UserKeys | ApiAppPermission.Site, config.UserKeyApps.Single().Permissions);
        }

        [Fact]
        public void EntriesWithoutKeyOrName_AreHandled()
        {
            var config = new ConfigModel
            {
                ApiKeys = new List<SanakanApiKey> { null, new SanakanApiKey { Key = null, Bearer = "broken" }, new SanakanApiKey { Key = "k" } },
            };

            Assert.Equal(new[] { "site" }, LegacySiteKeys.Migrate(config));
            Assert.Equal("k", config.UserKeyApps.Single().Key);
        }

        [Fact]
        public void EmptyLegacySection_IsRemoved()
        {
            var config = new ConfigModel { ApiKeys = new List<SanakanApiKey>() };

            Assert.Empty(LegacySiteKeys.Migrate(config));
            Assert.Null(config.ApiKeys);
        }

        [Fact]
        public void SecondRun_DoesNothing()
        {
            var config = Read("{\"ApiKeys\":[{\"Key\":\"k1\",\"Bearer\":\"Shinden\"}]}");
            LegacySiteKeys.Migrate(config);

            var saved = Read(JsonConvert.SerializeObject(config));

            Assert.Null(LegacySiteKeys.Migrate(saved));
            Assert.Equal(ApiAppPermission.Site, saved.UserKeyApps.Single().Permissions);
        }

        [Fact]
        public void GeneratedJwtKey_PassesConfigValidation()
        {
            var config = new ConfigModel { Jwt = new JwtConfig { Key = BotWebHost.GenerateJwtKey(), Issuer = "x" } };

            BotWebHost.ValidateConfig(config);
            Assert.NotEqual(config.Jwt.Key, BotWebHost.GenerateJwtKey());
        }

        [Fact]
        public void WeakAppKeys_ListsOnlyShortKeys()
        {
            var config = new ConfigModel
            {
                UserKeyApps = new List<ApiApp>
                {
                    new ApiApp { Bearer = "old", Key = "short-key" },
                    new ApiApp { Bearer = "generated", Key = UserKeyAuthenticationHandler.GenerateKey(UserKeyAuthenticationHandler.AppKeyPrefix) },
                    new ApiApp { Bearer = "empty", Key = null },
                    null,
                },
            };

            Assert.Equal(new[] { "old" }, BotWebHost.WeakAppKeys(config));
            Assert.Empty(BotWebHost.WeakAppKeys(new ConfigModel()));
        }
    }

    // key management on the real pipeline: changes in config must apply without a restart
    public class SiteKeyRotationApiTests : IDisposable
    {
        private readonly ApiServer _api = new ApiServer();

        public void Dispose() => _api.Dispose();

        private async Task<HttpStatusCode> TokenStatusAsync(string key)
            => (await _api.Client.PostAsync("/api/token", ApiServer.Json($"\"{key}\""))).StatusCode;

        [Fact]
        public async Task MigratedLegacyKey_StillWorks()
        {
            _api.Config.Model.UserKeyApps.Clear();
            _api.Config.Model.ApiKeys = new List<SanakanApiKey> { new SanakanApiKey { Key = ApiServer.SiteKey, Bearer = "Shinden" } };

            LegacySiteKeys.Migrate(_api.Config.Model);

            Assert.Equal(HttpStatusCode.OK, await TokenStatusAsync(ApiServer.SiteKey));
        }

        [Fact]
        public async Task ChangedAppKey_OldStopsWorking_NewWorks()
        {
            var app = _api.Config.Model.UserKeyApps.Single(x => x.Bearer == "Shinden");
            app.Key = UserKeyAuthenticationHandler.GenerateKey(UserKeyAuthenticationHandler.AppKeyPrefix);

            Assert.Equal(HttpStatusCode.Forbidden, await TokenStatusAsync(ApiServer.SiteKey));
            Assert.Equal(HttpStatusCode.OK, await TokenStatusAsync(app.Key));
        }

        [Fact]
        public async Task SitePermissionRemoved_StopsWorking()
        {
            _api.Config.Model.UserKeyApps.Single(x => x.Bearer == "Shinden").Permissions ^= ApiAppPermission.Site;

            Assert.Equal(HttpStatusCode.Forbidden, await TokenStatusAsync(ApiServer.SiteKey));
        }

        [Fact]
        public async Task JwtRotation_InvalidatesIssuedSiteTokens()
        {
            var client = await _api.AsSiteAsync();
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/richmessage")).StatusCode);

            _api.Config.Model.Jwt.Key = BotWebHost.GenerateJwtKey();

            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/richmessage")).StatusCode);

            await _api.AsSiteAsync();
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/richmessage")).StatusCode);
        }

        [Fact]
        public async Task JwtRotation_InvalidatesIssuedPlayerTokens()
        {
            var token = UserTokenBuilder.BuildUserToken(_api.Config, new Sanakan.Database.Models.User { Id = 1 }, new SystemTime());
            _api.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

            // 403 instead of 401 means the token was accepted and only the policy rejected it
            Assert.Equal(HttpStatusCode.Forbidden, (await _api.Client.GetAsync("/api/richmessage")).StatusCode);

            _api.Config.Model.Jwt.Key = BotWebHost.GenerateJwtKey();

            Assert.Equal(HttpStatusCode.Unauthorized, (await _api.Client.GetAsync("/api/richmessage")).StatusCode);
        }
    }
}
