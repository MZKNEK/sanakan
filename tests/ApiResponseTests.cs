using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Discord.WebSocket;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json.Linq;
using Sanakan.Api;
using Sanakan.Config.Model;
using Sanakan.Services.Executor;
using Sanakan.Services.PocketWaifu;
using Sanakan.Services.Time;
using Xunit;

namespace Artifacts
{
    // prawdziwy pipeline API (JWT, polityki, routing, formattery) na TestServerze, bez bazy i internetu
    public sealed class ApiServer : IDisposable
    {
        public const string SiteKey = "site-key-for-tests-0123456789";

        public FakeConfig Config { get; } = new FakeConfig();
        public FakeExecutor Executor { get; } = new FakeExecutor();
        public HttpClient Client { get; }
        private readonly IHost _host;
        private readonly LocalHttpServer _shindenServer;

        public ApiServer()
        {
            Config.Model.ApiKeys = new List<SanakanApiKey> { new SanakanApiKey { Key = SiteKey, Bearer = "Shinden" } };
            Config.Model.RMConfig = new List<RichMessageConfig>();

            var logger = new ListLogger();
            // JWT jest sprawdzany względem prawdziwego zegara, więc token musi być wystawiony "teraz"
            var time = new SystemTime();
            // lokalny "shinden", który na wszystko odpowiada 404
            _shindenServer = new LocalHttpServer(ctx =>
            {
                ctx.Response.StatusCode = 404;
                return Task.CompletedTask;
            });
            var shinden = new Shinden.ShindenClient(new Shinden.Auth("token", "tests", "marmolade"), logger, Shinden.Logger.LogLevel.Information,
                _shindenServer.BaseUrl, TimeSpan.FromSeconds(5));
            var waifu = new Waifu(null, shinden, null, logger, null, null, null, time, null, null, Config);

            _host = new HostBuilder().ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    BotWebHost.AddApiServices(services, Config);
                    services.AddSingleton((TagHelper)RuntimeHelpers.GetUninitializedObject(typeof(TagHelper)));
                    services.AddSingleton<ISystemTime>(time);
                    services.AddSingleton(waifu);
                    services.AddSingleton<Shinden.Logger.ILogger>(logger);
                    services.AddSingleton(new DiscordSocketClient());
                    services.AddSingleton(new Sanakan.Services.Helper(Config, logger));
                    services.AddSingleton(shinden);
                    services.AddSingleton<IExecutor>(Executor);
                    services.AddSingleton(new Expedition(time));
                })
                .Configure(BotWebHost.UseApi)).Start();

            Client = _host.GetTestClient();
        }

        public async Task<HttpClient> AsSiteAsync()
        {
            var res = await Client.PostAsync("/api/token", Json($"\"{SiteKey}\""));
            res.EnsureSuccessStatusCode();
            var token = JObject.Parse(await res.Content.ReadAsStringAsync())["token"].ToString();
            Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return Client;
        }

        public static StringContent Json(string body) => new StringContent(body, Encoding.UTF8, "application/json");

        public void Dispose()
        {
            Client.Dispose();
            _host.Dispose();
            _shindenServer.Dispose();
        }
    }

    public class ApiResponseTests : IDisposable
    {
        private readonly ApiServer _api = new ApiServer();

        public void Dispose() => _api.Dispose();

        // odpowiedź musi dojść w całości jako JSON z komunikatem, a nie zostać zerwana przez drugi zapis
        private static async Task<JObject> AssertMessage(HttpResponseMessage res, HttpStatusCode code)
        {
            Assert.Equal(code, res.StatusCode);
            var json = JObject.Parse(await res.Content.ReadAsStringAsync());
            Assert.False(string.IsNullOrEmpty((string)json["message"]));
            Assert.Equal((int)code >= 200 && (int)code < 300, (bool)json["success"]);
            return json;
        }

        [Fact]
        public async Task Token_InvalidKey_Returns403()
        {
            await AssertMessage(await _api.Client.PostAsync("/api/token", ApiServer.Json("\"wrong\"")), HttpStatusCode.Forbidden);
        }

        [Fact]
        public async Task SiteEndpoint_WithoutToken_Returns401()
        {
            var res = await _api.Client.PostAsync("/api/waifu/shinden/5/boosterpack/open", ApiServer.Json("[]"));
            Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        }

        [Fact]
        public async Task InfoCommands_TypedSuccess_ReturnsBody()
        {
            var res = await _api.Client.GetAsync("/api/info/commands");

            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var json = JObject.Parse(await res.Content.ReadAsStringAsync());
            Assert.NotNull(json["modules"] ?? json["Modules"]);
        }

        [Fact]
        public async Task OpenShindenPacks_EmptyList_Returns500Json()
        {
            var client = await _api.AsSiteAsync();
            await AssertMessage(await client.PostAsync("/api/waifu/shinden/5/boosterpack/open", ApiServer.Json("[]")), HttpStatusCode.InternalServerError);
        }

        [Fact]
        public async Task GiveShindenPacks_EmptyList_Returns500Json()
        {
            var client = await _api.AsSiteAsync();
            await AssertMessage(await client.PostAsync("/api/waifu/shinden/5/boosterpack", ApiServer.Json("[]")), HttpStatusCode.InternalServerError);
        }

        [Fact]
        public async Task GiveDiscordPacks_EmptyList_Returns500Json()
        {
            var client = await _api.AsSiteAsync();
            await AssertMessage(await client.PostAsync("/api/waifu/discord/5/boosterpack", ApiServer.Json("[]")), HttpStatusCode.InternalServerError);
        }

        [Fact]
        public async Task ShindenUsername_WhenShindenReturns404_Returns404Json()
        {
            await AssertMessage(await _api.Client.GetAsync("/api/user/shinden/5/username"), HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task FindUser_WhenShindenReturns404_Returns404Json()
        {
            await AssertMessage(await _api.Client.PostAsync("/api/user/find", ApiServer.Json("\"karna\"")), HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task RepairCards_WhenShindenReturns404_Returns500Json()
        {
            var client = await _api.AsSiteAsync();
            await AssertMessage(await client.PostAsync("/api/waifu/character/repair/1/2", null), HttpStatusCode.InternalServerError);
        }

        [Fact]
        public async Task UpdateCards_QueueFull_Returns503Json()
        {
            _api.Executor.Accept = false;
            var client = await _api.AsSiteAsync();
            await AssertMessage(await client.PostAsync("/api/waifu/cards/character/5/update", ApiServer.Json("{}")), (HttpStatusCode)503);
        }

        [Fact]
        public async Task UpdateCards_Accepted_Returns200Json()
        {
            var client = await _api.AsSiteAsync();
            await AssertMessage(await client.PostAsync("/api/waifu/cards/character/5/update", ApiServer.Json("{}")), HttpStatusCode.OK);
            Assert.Single(_api.Executor.Added);
        }

        [Fact]
        public async Task RichMessageDelete_Unknown_Returns404Json()
        {
            var client = await _api.AsSiteAsync();
            await AssertMessage(await client.DeleteAsync("/api/richmessage/123"), HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task RichMessageExample_Returns200()
        {
            var client = await _api.AsSiteAsync();
            var res = await client.GetAsync("/api/richmessage");
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            Assert.NotNull(JObject.Parse(await res.Content.ReadAsStringAsync()));
        }
    }

    public class CardImageFileTests
    {
        private static async Task<(int status, string type, byte[] body)> ExecuteAsync(IActionResult result)
        {
            var controller = ControllerHarness.Attach(new Sanakan.Api.Controllers.RichMessageController(null, new FakeConfig(), new ListLogger()));
            await result.ExecuteResultAsync(controller.ControllerContext);

            var response = controller.HttpContext.Response;
            response.Body.Position = 0;
            using var ms = new MemoryStream();
            await response.Body.CopyToAsync(ms);
            return (response.StatusCode, response.ContentType, ms.ToArray());
        }

        [Theory]
        [InlineData("webp", "image/webp")]
        [InlineData("gif", "image/gif")]
        public async Task RelativePathWithParentDir_IsServedWithContentType(string ext, string expectedType)
        {
            var root = "sanakan-card-" + Guid.NewGuid().ToString("N");
            var dir = Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "..", root, "GOut", "Cards"));
            var file = Path.Combine(dir.FullName, $"5.{ext}");
            var content = Encoding.ASCII.GetBytes($"fake-{ext}-image");
            File.WriteAllBytes(file, content);

            try
            {
                // jak Dir: ścieżka względna od katalogu roboczego, z "..", której PhysicalFile sam nie przyjmuje
                var relative = $"../{root}/GOut/Cards/5.{ext}";
                Assert.False(Path.IsPathRooted(relative));
                Assert.Throws<NotSupportedException>(() => ExecuteAsync(new PhysicalFileResult(relative, expectedType)).GetAwaiter().GetResult());

                var (status, type, body) = await ExecuteAsync(Sanakan.Api.Controllers.WaifuController.CardImageFile(relative));

                Assert.Equal(200, status);
                Assert.Equal(expectedType, type);
                Assert.Equal(content, body);
            }
            finally
            {
                Directory.Delete(dir.Parent.Parent.FullName, true);
            }
        }
    }

    public class ApiResponseStyleTests
    {
        private static IEnumerable<string> ControllerFiles()
            => Directory.GetFiles(Path.Combine(RepoPaths.Src, "Api", "Controllers"), "*.cs");

        [Fact]
        public void Controllers_DoNotWriteResponsesManually()
        {
            var offenders = ControllerFiles()
                .SelectMany(f => File.ReadAllLines(f).Select((line, i) => (file: Path.GetFileName(f), line, no: i + 1)))
                .Where(x => x.line.Contains("ExecuteResultAsync(") || x.line.Contains("Response.SendFileAsync("))
                .Select(x => $"{x.file}:{x.no}")
                .ToList();

            Assert.Empty(offenders);
        }

        [Fact]
        public void Actions_ReturnActionResults()
        {
            var actions = typeof(BotWebHost).Assembly.GetTypes()
                .Where(t => typeof(ControllerBase).IsAssignableFrom(t))
                .SelectMany(t => t.GetMethods().Where(m => m.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute), true).Any()))
                .ToList();

            Assert.NotEmpty(actions);

            var wrong = actions.Where(m =>
            {
                var type = m.ReturnType;
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
                    type = type.GetGenericArguments()[0];

                return !(typeof(IActionResult).IsAssignableFrom(type)
                    || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ActionResult<>)));
            }).Select(m => $"{m.DeclaringType.Name}.{m.Name}").ToList();

            Assert.Empty(wrong);
        }
    }
}
