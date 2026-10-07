using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Sanakan.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Artifacts
{
    // lokalny serwer HTTP na localhost, bez dostępu do internetu
    public sealed class LocalHttpServer : IDisposable
    {
        private readonly HttpListener _listener = new HttpListener();
        private readonly Func<HttpListenerContext, Task> _handler;

        public string BaseUrl { get; }

        public LocalHttpServer(Func<HttpListenerContext, Task> handler)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();

            BaseUrl = $"http://localhost:{port}/";
            _handler = handler;
            _listener.Prefixes.Add(BaseUrl);
            _listener.Start();
            _ = Task.Run(LoopAsync);
        }

        private async Task LoopAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync(); }
                catch { return; }

                _ = Task.Run(async () =>
                {
                    try { await _handler(ctx); }
                    catch { }
                    finally { try { ctx.Response.Close(); } catch { } }
                });
            }
        }

        public void Dispose()
        {
            try { _listener.Stop(); _listener.Close(); } catch { }
        }
    }

    public class ImageDownloadLimitTests
    {
        private static readonly MethodInfo DownloadMethod = typeof(ImageProcessing)
            .GetMethod("DownloadImageAsync", BindingFlags.NonPublic | BindingFlags.Instance);

        private static ImageProcessing Create() => new ImageProcessing(null, null, true);

        private static byte[] Png(int width, int height)
        {
            using var image = new Image<L8>(width, height);
            using var ms = new MemoryStream();
            image.SaveAsPng(ms);
            return ms.ToArray();
        }

        private static async Task<Stream> DownloadAsync(ImageProcessing img, string url)
            => await (Task<Stream>)DownloadMethod.Invoke(img, new object[] { url });

        private static Func<HttpListenerContext, Task> Serve(byte[] body, string type = "image/png")
            => async ctx =>
            {
                ctx.Response.ContentType = type;
                ctx.Response.ContentLength64 = body.Length;
                await ctx.Response.OutputStream.WriteAsync(body);
            };

        [Fact]
        public async Task ValidSmallImage_IsDownloadedAndLoadable()
        {
            using var server = new LocalHttpServer(Serve(Png(100, 50)));

            using var stream = await DownloadAsync(Create(), server.BaseUrl + "a.png");

            Assert.NotNull(stream);
            Assert.NotSame(Stream.Null, stream);
            using var image = Image.Load(stream);
            Assert.Equal(100, image.Width);
        }

        [Fact]
        public async Task ImageWithTooLargeDimension_IsRejected()
        {
            using var server = new LocalHttpServer(Serve(Png(8193, 1)));

            Assert.Same(Stream.Null, await DownloadAsync(Create(), server.BaseUrl + "wide.png"));
        }

        [Fact]
        public async Task NotAnImage_IsRejected()
        {
            using var server = new LocalHttpServer(Serve(Enumerable.Repeat((byte)7, 4096).ToArray()));

            Assert.Same(Stream.Null, await DownloadAsync(Create(), server.BaseUrl + "garbage.png"));
        }

        [Fact]
        public async Task DeclaredContentLengthOverLimit_IsRejectedWithoutDownloading()
        {
            var bytesSent = 0L;
            using var server = new LocalHttpServer(async ctx =>
            {
                ctx.Response.ContentType = "image/png";
                ctx.Response.ContentLength64 = 50L * 1024 * 1024;
                var chunk = new byte[64 * 1024];
                for (int i = 0; i < 800; i++)
                {
                    await ctx.Response.OutputStream.WriteAsync(chunk);
                    Interlocked.Add(ref bytesSent, chunk.Length);
                }
            });

            var img = Create();
            Assert.Same(Stream.Null, await DownloadAsync(img, server.BaseUrl + "huge.png"));

            var (isImage, _) = await img.IsUrlToImageAsync(server.BaseUrl + "huge.png");
            Assert.False(isImage);
        }

        [Fact]
        public async Task ChunkedBodyOverLimit_IsCutOff()
        {
            using var server = new LocalHttpServer(async ctx =>
            {
                ctx.Response.ContentType = "image/png";
                ctx.Response.SendChunked = true;
                // poprawny nagłówek PNG, żeby odrzucenie wynikało z rozmiaru, a nie z formatu
                await ctx.Response.OutputStream.WriteAsync(Png(10, 10));
                var chunk = new byte[256 * 1024];
                for (int i = 0; i < 180; i++)
                    await ctx.Response.OutputStream.WriteAsync(chunk);
            });

            Assert.Same(Stream.Null, await DownloadAsync(Create(), server.BaseUrl + "chunked.png"));
        }

        [Fact]
        public async Task ChunkedBodyUnderLimit_IsAccepted()
        {
            using var server = new LocalHttpServer(async ctx =>
            {
                ctx.Response.ContentType = "image/png";
                ctx.Response.SendChunked = true;
                await ctx.Response.OutputStream.WriteAsync(Png(10, 10));
                var chunk = new byte[256 * 1024];
                for (int i = 0; i < 80; i++)
                    await ctx.Response.OutputStream.WriteAsync(chunk);
            });

            using var stream = await DownloadAsync(Create(), server.BaseUrl + "chunked-ok.png");
            Assert.NotNull(stream);
            Assert.NotSame(Stream.Null, stream);
        }

        [Fact]
        public async Task NotFound_ReturnsNull()
        {
            using var server = new LocalHttpServer(ctx =>
            {
                ctx.Response.StatusCode = 404;
                return Task.CompletedTask;
            });

            Assert.Null(await DownloadAsync(Create(), server.BaseUrl + "missing.png"));
        }

        [Fact]
        public async Task IsUrlToImage_ReadsOnlyHeaders()
        {
            using var server = new LocalHttpServer(Serve(Png(10, 10)));

            var (isImage, ext) = await Create().IsUrlToImageAsync(server.BaseUrl + "ok.png");

            Assert.True(isImage);
            Assert.Equal("png", ext);
        }
    }

    public class SharedCacheConcurrencyTests
    {
        [Fact]
        public void FakeKc_ParallelAccess_IsConsistentAndDoesNotThrow()
        {
            var seen = new ConcurrentDictionary<ulong, int>();

            Parallel.For(0, 200_000, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount * 2 }, i =>
            {
                var id = (ulong)(i % 5000) + 9_000_000;
                var value = Fun.GetAFKC(id);
                var first = seen.GetOrAdd(id, value);
                Assert.Equal(first, value);
            });
        }

        [Fact]
        public void FontAndColorCache_ParallelAccess_DoesNotThrow()
        {
            var img = new ImageProcessing(null, null, true);
            var getFont = typeof(ImageProcessing).GetMethod("GetOrCreateFont", BindingFlags.NonPublic | BindingFlags.Instance);
            var getColor = typeof(ImageProcessing).GetMethod("GetOrCreateColor", BindingFlags.NonPublic | BindingFlags.Instance);
            var family = typeof(ImageProcessing).GetField("_latoBold", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(img);

            Parallel.For(0, 50_000, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount * 2 }, i =>
            {
                getFont.Invoke(img, new[] { family, (object)(float)(8 + i % 200) });
                getColor.Invoke(img, new object[] { $"#{i % 4096:X3}" });
            });

            var font1 = getFont.Invoke(img, new[] { family, (object)20f });
            var font2 = getFont.Invoke(img, new[] { family, (object)20f });
            Assert.Same(font1, font2);
        }
    }
}
