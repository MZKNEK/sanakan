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
        public ListLogger Logger { get; } = new ListLogger();
        public FakeBlacklist Blacklist { get; } = new FakeBlacklist();
        public Sanakan.Services.Session.SessionManager Sessions { get; }
        public HttpClient Client { get; }
        public Uri KestrelUrl { get; }
        private readonly IHost _host;
        private readonly LocalHttpServer _shindenServer;

        public ApiServer() : this(false) { }

        // kestrel: a real socket listener instead of the in-memory test server, for raw connection behaviour
        public ApiServer(bool kestrel)
        {
            Config.Model.UserKeyApps.Add(new ApiApp { Key = SiteKey, Bearer = "Shinden", Permissions = ApiAppPermission.Site });

            var logger = Logger;
            // JWT jest sprawdzany względem prawdziwego zegara, więc token musi być wystawiony "teraz"
            var time = new SystemTime();
            // lokalny "shinden", który na wszystko odpowiada 404
            _shindenServer = new LocalHttpServer(ctx =>
            {
                ctx.Response.StatusCode = 404;
                return Task.CompletedTask;
            });
            var shinden = Sanakan.ShindenCompat.ShindenClient.Create(new ConfigShinden { Token = "token", UserAgent = "tests",
                Marmolade = "marmolade", BaseUri = _shindenServer.BaseUrl }, null, null, TimeSpan.FromSeconds(5));
            var shindenCache = new Sanakan.Services.Shinden(shinden, null, null, logger, time);
            var waifu = new Waifu(null, shinden, null, logger, null, null, null, time, shindenCache, null, Config);
            var discord = new DiscordSocketClient();
            Sessions = new Sanakan.Services.Session.SessionManager(discord, Executor, logger);

            _host = new HostBuilder().ConfigureWebHost(web => (kestrel
                    ? web.UseKestrel(o => o.Listen(IPAddress.Loopback, 0))
                    : web.UseTestServer())
                .ConfigureServices(services =>
                {
                    BotWebHost.AddApiServices(services, Config);
                    services.AddSingleton((TagHelper)RuntimeHelpers.GetUninitializedObject(typeof(TagHelper)));
                    services.AddSingleton((Sanakan.Services.Profile)RuntimeHelpers.GetUninitializedObject(typeof(Sanakan.Services.Profile)));
                    services.AddSingleton<ISystemTime>(time);
                    services.AddSingleton(waifu);
                    services.AddSingleton<Sanakan.ILogger>(logger);
                    services.AddSingleton(discord);
                    services.AddSingleton(Sessions);
                    services.AddSingleton<IPlayerBlacklist>(Blacklist);
                    services.AddSingleton(new Sanakan.Services.Helper(Config, logger));
                    services.AddSingleton(shinden);
                    services.AddSingleton(shindenCache);
                    services.AddSingleton<IExecutor>(Executor);
                    services.AddSingleton(new Expedition(time));
                })
                .Configure(BotWebHost.UseApi)).Start();

            if (kestrel)
            {
                var address = _host.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>().Features
                    .Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>().Addresses.First();
                KestrelUrl = new Uri(address);
                Client = new HttpClient { BaseAddress = KestrelUrl };
            }
            else
            {
                Client = _host.GetTestClient();
            }
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

        public static Task<HttpResponseMessage> PostSiteEndpointAsync(HttpClient client)
            => client.PostAsync("/api/waifu/character/5/sync", Json("{\"characterName\":\"x\"}"));

        public void Dispose()
        {
            Client.Dispose();
            _host.Dispose();
            _shindenServer.Dispose();
        }
    }

    public class FakeBlacklist : IPlayerBlacklist
    {
        public System.Collections.Concurrent.ConcurrentDictionary<ulong, bool> Users { get; } = new System.Collections.Concurrent.ConcurrentDictionary<ulong, bool>();
        public Task<bool> IsBlacklistedAsync(ulong discordId) => Task.FromResult(Users.ContainsKey(discordId));
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

        // ochrona przed brute-force: 3 nieudane próby blokują klienta na 24h
        [Fact]
        public async Task Token_RepeatedInvalidKeys_AreLockedWith429()
        {
            for (int i = 0; i < Sanakan.Api.TokenAttemptGuard.MaxAttempts; i++)
                Assert.Equal(HttpStatusCode.Forbidden, (await _api.Client.PostAsync("/api/token", ApiServer.Json("\"wrong\""))).StatusCode);

            Assert.Equal(HttpStatusCode.TooManyRequests, (await _api.Client.PostAsync("/api/token", ApiServer.Json("\"wrong\""))).StatusCode);
            // poprawny klucz też jest odrzucany w czasie blokady
            Assert.Equal(HttpStatusCode.TooManyRequests, (await _api.Client.PostAsync("/api/token", ApiServer.Json($"\"{ApiServer.SiteKey}\""))).StatusCode);
        }

        // the test config has no connection string, so the database access throws
        [Fact]
        public async Task UnhandledException_Returns500Json_AndIsLogged()
        {
            await AssertMessage(await _api.Client.GetAsync("/api/waifu/card/5/view"), HttpStatusCode.InternalServerError);

            Assert.Contains(_api.Logger.Messages, x => x.StartsWith(Sanakan.LoggerExtensions.ErrorPrefix + "API: GET /api/waifu/card/5/view"));
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
        public async Task GiveShindenPacks_InvalidIdempotencyKey_Returns400Json()
        {
            var client = await _api.AsSiteAsync();
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/waifu/shinden/5/boosterpack") { Content = ApiServer.Json("[]") };
            req.Headers.Add("Idempotency-Key", new string('a', 65));
            await AssertMessage(await client.SendAsync(req), HttpStatusCode.BadRequest);
        }

        [Theory]
        [InlineData("discord", " ")]
        [InlineData("shinden", " ")]
        [InlineData("discord", "invalid key")]
        [InlineData("shinden", "invalid key")]
        public async Task ModifyTc_InvalidIdempotencyHeader_Returns400Json(string identifier, string key)
        {
            var client = await _api.AsSiteAsync();
            var request = new HttpRequestMessage(HttpMethod.Put, $"/api/user/{identifier}/5/tc") { Content = ApiServer.Json("100") };
            request.Headers.TryAddWithoutValidation("Idempotency-Key", new[] { key });

            await AssertMessage(await client.SendAsync(request), HttpStatusCode.BadRequest);
            Assert.Empty(_api.Executor.Added);
        }

        [Fact]
        public async Task OpenShindenPacks_InvalidIdempotencyKey_Returns400Json()
        {
            var client = await _api.AsSiteAsync();
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/waifu/shinden/5/boosterpack/open") { Content = ApiServer.Json("[]") };
            req.Headers.Add("Idempotency-Key", "zażółć");
            await AssertMessage(await client.SendAsync(req), HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task OpenShindenPacksStatus_InvalidKey_Returns400Json()
        {
            var client = await _api.AsSiteAsync();
            await AssertMessage(await client.GetAsync("/api/waifu/shinden/5/boosterpack/open/" + new string('a', 65)), HttpStatusCode.BadRequest);
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

        // the site sends no data by hand, as an empty JSON body or {}; both still report a missing character
        [Theory]
        [InlineData("")]
        [InlineData("{}")]
        [InlineData("null")]
        public async Task SyncCharacter_WhenShindenReturns404_Returns404Json(string body)
        {
            var client = await _api.AsSiteAsync();
            await AssertMessage(await client.PostAsync("/api/waifu/character/2/sync?from=1", ApiServer.Json(body)), HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task UpdateCards_QueueFull_Returns503Json()
        {
            _api.Executor.Accept = false;
            var client = await _api.AsSiteAsync();
            await AssertMessage(await client.PostAsync("/api/waifu/character/5/sync", ApiServer.Json("{\"characterName\":\"x\"}")), (HttpStatusCode)503);
        }

        [Fact]
        public async Task UpdateCards_Accepted_Returns200Json()
        {
            var client = await _api.AsSiteAsync();
            await AssertMessage(await client.PostAsync("/api/waifu/character/5/sync", ApiServer.Json("{\"characterName\":\"x\"}")), HttpStatusCode.OK);
            Assert.Single(_api.Executor.Added);
        }
    }

    public class CardImageFileTests
    {
        private static async Task<(int status, string type, byte[] body)> ExecuteAsync(IActionResult result)
        {
            var controller = ControllerHarness.Attach(new Sanakan.Api.Controllers.DebugController(null, new ListLogger()));
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

    public class ApiClientAbortTests : IDisposable
    {
        private readonly ApiServer _api = new ApiServer(kestrel: true);

        public void Dispose() => _api.Dispose();

        // the client announces a body and closes the connection before sending all of it
        [Fact]
        public async Task TruncatedBody_IsABadRequest_NotAnError()
        {
            using var tcp = new System.Net.Sockets.TcpClient();
            await tcp.ConnectAsync(_api.KestrelUrl.Host, _api.KestrelUrl.Port);
            var stream = tcp.GetStream();
            var request = "POST /api/waifu/unique/cards/0/500 HTTP/1.1\r\nHost: localhost\r\nContent-Type: application/json\r\n"
                + "Content-Length: 100\r\n\r\n{\"orderBy\":";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(request));
            tcp.Client.Shutdown(System.Net.Sockets.SocketShutdown.Send);

            // the client is gone, so the connection is closed without a response
            Assert.Equal("", await new StreamReader(stream, Encoding.ASCII).ReadToEndAsync());
            for (var i = 0; i < 100 && !_api.Logger.Messages.Any(x => x.Contains("/api/waifu/unique/cards/0/500")); i++)
                await Task.Delay(50);

            Assert.DoesNotContain(_api.Logger.Messages, x => Sanakan.LoggerExtensions.Parse(x).Kind == Sanakan.LogKind.Error);
            var message = Assert.Single(_api.Logger.Messages, x => x.Contains("/api/waifu/unique/cards/0/500"));
            Assert.Equal(Sanakan.LogKind.Routine, Sanakan.LoggerExtensions.Parse(message).Kind);
            Assert.DoesNotContain(" at ", message);
        }
    }
}
