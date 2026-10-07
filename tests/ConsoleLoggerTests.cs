using System.Collections.Generic;
using Sanakan.Config.Model;
using Sanakan.Services;
using Xunit;

namespace Artifacts
{
    public class ConsoleLoggerTests
    {
        private static FakeConfig Config()
        {
            var config = new FakeConfig();
            config.Model.BotToken = "bot-token-ABCDEFGH.1234";
            config.Model.Shinden = new ConfigShinden { Token = "MOeLshindenFakeKey123" };
            config.Model.ApiKeys = new List<SanakanApiKey> { new SanakanApiKey { Key = "site-secret-key", Bearer = "site" } };
            return config;
        }

        [Fact]
        public void ShindenRequestUrl_ApiKeyIsMasked()
        {
            var logger = new ConsoleLogger();
            var msg = logger.MaskSecrets("Processing request: [GET] http://api.shinden.pl/api/character/flat-list-anime?api_key=FAKE-SHINDEN-KEY-0123456789&x=1");

            Assert.DoesNotContain("FAKE-SHINDEN-KEY-0123456789", msg);
            Assert.Contains("api_key=***&x=1", msg);
        }

        [Fact]
        public void SecretsFromConfig_AreMaskedAnywhereInMessage()
        {
            var logger = new ConsoleLogger(Config());
            var msg = logger.MaskSecrets("bot-token-ABCDEFGH.1234 | key MOeLshindenFakeKey123 | jwt local-test-signing-key-0123456789abcdef | site-secret-key");

            Assert.DoesNotContain("bot-token-ABCDEFGH.1234", msg);
            Assert.DoesNotContain("MOeLshindenFakeKey123", msg);
            Assert.DoesNotContain("local-test-signing-key", msg);
            Assert.DoesNotContain("site-secret-key", msg);
        }

        [Fact]
        public void AppKeyAddedAtRuntime_IsMasked()
        {
            var config = Config();
            var logger = new ConsoleLogger(config);

            config.Model.UserKeyApps = new List<ApiApp> { new ApiApp { Key = "runtime-app-secret", Bearer = "app" } };

            Assert.DoesNotContain("runtime-app-secret", logger.MaskSecrets("x runtime-app-secret y"));
        }

        [Theory]
        [InlineData("Server=db;User ID=sanakan;Password=hunter2pass;", "hunter2pass")]
        [InlineData("{\"token\":\"eyJhbGciOiJIUzI1NiJ9.abc\",\"expire\":1}", "eyJhbGciOiJIUzI1NiJ9")]
        [InlineData("apikey: abcdef123456", "abcdef123456")]
        [InlineData("SECRET=topsecretvalue", "topsecretvalue")]
        public void GenericSecretParameters_AreMasked(string message, string secret)
        {
            Assert.DoesNotContain(secret, new ConsoleLogger().MaskSecrets(message));
        }

        [Fact]
        public void SanakanUserAndAppKeys_AreMasked()
        {
            var logger = new ConsoleLogger();
            var userKey = Sanakan.Api.UserKeyAuthenticationHandler.GenerateKey();
            var appKey = Sanakan.Api.UserKeyAuthenticationHandler.GenerateKey(Sanakan.Api.UserKeyAuthenticationHandler.AppKeyPrefix);

            var msg = logger.MaskSecrets($"user {userKey} app {appKey}");

            Assert.DoesNotContain(userKey, msg);
            Assert.DoesNotContain(appKey, msg);
        }

        [Theory]
        [InlineData("Executor: running cmd-lazyp")]
        [InlineData("Run cmd: u303101521552211970 lazyp")]
        [InlineData("mem usage: 682 MiB")]
        [InlineData("Response code: 200")]
        public void OrdinaryMessages_AreUnchanged(string message)
        {
            Assert.Equal(message, new ConsoleLogger(Config()).MaskSecrets(message));
        }

        [Fact]
        public void ShortConfigValues_AreNotUsedAsSecrets()
        {
            var config = Config();
            config.Model.ApiKeys.Add(new SanakanApiKey { Key = "a", Bearer = "x" });

            Assert.Equal("a cat sat", new ConsoleLogger(config).MaskSecrets("a cat sat"));
        }

        [Fact]
        public void NullOrEmptyMessage_IsReturnedAsIs()
        {
            var logger = new ConsoleLogger(Config());
            Assert.Null(logger.MaskSecrets(null));
            Assert.Equal("", logger.MaskSecrets(""));
        }
    }
}
