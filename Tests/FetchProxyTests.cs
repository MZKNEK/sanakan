using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Sanakan.Config.Model;
using Sanakan.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Artifacts
{
    public class FetchProxyTests
    {
        private const string ProxyUrl = "http://proxy.test/fetch";

        private class FakeHandler : HttpMessageHandler
        {
            public ConcurrentQueue<(HttpRequestMessage Request, string Body)> Requests { get; } = new();
            public Func<HttpRequestMessage, Task<HttpResponseMessage>> Respond { get; set; }
                = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Requests.Enqueue((request, request.Content == null ? null : await request.Content.ReadAsStringAsync()));
                return await Respond(request);
            }
        }

        private readonly FakeHandler _proxy = new FakeHandler();
        private readonly FakeHandler _direct = new FakeHandler();
        private readonly FakeConfig _config = new FakeConfig();
        private readonly ListLogger _logger = new ListLogger();

        private FetchProxy Create() => new FetchProxy(_config, _logger, _proxy);
        private HttpClient Client() => new HttpClient(Create().CreateHandler(_direct));

        private void Enable(List<string> directHosts = null)
            => _config.Model.FetchProxy = new FetchProxyConfig { Url = ProxyUrl, Key = "secret-key", DirectHosts = directHosts };

        private void ProxyAnswers(object body, HttpStatusCode status = HttpStatusCode.OK)
            => _proxy.Respond = _ => Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(JObject.FromObject(body).ToString(), Encoding.UTF8, "application/json"),
            });

        private static byte[] Png()
        {
            using var image = new Image<Rgba32>(4, 4);
            using var stream = new MemoryStream();
            image.SaveAsPng(stream);
            return stream.ToArray();
        }

        [Fact]
        public async Task WithoutUrlEverythingGoesDirect()
        {
            using var client = Client();
            await client.GetAsync("https://i.imgur.com/a.png");
            _config.Model.FetchProxy = new FetchProxyConfig { Url = " " };
            await client.GetAsync("https://i.imgur.com/b.png");

            Assert.Equal(2, _direct.Requests.Count);
            Assert.Empty(_proxy.Requests);
        }

        [Fact]
        public async Task PlayerLinkGoesThroughProxy()
        {
            Enable();
            var png = Png();
            ProxyAnswers(new { status = 200, content_type = "image/png", content_length = png.Length, body = Convert.ToBase64String(png), truncated = false });

            using var client = Client();
            using var response = await client.GetAsync("https://i.imgur.com/a.png?x=1");

            Assert.Empty(_direct.Requests);
            var (request, body) = Assert.Single(_proxy.Requests);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(ProxyUrl, request.RequestUri.ToString());
            Assert.Equal("secret-key", request.Headers.GetValues(FetchProxy.KeyHeader).Single());
            var json = JObject.Parse(body);
            Assert.Equal("https://i.imgur.com/a.png?x=1", json.Value<string>("url"));
            Assert.Equal("GET", json.Value<string>("method"));
            Assert.Equal("image", json.Value<string>("kind"));
            Assert.Equal(5 * 1024 * 1024, json.Value<long>("max_bytes"));
            Assert.Equal(FetchProxy.TimeoutMs, json.Value<int>("timeout_ms"));
            Assert.Equal("sanakan", json.Value<string>("purpose"));
            Assert.True(json.Value<bool>("any"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("image/png", response.Content.Headers.ContentType.MediaType);
            Assert.Equal(png, await response.Content.ReadAsByteArrayAsync());
            Assert.Equal("https://i.imgur.com/a.png?x=1", response.RequestMessage.RequestUri.ToString());
        }

        [Theory]
        [InlineData("http://cdn.shinden.eu/cdn1/images/genuine/1.jpg", true)]
        [InlineData("https://CDN.discordapp.com/attachments/1/2/a.png", true)]
        [InlineData("https://images-ext-1.media.discordapp.net/a.png", true)]
        [InlineData("https://notcdn.shinden.eu/a.png", false)]
        [InlineData("https://cdn.shinden.eu.example.com/a.png", false)]
        [InlineData("https://shinden.eu/a.png", false)]
        [InlineData("https://i.imgur.com/a.png", false)]
        public void DefaultDirectHostsMatchThemAndTheirSubdomains(string url, bool direct)
            => Assert.Equal(direct, FetchProxy.IsDirect(new Uri(url).Host, new FetchProxyConfig()));

        [Fact]
        public async Task ConfiguredDirectHostsReplaceDefaults()
        {
            Enable(new List<string> { " example.com " });
            ProxyAnswers(new { status = 200, body = "" });

            using var client = Client();
            await client.GetAsync("https://img.example.com/a.png");
            await client.GetAsync("http://cdn.shinden.eu/a.jpg");

            Assert.Equal("https://img.example.com/a.png", Assert.Single(_direct.Requests).Request.RequestUri.ToString());
            Assert.Equal("http://cdn.shinden.eu/a.jpg", JObject.Parse(Assert.Single(_proxy.Requests).Body).Value<string>("url"));
        }

        [Fact]
        public async Task MaxBytesComesFromConfig()
        {
            Enable();
            _config.Model.FetchProxy.MaxBytes = 20 * 1024 * 1024;
            ProxyAnswers(new { status = 200, body = "" });

            using var client = Client();
            await client.GetAsync("https://i.imgur.com/a.png");

            Assert.Equal(20 * 1024 * 1024, JObject.Parse(Assert.Single(_proxy.Requests).Body).Value<long>("max_bytes"));
        }

        [Fact]
        public async Task StatusOfTheLinkedSiteIsKept()
        {
            Enable();
            ProxyAnswers(new { status = 404, content_type = "text/html", body = "" });

            using var client = Client();
            using var response = await client.GetAsync("https://i.imgur.com/gone.png");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Empty(_logger.Messages);
        }

        [Fact]
        public async Task TruncatedFileIsTooLarge()
        {
            Enable();
            ProxyAnswers(new { status = 200, content_type = "image/png", body = Convert.ToBase64String(new byte[10]), truncated = true });

            using var client = Client();
            using var response = await client.GetAsync("https://i.imgur.com/big.png");

            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        }

        [Fact]
        public async Task BusyProxyGivesRetryAfter()
        {
            Enable();
            ProxyAnswers(new { code = "busy", message = "busy", retry_after = 7 }, HttpStatusCode.TooManyRequests);

            using var client = Client();
            using var response = await client.GetAsync("https://i.imgur.com/a.png");

            Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
            Assert.Equal(TimeSpan.FromSeconds(7), response.Headers.RetryAfter.Delta);
            Assert.Empty(_logger.Messages);
        }

        [Fact]
        public async Task ProxyNotFoundIsNotAMissingPictureAndIsLoggedOnce()
        {
            Enable();
            _proxy.Respond = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("<html>") });

            using var client = Client();
            using (var response = await client.GetAsync("https://i.imgur.com/a.png"))
                Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
            (await client.GetAsync("https://i.imgur.com/b.png")).Dispose();

            Assert.Contains("HTTP 404", Assert.Single(_logger.Messages));

            ProxyAnswers(new { status = 200, body = "" });
            (await client.GetAsync("https://i.imgur.com/c.png")).Dispose();
            Assert.Contains("works again", _logger.Messages.Last());
        }

        [Fact]
        public async Task WrongKeyIsLogged()
        {
            Enable();
            ProxyAnswers(new { code = "bad_key", message = "bad key", retry_after = 0 }, HttpStatusCode.Unauthorized);

            using var client = Client();
            using var response = await client.GetAsync("https://i.imgur.com/a.png");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Contains("HTTP 401 bad_key", Assert.Single(_logger.Messages));
        }

        [Fact]
        public async Task UnreachableProxyThrowsLikeADirectDownload()
        {
            Enable();
            _proxy.Respond = _ => throw new HttpRequestException("connection refused");

            using var client = Client();
            await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("https://i.imgur.com/a.png"));
            Assert.Contains("connection refused", Assert.Single(_logger.Messages));
        }

        [Fact]
        public async Task BrokenAnswerThrows()
        {
            Enable();
            _proxy.Respond = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"status\":200,\"body\":\"not base64!\"}") });

            using var client = Client();
            await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("https://i.imgur.com/a.png"));
        }

        [Fact]
        public async Task OnlyGetIsSentAndNeverDirectly()
        {
            Enable();

            using var client = Client();
            await Assert.ThrowsAsync<HttpRequestException>(() => client.PostAsync("https://i.imgur.com/a", new StringContent("x")));

            Assert.Empty(_proxy.Requests);
            Assert.Empty(_direct.Requests);
        }

        [Fact]
        public async Task AtMostFourRequestsAtOnce()
        {
            Enable();
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int running = 0, most = 0;
            _proxy.Respond = async _ =>
            {
                var now = Interlocked.Increment(ref running);
                lock (this) most = Math.Max(most, now);
                await release.Task;
                Interlocked.Decrement(ref running);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"status\":200,\"body\":\"\"}") };
            };

            using var client = Client();
            var requests = Enumerable.Range(0, 7).Select(i => client.GetAsync($"https://i.imgur.com/{i}.png")).ToList();
            for (int i = 0; i < 100 && Volatile.Read(ref running) < FetchProxy.MaxParallel; i++)
                await Task.Delay(10);
            await Task.Delay(100);

            Assert.Equal(FetchProxy.MaxParallel, Volatile.Read(ref running));
            release.SetResult(true);
            foreach (var response in await Task.WhenAll(requests))
                response.Dispose();
            Assert.Equal(FetchProxy.MaxParallel, most);
            Assert.Equal(7, _proxy.Requests.Count);
        }

        [Theory]
        [InlineData("http://local.test:81/cdn/")]
        [InlineData("http://local.test:81/cdn")]
        public async Task MirroredHostIsDownloadedFromItsMirror(string mirror)
        {
            Enable();
            _config.Model.FetchProxy.Mirrors = new Dictionary<string, string> { [" CDN.shinden.eu "] = mirror };

            using var client = Client();
            using var response = await client.GetAsync("http://cdn.shinden.eu/cdn1/avatars/225x350/5.jpg?v7");

            Assert.Empty(_proxy.Requests);
            Assert.Equal("http://local.test:81/cdn/cdn1/avatars/225x350/5.jpg?v7", Assert.Single(_direct.Requests).Request.RequestUri.ToString());
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task MirrorsWorkWithoutTheProxy()
        {
            _config.Model.FetchProxy = new FetchProxyConfig { Mirrors = new Dictionary<string, string> { ["cdn.shinden.eu"] = "http://local.test/cdn/" } };

            using var client = Client();
            await client.GetAsync("http://cdn.shinden.eu/cdn1/images/genuine/1.jpg");
            await client.GetAsync("https://i.imgur.com/a.png");

            Assert.Equal(new[] { "http://local.test/cdn/cdn1/images/genuine/1.jpg", "https://i.imgur.com/a.png" },
                _direct.Requests.Select(x => x.Request.RequestUri.ToString()));
        }

        [Fact]
        public async Task OnlyTheMirroredHostItselfIsMapped()
        {
            Enable();
            _config.Model.FetchProxy.Mirrors = new Dictionary<string, string> { ["cdn.shinden.eu"] = "http://local.test/cdn/", ["broken.test"] = " " };
            ProxyAnswers(new { status = 200, body = "" });

            using var client = Client();
            await client.GetAsync("https://img.cdn.shinden.eu/a.png");
            await client.GetAsync("https://shinden.eu/a.png");
            await client.GetAsync("https://broken.test/a.png");

            // a subdomain of a direct host stays direct and keeps its address
            Assert.Equal("https://img.cdn.shinden.eu/a.png", Assert.Single(_direct.Requests).Request.RequestUri.ToString());
            Assert.Equal(new[] { "https://shinden.eu/a.png", "https://broken.test/a.png" }, _proxy.Requests.Select(x => JObject.Parse(x.Body).Value<string>("url")));
        }

        [Fact]
        public async Task CardPicturesComeFromTheMirror()
        {
            _config.Model.FetchProxy = new FetchProxyConfig { Mirrors = new Dictionary<string, string> { ["cdn.shinden.eu"] = "http://local.test/cdn/" } };
            var png = Png();
            _direct.Respond = request => Task.FromResult(new HttpResponseMessage(request.RequestUri.Host == "local.test" ? HttpStatusCode.OK : HttpStatusCode.NotFound)
            {
                Content = new ByteArrayContent(png),
            });
            var images = new ImageProcessing(null, null, handler: Create().CreateHandler(_direct));

            using var picture = await images.LoadCharacterPictureAsync("http://cdn.shinden.eu/cdn1/images/genuine/77.jpg", 1);

            Assert.NotNull(picture);
            Assert.Equal(4, picture.Width);
            Assert.Equal("http://local.test/cdn/cdn1/images/genuine/77.jpg", Assert.Single(_direct.Requests).Request.RequestUri.ToString());
        }

        [Fact]
        public async Task ImageProcessingDownloadsPlayerLinksThroughProxy()
        {
            Enable();
            var png = Png();
            ProxyAnswers(new { status = 200, content_type = "image/png", body = Convert.ToBase64String(png) });
            var images = new ImageProcessing(null, null, handler: Create().CreateHandler(_direct));

            var (isImage, ext) = await images.IsUrlToImageAsync("https://example.com/a.png");
            using var stream = await images.TryGetImageStreamAsync("https://example.com/a.png");

            Assert.True(isImage);
            Assert.Equal("png", ext);
            Assert.NotSame(Stream.Null, stream);
            Assert.Equal(png.Length, stream.Length);
            Assert.Equal(2, _proxy.Requests.Count);
            Assert.Empty(_direct.Requests);
        }

        [Fact]
        public async Task ImageProcessingStillRefusesPrivateAddresses()
        {
            Enable();
            var images = new ImageProcessing(null, null, handler: Create().CreateHandler(_direct));

            var (isImage, _) = await images.IsUrlToImageAsync("http://127.0.0.1/a.png");

            Assert.False(isImage);
            Assert.Empty(_proxy.Requests);
            Assert.Empty(_direct.Requests);
        }

        [Fact]
        public async Task HelperChecksStoredLinksThroughProxy()
        {
            Enable();
            ProxyAnswers(new { status = 404, body = "" });
            var helper = new Helper(_config, _logger, Create().CreateHandler(_direct));

            Assert.Equal(HttpStatusCode.NotFound, await helper.GetResponseFromUrl("https://i.imgur.com/a.png"));
            Assert.Single(_proxy.Requests);
            Assert.Empty(_direct.Requests);
        }

        // a minimal HTTP proxy: records each request line and answers with whatever Answer returns
        private sealed class LocalHttpProxy : IDisposable
        {
            private readonly System.Net.Sockets.TcpListener _listener = new(IPAddress.Loopback, 0);
            public ConcurrentQueue<string> RequestLines { get; } = new();
            public Func<string, (int Status, string Headers, byte[] Body)> Answer { get; set; } = _ => (200, "", Array.Empty<byte>());
            public string Url => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/";

            public LocalHttpProxy()
            {
                _listener.Start();
                _ = Task.Run(ServeAsync);
            }

            private async Task ServeAsync()
            {
                while (true)
                {
                    System.Net.Sockets.TcpClient client;
                    try { client = await _listener.AcceptTcpClientAsync(); }
                    catch (Exception) { return; }

                    using (client)
                    {
                        var stream = client.GetStream();
                        var reader = new StreamReader(stream, Encoding.ASCII);
                        var line = await reader.ReadLineAsync();
                        while (!string.IsNullOrEmpty(await reader.ReadLineAsync())) { }
                        RequestLines.Enqueue(line);

                        var (status, headers, body) = Answer(line);
                        var head = $"HTTP/1.1 {status} X\r\nContent-Length: {body.Length}\r\nConnection: close\r\n{headers}\r\n";
                        await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
                        await stream.WriteAsync(body);
                    }
                }
            }

            public void Dispose() => _listener.Stop();
        }

        private static string ClosedPortUrl()
        {
            var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var url = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/";
            listener.Stop();
            return url;
        }

        [Fact]
        public async Task HttpProxyAloneCarriesPlayerLinks()
        {
            using var local = new LocalHttpProxy();
            var png = Png();
            local.Answer = _ => (200, "Content-Type: image/png\r\n", png);
            _config.Model.FetchProxy = new FetchProxyConfig { HttpProxy = local.Url };

            using var client = Client();
            using var response = await client.GetAsync("http://img.example/a.png?x=1");

            Assert.Equal("GET http://img.example/a.png?x=1 HTTP/1.1", Assert.Single(local.RequestLines));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(png, await response.Content.ReadAsByteArrayAsync());
            Assert.Empty(_proxy.Requests);
            Assert.Empty(_direct.Requests);
        }

        [Fact]
        public async Task WithBothTheFetchProxyGoesFirst()
        {
            using var local = new LocalHttpProxy();
            _config.Model.FetchProxy = new FetchProxyConfig { HttpProxy = local.Url, Url = ProxyUrl, Key = "secret-key" };
            ProxyAnswers(new { status = 200, content_type = "image/png", body = Convert.ToBase64String(Png()) });

            using var client = Client();
            using var response = await client.GetAsync("http://img.example/a.png");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Single(_proxy.Requests);
            Assert.Empty(local.RequestLines);
        }

        [Fact]
        public async Task FileTooLargeForTheFetchProxyGoesThroughHttpProxy()
        {
            using var local = new LocalHttpProxy();
            var big = new byte[] { 1, 2, 3 };
            local.Answer = _ => (200, "Content-Type: image/gif\r\n", big);
            _config.Model.FetchProxy = new FetchProxyConfig { HttpProxy = local.Url, Url = ProxyUrl, Key = "secret-key" };
            ProxyAnswers(new { status = 200, body = "", truncated = true });

            using var client = Client();
            using var response = await client.GetAsync("http://img.example/big.gif");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(big, await response.Content.ReadAsByteArrayAsync());
            Assert.Single(_proxy.Requests);
            Assert.Equal("GET http://img.example/big.gif HTTP/1.1", Assert.Single(local.RequestLines));
        }

        [Fact]
        public async Task HttpProxySkipsDirectAndMirroredHosts()
        {
            using var local = new LocalHttpProxy();
            _config.Model.FetchProxy = new FetchProxyConfig
            {
                HttpProxy = local.Url,
                Mirrors = new Dictionary<string, string> { ["cdn.shinden.eu"] = "http://local.test/cdn/" },
            };

            using var client = Client();
            (await client.GetAsync("https://cdn.discordapp.com/attachments/1/2/a.png")).Dispose();
            (await client.GetAsync("http://cdn.shinden.eu/cdn1/images/genuine/1.jpg")).Dispose();

            Assert.Empty(local.RequestLines);
            Assert.Equal(new[] { "cdn.discordapp.com", "local.test" }, _direct.Requests.Select(x => x.Request.RequestUri.Host));
        }

        [Fact]
        public async Task HttpProxyKeepsTheCallersRedirectChoice()
        {
            using var local = new LocalHttpProxy();
            local.Answer = line => line.Contains("/old.png") ? (302, "Location: http://img.example/new.png\r\n", Array.Empty<byte>()) : (200, "", new byte[] { 1 });
            _config.Model.FetchProxy = new FetchProxyConfig { HttpProxy = local.Url };
            var proxy = Create();

            using (var following = new HttpClient(proxy.CreateHandler(new HttpClientHandler())))
            using (var response = await following.GetAsync("http://img.example/old.png"))
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(2, local.RequestLines.Count);

            using (var checking = new HttpClient(proxy.CreateHandler(new HttpClientHandler { AllowAutoRedirect = false })))
            using (var response = await checking.GetAsync("http://img.example/old.png"))
                Assert.Equal(HttpStatusCode.Found, response.StatusCode);
            Assert.Equal(3, local.RequestLines.Count);
        }

        [Fact]
        public async Task UnreachableHttpProxyThrowsAndIsLoggedOnce()
        {
            _config.Model.FetchProxy = new FetchProxyConfig { HttpProxy = ClosedPortUrl() };

            using var client = Client();
            await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("http://img.example/a.png"));
            await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("http://img.example/b.png"));
            Assert.Contains("http proxy", Assert.Single(_logger.Messages));
            Assert.Empty(_direct.Requests);

            using var local = new LocalHttpProxy();
            _config.Model.FetchProxy.HttpProxy = local.Url;
            (await client.GetAsync("http://img.example/c.png")).Dispose();
            Assert.Contains("works again", _logger.Messages.Last());
        }

        [Fact]
        public async Task SiteTheHttpProxyCannotReachIsNotLogged()
        {
            using var local = new LocalHttpProxy();
            local.Answer = _ => (502, "", Array.Empty<byte>());
            _config.Model.FetchProxy = new FetchProxyConfig { HttpProxy = local.Url };

            using var client = Client();
            using var response = await client.GetAsync("http://gone.example/a.png");

            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
            Assert.Empty(_logger.Messages);
        }

        [Fact]
        public async Task ImageProcessingDownloadsPlayerLinksThroughHttpProxy()
        {
            using var local = new LocalHttpProxy();
            var png = Png();
            local.Answer = _ => (200, "Content-Type: image/png\r\n", png);
            _config.Model.FetchProxy = new FetchProxyConfig { HttpProxy = local.Url };
            var images = new ImageProcessing(null, null, handler: Create().CreateHandler(_direct));

            using var stream = await images.TryGetImageStreamAsync("http://example.com/a.png");

            Assert.Equal(png.Length, stream.Length);
            Assert.Single(local.RequestLines);
            Assert.Empty(_direct.Requests);
        }
    }
}
