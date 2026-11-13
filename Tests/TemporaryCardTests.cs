using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sanakan.Database.Models;
using Sanakan.Services;
using Sanakan.Services.PocketWaifu;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Artifacts
{
    // A picture that fails for a moment (the image host restarts) must not leave the card without it for good.
    public class TemporaryCardTests
    {
        private static byte[] Png()
        {
            using var image = new Image<Rgba32>(40, 60, new Rgba32(200, 30, 30));
            using var ms = new MemoryStream();
            image.SaveAsPng(ms);
            return ms.ToArray();
        }

        // answers `status` while `failing` is set, then the picture
        private static Func<System.Net.HttpListenerContext, Task> Server(Func<int?> failing)
        {
            var body = Png();
            return async ctx =>
            {
                var status = failing();
                if (status != null)
                {
                    ctx.Response.StatusCode = status.Value;
                    if (status == 200)
                    {
                        ctx.Response.ContentType = "text/html";
                        await ctx.Response.OutputStream.WriteAsync(System.Text.Encoding.UTF8.GetBytes("<html></html>"));
                    }
                    ctx.Response.Close();
                    return;
                }
                ctx.Response.ContentType = "image/png";
                ctx.Response.ContentLength64 = body.Length;
                await ctx.Response.OutputStream.WriteAsync(body);
            };
        }

        [Theory]
        [InlineData(500, true)]
        [InlineData(503, true)]
        [InlineData(429, true)]
        [InlineData(404, false)]
        [InlineData(410, false)]
        // a page instead of a picture stays broken
        [InlineData(200, false)]
        public async Task OnlyFailuresThatPassAreTemporary(int status, bool temporary)
        {
            using var server = new LocalHttpServer(Server(() => status));
            var pictures = new ImageProcessing.CardPictureCache();

            Assert.Null(await new ImageProcessing(null, null, true, new ListLogger()).LoadCharacterPictureAsync(server.BaseUrl + "char.png", 1, pictures));

            Assert.True(pictures.HasFailure);
            Assert.Equal(temporary, pictures.TemporaryFailure);
        }

        [Fact]
        public async Task NoConnectionIsTemporary()
        {
            var pictures = new ImageProcessing.CardPictureCache();

            Assert.Null(await new ImageProcessing(null, null, true, new ListLogger()).LoadCharacterPictureAsync("http://127.0.0.1:1/char.png", 1, pictures));

            Assert.True(pictures.TemporaryFailure);
        }

        private static Card NewCard(string image) => new Card
        {
            Id = 970000000000UL + (ulong)Interlocked.Increment(ref _next) * 1000 + (ulong)(Environment.TickCount64 % 1000),
            Quality = Quality.Broken,
            Image = image,
        };

        private static long _next;

        private static string[] Files(Card card) => new[]
        {
            $"{Dir.Cards}/{card.Id}.webp", $"{Dir.CardsMiniatures}/{card.Id}.webp", $"{Dir.CardsInProfiles}/{card.Id}.webp",
        };

        private static void Clean(Card card)
        {
            foreach (var path in Files(card).Where(File.Exists))
                File.Delete(path);
            if (Directory.Exists(Dir.TemporaryCards))
                foreach (var path in Directory.EnumerateFiles(Dir.TemporaryCards, $"{card.Id}-*"))
                    File.Delete(path);
        }

        private static Waifu NewWaifu() => new Waifu(new ImageProcessing(null, null, true, new ListLogger()), null, null, new ListLogger(),
            null, null, null, new FixedTime(), null, null, new FakeConfig());

        [Theory]
        [InlineData(CardImageType.Normal, 475)]
        [InlineData(CardImageType.Small, 133)]
        [InlineData(CardImageType.Profile, 475)]
        public async Task CardIsShownFromATemporaryFileAndDrawnAgainWhenThePictureIsBack(CardImageType type, int width)
        {
            Dir.Create();
            int? failing = 503;
            using var server = new LocalHttpServer(Server(() => failing));
            var card = NewCard(server.BaseUrl + "char.png");
            var waifu = NewWaifu();
            try
            {
                var shown = await waifu.GenerateAndSaveCardAsync(card, type);

                Assert.True(Dir.IsTemporary(shown), shown);
                Assert.True(File.Exists(shown));
                Assert.Equal(width, Image.Identify(shown).Width);
                Assert.All(Files(card), path => Assert.False(File.Exists(path), path));

                failing = null;
                var drawn = await waifu.GenerateAndSaveCardAsync(card, type);

                Assert.False(Dir.IsTemporary(drawn), drawn);
                Assert.All(Files(card), path => Assert.True(File.Exists(path), path));
            }
            finally
            {
                Clean(card);
            }
        }

        [Fact]
        public async Task MissingPictureIsSavedAsBefore()
        {
            Dir.Create();
            using var server = new LocalHttpServer(Server(() => 404));
            var card = NewCard(server.BaseUrl + "char.png");
            try
            {
                var drawn = await NewWaifu().GenerateAndSaveCardAsync(card);

                Assert.False(Dir.IsTemporary(drawn), drawn);
                Assert.All(Files(card), path => Assert.True(File.Exists(path), path));
            }
            finally
            {
                Clean(card);
            }
        }

        [Fact]
        public async Task OldTemporaryCardsAreRemoved()
        {
            Dir.Create();
            var old = $"{Dir.TemporaryCards}/1-old.webp";
            var recent = $"{Dir.TemporaryCards}/2-recent.webp";
            File.WriteAllText(old, "x");
            File.WriteAllText(recent, "x");
            File.SetLastWriteTimeUtc(old, DateTime.UtcNow - Waifu.TemporaryCardLifetime - TimeSpan.FromMinutes(1));
            using var server = new LocalHttpServer(Server(() => 503));
            var card = NewCard(server.BaseUrl + "char.png");
            try
            {
                await NewWaifu().GenerateAndSaveCardAsync(card);

                Assert.False(File.Exists(old));
                Assert.True(File.Exists(recent));
            }
            finally
            {
                File.Delete(recent);
                Clean(card);
            }
        }

        [Fact]
        public void OnlyTheTemporaryFolderIsTemporary()
        {
            Assert.True(Dir.IsTemporary($"{Dir.TemporaryCards}/1-abc.webp"));
            Assert.False(Dir.IsTemporary($"{Dir.Cards}/1.webp"));
            Assert.False(Dir.IsTemporary($"{Dir.TemporaryCards}x/1.webp"));
            Assert.False(Dir.IsTemporary(null));
        }
    }
}
