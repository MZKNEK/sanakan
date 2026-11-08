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
    public class CardImageReliabilityTests
    {
        private static byte[] Png()
        {
            using var image = new Image<Rgba32>(40, 60, new Rgba32(200, 30, 30));
            using var ms = new MemoryStream();
            image.SaveAsPng(ms);
            return ms.ToArray();
        }

        // fails the first `failures` requests with the given status, then serves a picture
        private static Func<System.Net.HttpListenerContext, Task> Flaky(int failures, int status, Func<int> counter)
        {
            var body = Png();
            return async ctx =>
            {
                if (counter() <= failures)
                {
                    ctx.Response.StatusCode = status;
                    ctx.Response.Close();
                    return;
                }
                ctx.Response.ContentType = "image/png";
                ctx.Response.ContentLength64 = body.Length;
                await ctx.Response.OutputStream.WriteAsync(body);
            };
        }

        [Fact]
        public async Task CharacterPicture_IsRetried_AfterTemporaryFailure()
        {
            var requests = 0;
            // one failed attempt is the picture url plus its five extension variants
            using var server = new LocalHttpServer(Flaky(6, 503, () => Interlocked.Increment(ref requests)));
            var logger = new ListLogger();

            using var picture = await new ImageProcessing(null, null, true, logger).LoadCharacterPictureAsync(server.BaseUrl + "char.png", 77);

            Assert.NotNull(picture);
            Assert.Equal(40, picture.Width);
            Assert.Empty(logger.Messages);
        }

        [Fact]
        public async Task CharacterPicture_GivesUpAfterThreeAttempts_AndLogsCardAndUrl()
        {
            var requests = 0;
            using var server = new LocalHttpServer(Flaky(int.MaxValue, 404, () => Interlocked.Increment(ref requests)));
            var logger = new ListLogger();
            var url = server.BaseUrl + "char.png";

            var picture = await new ImageProcessing(null, null, true, logger).LoadCharacterPictureAsync(url, 2930843);

            Assert.Null(picture);
            Assert.Equal(3 * 6, requests);
            var message = Assert.Single(logger.Messages);
            Assert.Contains("2930843", message);
            Assert.Contains(url, message);
        }

        private sealed class FakeImages : ImageProcessing
        {
            public int CardRenders;
            public int ProfileRenders;
            public bool FailNextProfile;

            public FakeImages() : base(null, null, true) { }

            public override Task<Image> GetWaifuCardAsync(Card card)
            {
                CardRenders++;
                return Task.FromResult<Image>(new Image<Rgba32>(475, 667));
            }

            public override Task<Image> GetWaifuInProfileCardAsync(Card card)
            {
                ProfileRenders++;
                if (FailNextProfile)
                {
                    FailNextProfile = false;
                    throw new InvalidOperationException("profile render failed");
                }
                return Task.FromResult<Image>(new Image<Rgba32>(475, 667));
            }
        }

        private static string[] Files(Card card) => new[]
        {
            $"{Dir.Cards}/{card.Id}.webp", $"{Dir.CardsMiniatures}/{card.Id}.webp", $"{Dir.CardsInProfiles}/{card.Id}.webp",
        };

        [Fact]
        public async Task HalfFailedGeneration_LaterCreatesMissingProfileImage_WithoutRedrawingTheCard()
        {
            Dir.Create();
            var card = new Card { Id = 990000000000UL + (ulong)Environment.TickCount64 % 1000000, Quality = Quality.Broken };
            var images = new FakeImages { FailNextProfile = true };
            var waifu = new Waifu(images, null, null, new ListLogger(), null, null, null, new FixedTime(), null, null, new FakeConfig());
            try
            {
                await waifu.GenerateAndSaveCardAsync(card);
                Assert.True(File.Exists(Files(card)[0]));
                Assert.False(File.Exists(Files(card)[2]));

                await waifu.GenerateAndSaveCardAsync(card);

                Assert.All(Files(card), path => Assert.True(File.Exists(path), path));
                Assert.Equal(1, images.CardRenders);
                Assert.Equal(2, images.ProfileRenders);

                await waifu.GenerateAndSaveCardAsync(card, CardImageType.Small);
                Assert.Equal(1, images.CardRenders);
                Assert.Equal(2, images.ProfileRenders);
            }
            finally
            {
                foreach (var path in Files(card).Where(File.Exists))
                    File.Delete(path);
            }
        }
    }
}
