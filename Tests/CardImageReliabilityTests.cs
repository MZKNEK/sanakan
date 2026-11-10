using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sanakan;
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
            using var server = new LocalHttpServer(Flaky(int.MaxValue, 500, () => Interlocked.Increment(ref requests)));
            var logger = new ListLogger();
            var url = server.BaseUrl + "char.png";

            var picture = await new ImageProcessing(null, null, true, logger).LoadCharacterPictureAsync(url, 2930843);

            Assert.Null(picture);
            Assert.Equal(3 * 6, requests);
            var message = Assert.Single(logger.Messages);
            Assert.Contains("2930843", message);
            Assert.Contains(url, message);
            Assert.Contains("HTTP 500", message);
            Assert.True(DiscordChannelLogger.ShouldSend(message));
            Assert.Equal(LogKind.Error, LoggerExtensions.Parse(message).Kind);
        }

        [Theory]
        [InlineData(404)]
        [InlineData(410)]
        public async Task CharacterPicture_Gone_IsLoggedWithReason_ButNotSentToLogChannel(int status)
        {
            // a picture missing on Shinden or set by the player that no longer exists
            var requests = 0;
            using var server = new LocalHttpServer(Flaky(int.MaxValue, status, () => Interlocked.Increment(ref requests)));
            var logger = new ListLogger();
            var url = server.BaseUrl + "char.png";

            var picture = await new ImageProcessing(null, null, true, logger).LoadCharacterPictureAsync(url, 1704809);

            Assert.Null(picture);
            var message = Assert.Single(logger.Messages);
            Assert.Contains("1704809", message);
            Assert.Contains(url, message);
            Assert.Contains($"HTTP {status}", message);
            Assert.Equal(LogKind.Routine, LoggerExtensions.Parse(message).Kind);
            Assert.False(DiscordChannelLogger.ShouldSend(message));
        }

        [Fact]
        public async Task CharacterPicture_ServerError_IsStillAnError()
        {
            var requests = 0;
            using var server = new LocalHttpServer(Flaky(int.MaxValue, 500, () => Interlocked.Increment(ref requests)));
            var logger = new ListLogger();

            var picture = await new ImageProcessing(null, null, true, logger).LoadCharacterPictureAsync(server.BaseUrl + "char.png", 1);

            Assert.Null(picture);
            Assert.Equal(LogKind.Error, LoggerExtensions.Parse(Assert.Single(logger.Messages)).Kind);
        }

        [Fact]
        public async Task CharacterPicture_RateLimited_IsLoggedWithReason_ButNotSentToLogChannel()
        {
            var requests = 0;
            using var server = new LocalHttpServer(Flaky(int.MaxValue, 429, () => Interlocked.Increment(ref requests)));
            var logger = new ListLogger();

            var picture = await new ImageProcessing(null, null, true, logger).LoadCharacterPictureAsync(server.BaseUrl + "char.png", 1081239);

            Assert.Null(picture);
            Assert.Equal(1, requests);
            var message = Assert.Single(logger.Messages);
            Assert.Contains("1081239", message);
            Assert.Contains("HTTP 429", message);
            Assert.Equal(LogKind.Routine, LoggerExtensions.Parse(message).Kind);
            Assert.False(DiscordChannelLogger.ShouldSend(message));
        }

        [Fact]
        public async Task RateLimitedDownload_DoesNotTryOtherExtensions()
        {
            var requests = 0;
            using var server = new LocalHttpServer(Flaky(int.MaxValue, 429, () => Interlocked.Increment(ref requests)));
            var card = new Card { Id = 3 };

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                new ImageProcessing(null, null, true).SaveCardBorderImageFromUrlAsync(server.BaseUrl + "border.png", card));

            Assert.Equal(1, requests);
            Assert.Null(card.CustomBorder);
        }

        [Fact]
        public async Task CharacterPicture_NotAnImage_IsLoggedWithReason()
        {
            using var server = new LocalHttpServer(async ctx =>
            {
                ctx.Response.ContentType = "text/html";
                await ctx.Response.OutputStream.WriteAsync(System.Text.Encoding.UTF8.GetBytes("<html></html>"));
            });
            var logger = new ListLogger();

            var picture = await new ImageProcessing(null, null, true, logger).LoadCharacterPictureAsync(server.BaseUrl + "char.png", 5);

            Assert.Null(picture);
            var message = Assert.Single(logger.Messages);
            Assert.Contains("not a supported image", message);
            Assert.Equal(LogKind.Error, LoggerExtensions.Parse(message).Kind);
        }

        private sealed class FakeImages : ImageProcessing
        {
            public int CardRenders;
            public int ProfileRenders;
            public bool FailNextProfile;

            public FakeImages() : base(null, null, true) { }

            public override Task<Image> GetWaifuCardAsync(Card card, CardPictureCache pictures)
            {
                CardRenders++;
                return Task.FromResult<Image>(new Image<Rgba32>(475, 667));
            }

            public override Task<Image> GetWaifuInProfileCardAsync(Card card, CardPictureCache pictures)
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

        [Fact]
        public async Task CharacterPicture_WithCache_IsDownloadedOnce()
        {
            var requests = 0;
            using var server = new LocalHttpServer(Flaky(0, 200, () => Interlocked.Increment(ref requests)));
            var images = new ImageProcessing(null, null, true);
            var pictures = new ImageProcessing.CardPictureCache();

            using var first = await images.LoadCharacterPictureAsync(server.BaseUrl + "char.png", 1, pictures);
            using var second = await images.LoadCharacterPictureAsync(server.BaseUrl + "char.png", 1, pictures);

            Assert.Equal(1, requests);
            Assert.Equal(40, first.Width);
            Assert.Equal(40, second.Width);
            Assert.NotSame(first, second);
        }

        [Fact]
        public async Task CharacterPicture_WithCache_FailureIsNotRetriedOrLoggedAgain()
        {
            var requests = 0;
            using var server = new LocalHttpServer(Flaky(int.MaxValue, 404, () => Interlocked.Increment(ref requests)));
            var logger = new ListLogger();
            var images = new ImageProcessing(null, null, true, logger);
            var pictures = new ImageProcessing.CardPictureCache();

            Assert.Null(await images.LoadCharacterPictureAsync(server.BaseUrl + "char.png", 1, pictures));
            Assert.Null(await images.LoadCharacterPictureAsync(server.BaseUrl + "char.png", 1, pictures));

            Assert.Equal(3 * 6, requests);
            Assert.Single(logger.Messages);
        }

        [Theory]
        [InlineData(Quality.Broken, false)]
        [InlineData(Quality.Broken, true)]
        public async Task GeneratingCard_DownloadsCharacterPictureOnce(Quality quality, bool animated)
        {
            Dir.Create();
            var requests = 0;
            using var server = new LocalHttpServer(Flaky(0, 200, () => Interlocked.Increment(ref requests)));
            var card = new Card
            {
                Id = 980000000000UL + (ulong)Environment.TickCount64 % 1000000 + (ulong)quality * 10 + (animated ? 1UL : 0UL),
                Quality = quality,
                IsAnimatedImage = animated,
                Image = server.BaseUrl + "char.png",
            };
            var logger = new ListLogger();
            var waifu = new Waifu(new ImageProcessing(null, null, true), null, null, logger, null, null, null, new FixedTime(), null, null, new FakeConfig());
            try
            {
                await waifu.GenerateAndSaveCardAsync(card);

                Assert.Empty(logger.Messages);
                Assert.All(Files(card), path => Assert.True(File.Exists(path), path));
                Assert.Equal(1, requests);
            }
            finally
            {
                foreach (var path in Files(card).Where(File.Exists))
                    File.Delete(path);
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
