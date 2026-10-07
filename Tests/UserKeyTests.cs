using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sanakan.Api;
using Sanakan.Api.Controllers;
using Sanakan.Config.Model;
using Xunit;

namespace Artifacts
{
    public class UserKeyGenerationTests
    {
        [Fact]
        public void GenerateKey_HasPrefixAndUrlSafeBody()
        {
            var key = UserKeyAuthenticationHandler.GenerateKey();

            Assert.StartsWith("snk_", key);
            Assert.Matches(new Regex("^snk_[A-Za-z0-9_-]{43}$"), key);
        }

        [Fact]
        public void GenerateKey_AppPrefix_IsDistinguishableFromUserKey()
        {
            var appKey = UserKeyAuthenticationHandler.GenerateKey(UserKeyAuthenticationHandler.AppKeyPrefix);

            Assert.Matches(new Regex("^snka_[A-Za-z0-9_-]{43}$"), appKey);
            Assert.DoesNotMatch(new Regex("^snk_"), appKey);
        }

        [Fact]
        public void GenerateKey_IsUnique()
        {
            var keys = Enumerable.Range(0, 2000).Select(_ => UserKeyAuthenticationHandler.GenerateKey()).ToList();
            Assert.Equal(keys.Count, keys.Distinct().Count());
        }

        [Fact]
        public void HashKey_IsDeterministicLowercaseSha256Hex()
        {
            var hash = UserKeyAuthenticationHandler.HashKey("snk_test");

            Assert.Equal(hash, UserKeyAuthenticationHandler.HashKey("snk_test"));
            Assert.Matches(new Regex("^[0-9a-f]{64}$"), hash);

            var expected = System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("snk_test"))).ToLowerInvariant();
            Assert.Equal(expected, hash);
        }

        [Fact]
        public void HashKey_DifferentKeysGiveDifferentHashes()
        {
            Assert.NotEqual(UserKeyAuthenticationHandler.HashKey("snk_a"), UserKeyAuthenticationHandler.HashKey("snk_b"));
        }

        [Fact]
        public void HashKey_FitsDatabaseColumn()
        {
            Assert.True(UserKeyAuthenticationHandler.HashKey(UserKeyAuthenticationHandler.GenerateKey()).Length <= 64);
        }
    }

    public class UserKeyHandlerTests
    {
        private static async Task<AuthenticateResult> AuthenticateAsync(HttpContext context)
        {
            var options = new Mock<IOptionsMonitor<AuthenticationSchemeOptions>>();
            options.Setup(x => x.Get(It.IsAny<string>())).Returns(new AuthenticationSchemeOptions());

            var handler = new UserKeyAuthenticationHandler(options.Object, NullLoggerFactory.Instance,
                UrlEncoder.Default, new SystemClock(), new FakeConfig());

            var scheme = new AuthenticationScheme(UserKeyAuthenticationHandler.SchemeName, null, typeof(UserKeyAuthenticationHandler));
            await handler.InitializeAsync(scheme, context);
            return await handler.AuthenticateAsync();
        }

        [Fact]
        public async Task NoHeader_ReturnsNoResult()
        {
            var result = await AuthenticateAsync(new DefaultHttpContext());

            Assert.True(result.None);
            Assert.False(result.Succeeded);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task EmptyHeader_Fails(string value)
        {
            var context = new DefaultHttpContext();
            context.Request.Headers[UserKeyAuthenticationHandler.HeaderName] = value;

            var result = await AuthenticateAsync(context);

            Assert.False(result.Succeeded);
            Assert.NotNull(result.Failure);
        }
    }

    public class UserKeyControllerTests
    {
        private static UserKeyController Controller(FakeConfig config, ClaimsPrincipal user = null)
        {
            return new UserKeyController(config, new FixedTime())
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = user ?? new ClaimsPrincipal() }
                }
            };
        }

        private static int? Status(IActionResult result) => (result as ObjectResult)?.StatusCode;

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task Generate_WithoutAppKey_Returns401(string appKey)
        {
            Assert.Equal(401, Status(await Controller(new FakeConfig()).GenerateUserKeyAsync(1, appKey)));
        }

        [Fact]
        public async Task Generate_WithWrongAppKey_Returns403()
        {
            var config = new FakeConfig();
            config.Model.UserKeyApps.Add(new ApiApp { Key = "good-key", Bearer = "app" });

            Assert.Equal(403, Status(await Controller(config).GenerateUserKeyAsync(1, "bad-key")));
        }

        [Fact]
        public async Task Generate_PrefixOfValidKey_Returns403()
        {
            var config = new FakeConfig();
            config.Model.UserKeyApps.Add(new ApiApp { Key = "good-key", Bearer = "app" });

            Assert.Equal(403, Status(await Controller(config).GenerateUserKeyAsync(1, "good")));
        }

        [Fact]
        public async Task Generate_WithoutUserKeyAppsSection_Returns403()
        {
            var config = new FakeConfig();
            config.Model.UserKeyApps = null;

            Assert.Equal(403, Status(await Controller(config).GenerateUserKeyAsync(1, "anything")));
        }

        [Fact]
        public async Task Generate_SiteApiKeyIsNotAcceptedAsAppKey()
        {
            var config = new FakeConfig();
            config.Model.ApiKeys.Add(new SanakanApiKey { Key = "site-key", Bearer = "site" });

            Assert.Equal(403, Status(await Controller(config).GenerateUserKeyAsync(1, "site-key")));
        }

        [Fact]
        public async Task Generate_EntryWithNullKeyIsIgnored()
        {
            var config = new FakeConfig();
            config.Model.UserKeyApps.Add(new ApiApp { Key = null, Bearer = "broken" });

            Assert.Equal(403, Status(await Controller(config).GenerateUserKeyAsync(1, "x")));
        }

        [Fact]
        public async Task Revoke_WithoutOrWrongAppKey_Returns401Or403()
        {
            var config = new FakeConfig();
            config.Model.UserKeyApps.Add(new ApiApp { Key = "good-key", Bearer = "app" });

            Assert.Equal(401, Status(await Controller(config).RevokeUserKeyAsync(1, null)));
            Assert.Equal(403, Status(await Controller(config).RevokeUserKeyAsync(1, "bad-key")));
        }

        [Fact]
        public void WhoAmI_ReturnsUserAndApplicationFromClaims()
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("DiscordId", "123456789012345678"),
                new Claim("Player", "waifu_player"),
                new Claim("UserKeyApp", "my-app"),
            }, UserKeyAuthenticationHandler.SchemeName));

            var result = Controller(new FakeConfig(), principal).WhoAmI() as OkObjectResult;

            Assert.NotNull(result);
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(result.Value);
            Assert.Contains("\"userId\":\"123456789012345678\"", json);
            Assert.Contains("\"application\":\"my-app\"", json);
        }

        [Fact]
        public void WhoAmI_WithoutDiscordIdClaim_Returns403()
        {
            Assert.Equal(403, Status(Controller(new FakeConfig()).WhoAmI()));
        }
    }

    public class TokenControllerTests
    {
        private static TokenController Controller(FakeConfig config, Sanakan.Api.ITokenAttemptGuard guard = null)
            => new TokenController(config, new FixedTime { Value = System.DateTime.UtcNow },
                guard ?? new Sanakan.Api.TokenAttemptGuard(new FixedTime { Value = System.DateTime.UtcNow }));

        private static FakeConfig SiteConfig()
        {
            var config = new FakeConfig();
            config.Model.ApiKeys = new List<SanakanApiKey>
            {
                new SanakanApiKey { Key = "site-key", Bearer = "site" },
            };
            return config;
        }

        private static int? Status(IActionResult result) => (result as ObjectResult)?.StatusCode;

        [Fact]
        public void ThreeFailedAttempts_LockClientFor24Hours()
        {
            var time = new FixedTime { Value = new System.DateTime(2026, 1, 1, 12, 0, 0) };
            var guard = new Sanakan.Api.TokenAttemptGuard(time);
            var controller = new TokenController(SiteConfig(), time, guard);

            for (int i = 0; i < Sanakan.Api.TokenAttemptGuard.MaxAttempts; i++)
                Assert.Equal(403, Status(controller.CreateToken("wrong")));

            // 4. próba - zablokowana, nawet z poprawnym kluczem
            Assert.Equal(429, Status(controller.CreateToken("wrong")));
            Assert.Equal(429, Status(controller.CreateToken("site-key")));

            // po wygaśnięciu blokady poprawny klucz znowu działa
            time.Value = time.Value.Add(Sanakan.Api.TokenAttemptGuard.LockDuration).AddMinutes(1);
            Assert.IsType<OkObjectResult>(controller.CreateToken("site-key"));
        }

        [Fact]
        public void SuccessfulAttempt_ResetsFailureCounter()
        {
            var time = new FixedTime { Value = new System.DateTime(2026, 1, 1, 12, 0, 0) };
            var guard = new Sanakan.Api.TokenAttemptGuard(time);
            var controller = new TokenController(SiteConfig(), time, guard);

            Assert.Equal(403, Status(controller.CreateToken("wrong")));
            Assert.Equal(403, Status(controller.CreateToken("wrong")));
            Assert.IsType<OkObjectResult>(controller.CreateToken("site-key"));

            // licznik wyzerowany - dwie kolejne pomyłki nie blokują
            Assert.Equal(403, Status(controller.CreateToken("wrong")));
            Assert.Equal(403, Status(controller.CreateToken("wrong")));
            Assert.IsType<OkObjectResult>(controller.CreateToken("site-key"));
        }

        [Fact]
        public void DifferentClients_HaveIndependentCounters()
        {
            var time = new FixedTime { Value = new System.DateTime(2026, 1, 1, 12, 0, 0) };
            var guard = new Sanakan.Api.TokenAttemptGuard(time);

            for (int i = 0; i < Sanakan.Api.TokenAttemptGuard.MaxAttempts; i++)
                guard.RegisterFailure("ip-a");

            Assert.True(guard.IsLocked("ip-a", out _));
            Assert.False(guard.IsLocked("ip-b", out _));

            guard.RegisterFailure("ip-b");
            Assert.False(guard.IsLocked("ip-b", out _));
        }

        [Fact]
        public void Guard_NullKey_IsSafe()
        {
            var guard = new Sanakan.Api.TokenAttemptGuard(new FixedTime());
            Assert.False(guard.IsLocked(null, out _));
            guard.RegisterFailure(null);
            guard.RegisterSuccess(null);
        }

        [Fact]
        public void EntryWithNullKey_DoesNotThrowAndOtherKeysWork()
        {
            var config = new FakeConfig();
            config.Model.ApiKeys = new List<SanakanApiKey>
            {
                new SanakanApiKey { Key = null, Bearer = "broken" },
                new SanakanApiKey { Key = "site-key", Bearer = "site" },
            };

            Assert.IsType<OkObjectResult>(Controller(config).CreateToken("site-key"));
            Assert.Equal(403, (Controller(config).CreateToken("wrong") as ObjectResult)?.StatusCode);
        }

        [Fact]
        public void NullApiKeysSection_Returns403()
        {
            var config = new FakeConfig();
            config.Model.ApiKeys = null;

            Assert.Equal(403, (Controller(config).CreateToken("x") as ObjectResult)?.StatusCode);
        }

        [Fact]
        public void MissingApiKey_Returns401()
        {
            Assert.Equal(401, (Controller(new FakeConfig()).CreateToken(null) as ObjectResult)?.StatusCode);
        }
    }
}
