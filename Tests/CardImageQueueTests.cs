using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
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
    public class CardImageQueueTests
    {
        private static long _nextId = 970000000000L + Environment.TickCount64 % 1000000 * 100;
        private static ulong NewId() => (ulong)Interlocked.Increment(ref _nextId);

        private sealed class Clock
        {
            public long Now;
        }

        private static CardImageQueue Queue(Clock clock, ListLogger logger = null)
            => new CardImageQueue((_, _) => Task.FromResult(CardDrawing.Drawn), logger ?? new ListLogger(), TimeSpan.FromSeconds(10), nowMs: () => clock.Now);

        [Fact]
        public void Card_IsDueOnlyAfterItStopsChanging()
        {
            var clock = new Clock();
            var queue = Queue(clock);

            Assert.True(queue.Enqueue(5, false));
            clock.Now = 9000;
            Assert.Empty(queue.TakeDue());

            Assert.True(queue.Enqueue(5, false));
            clock.Now = 15000;
            Assert.Empty(queue.TakeDue());

            clock.Now = 19000;
            Assert.Equal(new[] { (5UL, false) }, queue.TakeDue());
            Assert.Equal(0, queue.Pending);
            Assert.Empty(queue.TakeDue());
        }

        [Fact]
        public void Force_IsKeptWhenTheCardChangesAgain()
        {
            var clock = new Clock();
            var queue = Queue(clock);

            queue.Enqueue(5, true);
            queue.Enqueue(5, false);
            queue.Enqueue(6, false);
            clock.Now = 10000;

            Assert.Equal(new[] { (5UL, true), (6UL, false) }, queue.TakeDue().OrderBy(x => x.Id));
        }

        [Fact]
        public void FullQueue_RejectsNewCards_LogsOnce_AndStillTakesChangesOfQueuedCards()
        {
            var clock = new Clock();
            var logger = new ListLogger();
            var queue = Queue(clock, logger);

            Assert.False(queue.Enqueue(0, false));
            for (ulong i = 1; i <= CardImageQueue.MaxPending; i++)
                Assert.True(queue.Enqueue(i, false));

            Assert.False(queue.Enqueue(CardImageQueue.MaxPending + 1, false));
            Assert.False(queue.Enqueue(CardImageQueue.MaxPending + 2, false));
            Assert.True(queue.Enqueue(1, true));
            Assert.Equal(LogKind.Routine, LoggerExtensions.Parse(Assert.Single(logger.Messages)).Kind);

            clock.Now = 10000;
            Assert.Equal(CardImageQueue.MaxPending, queue.TakeDue().Count);
            Assert.True(queue.Enqueue(CardImageQueue.MaxPending + 1, false));
        }

        [Fact]
        public async Task Started_DrawsARepeatedlyChangedCardOnce()
        {
            var drawn = new ConcurrentQueue<(ulong, bool)>();
            var queue = new CardImageQueue((id, force) =>
            {
                drawn.Enqueue((id, force));
                return Task.FromResult(CardDrawing.Drawn);
            }, new ListLogger(), TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(20));
            using var stop = new CancellationTokenSource();
            queue.Start(stop.Token);

            queue.Enqueue(7, false);
            queue.Enqueue(7, false);
            queue.Enqueue(8, true);

            var until = DateTime.UtcNow.AddSeconds(10);
            while (drawn.Count < 2 && DateTime.UtcNow < until)
                await Task.Delay(20);
            await Task.Delay(300);
            stop.Cancel();

            Assert.Equal(new[] { (7UL, false), (8UL, true) }, drawn.OrderBy(x => x.Item1));
            Assert.Contains("background 2 drawn (1 forced)", queue.FlushReport());
        }

        [Fact]
        public async Task Report_CountsEveryOutcome_AndIsEmptyWhenNothingHappened()
        {
            var results = new ConcurrentDictionary<ulong, CardDrawing>
            {
                [1] = CardDrawing.Drawn, [2] = CardDrawing.AlreadyDrawn, [3] = CardDrawing.NoPicture, [4] = CardDrawing.Removed, [5] = CardDrawing.Failed,
            };
            var done = 0;
            var clock = new Clock();
            var queue = new CardImageQueue((id, _) =>
            {
                Interlocked.Increment(ref done);
                if (id == 6)
                    throw new InvalidOperationException("boom");
                return Task.FromResult(results[id]);
            }, new ListLogger(), TimeSpan.Zero, TimeSpan.FromMilliseconds(20), () => clock.Now);
            using var stop = new CancellationTokenSource();
            queue.Start(stop.Token);

            for (ulong id = 1; id <= 6; id++)
                queue.Enqueue(id, id == 1);
            var until = DateTime.UtcNow.AddSeconds(10);
            while (Volatile.Read(ref done) < 6 && DateTime.UtcNow < until)
                await Task.Delay(20);
            await Task.Delay(50);
            stop.Cancel();
            queue.CountLateRequest();
            clock.Now = 60 * 60000;

            Assert.Equal("Card images (60 min): background 1 drawn (1 forced), 1 already drawn, 1 without picture, 1 removed cards, 2 failed, "
                + "0 not queued (queue full), 0 waiting; 1 requests not ready in time (503)", queue.FlushReport());
            Assert.Null(queue.FlushReport());
        }

        [Fact]
        public void Report_ShowsRejectedAndWaitingCards()
        {
            var clock = new Clock();
            var queue = Queue(clock);
            for (ulong i = 1; i <= CardImageQueue.MaxPending + 3; i++)
                queue.Enqueue(i, false);

            var report = queue.FlushReport();

            Assert.Contains("3 not queued (queue full)", report);
            Assert.Contains($"{CardImageQueue.MaxPending} waiting", report);
        }

        [Fact]
        public async Task FailedDrawing_IsLogged_AndTheQueueGoesOn()
        {
            var logger = new ListLogger();
            var drawn = new ConcurrentQueue<ulong>();
            var queue = new CardImageQueue((id, _) =>
            {
                if (id == 1)
                    throw new InvalidOperationException("boom");
                drawn.Enqueue(id);
                return Task.FromResult(CardDrawing.Drawn);
            }, logger, TimeSpan.Zero, TimeSpan.FromMilliseconds(20));
            using var stop = new CancellationTokenSource();
            queue.Start(stop.Token);

            queue.Enqueue(1, false);
            var until = DateTime.UtcNow.AddSeconds(10);
            while (logger.Messages.IsEmpty && DateTime.UtcNow < until)
                await Task.Delay(20);
            queue.Enqueue(2, false);
            while (drawn.IsEmpty && DateTime.UtcNow < until)
                await Task.Delay(20);
            stop.Cancel();

            Assert.Equal(LogKind.Error, LoggerExtensions.Parse(Assert.Single(logger.Messages)).Kind);
            Assert.Equal(new[] { 2UL }, drawn);
        }

        private sealed class GatedImages : ImageProcessing
        {
            public int CardRenders;
            public int ProfileRenders;
            public bool PictureFails;
            public TaskCompletionSource Gate;
            public TaskCompletionSource Started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            public GatedImages() : base(null, null, true) { }

            public override async Task<Image> GetWaifuCardAsync(Card card, CardPictureCache pictures)
            {
                Interlocked.Increment(ref CardRenders);
                Started.TrySetResult();
                if (Gate != null)
                    await Gate.Task;
                if (PictureFails)
                    pictures.Add("http://picture", null);
                return new Image<Rgba32>(475, 667);
            }

            public override Task<Image> GetWaifuInProfileCardAsync(Card card, CardPictureCache pictures)
            {
                Interlocked.Increment(ref ProfileRenders);
                return Task.FromResult<Image>(new Image<Rgba32>(475, 667));
            }
        }

        private static string[] Files(Card card) => new[]
        {
            $"{Dir.Cards}/{card.Id}.webp", $"{Dir.CardsInProfiles}/{card.Id}.webp",
        };

        private static void Cleanup(Card card)
        {
            foreach (var path in Files(card).Where(File.Exists))
                File.Delete(path);
        }

        private static Waifu NewWaifu(ImageProcessing images, CardImageQueue queue = null)
            => new Waifu(images, null, null, new ListLogger(), null, null, null, new FixedTime(), null, null, new FakeConfig()) { ImageQueue = queue };

        [Fact]
        public async Task DeletedImages_AreQueued_ExceptForRemovedCards()
        {
            Dir.Create();
            var clock = new Clock();
            var queue = Queue(clock);
            var waifu = NewWaifu(new GatedImages(), queue);
            var card = new Card { Id = NewId(), Quality = Quality.Broken };
            var removed = new Card { Id = NewId(), Quality = Quality.Broken };
            try
            {
                await waifu.GenerateAndSaveCardAsync(card);
                await waifu.GenerateAndSaveCardAsync(removed);

                waifu.DeleteCardImageIfExist(card);
                waifu.DeleteCardImageIfExist(removed, false);

                Assert.All(Files(card).Concat(Files(removed)), path => Assert.False(File.Exists(path), path));
                clock.Now = 10000;
                Assert.Equal(new[] { (card.Id, false) }, queue.TakeDue());
            }
            finally
            {
                Cleanup(card);
                Cleanup(removed);
            }
        }

        // the drawing read the card before the change and saves its images after the delete, so they have to be drawn again
        [Fact]
        public async Task DeletingDuringADrawing_QueuesAForcedRedraw()
        {
            Dir.Create();
            var clock = new Clock();
            var queue = Queue(clock);
            var images = new GatedImages { Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
            var waifu = NewWaifu(images, queue);
            var card = new Card { Id = NewId(), Quality = Quality.Broken };
            try
            {
                var drawing = waifu.GenerateAndSaveCardAsync(card);
                await images.Started.Task;

                waifu.DeleteCardImageIfExist(card);
                images.Gate.SetResult();
                await drawing;
                Assert.All(Files(card), path => Assert.True(File.Exists(path), path));

                clock.Now = 10000;
                var due = Assert.Single(queue.TakeDue());
                Assert.Equal((card.Id, true), due);

                await waifu.DrawInBackgroundAsync(card, due.Force);
                Assert.Equal(2, images.CardRenders);
                Assert.Equal(2, images.ProfileRenders);
            }
            finally
            {
                Cleanup(card);
            }
        }

        [Fact]
        public async Task BackgroundDrawing_SkipsExistingImages_UnlessForced()
        {
            Dir.Create();
            var images = new GatedImages();
            var waifu = NewWaifu(images);
            var card = new Card { Id = NewId(), Quality = Quality.Broken };
            try
            {
                Assert.Equal(CardDrawing.Drawn, await waifu.DrawInBackgroundAsync(card, false));
                Assert.Equal(CardDrawing.AlreadyDrawn, await waifu.DrawInBackgroundAsync(card, false));
                Assert.Equal(1, images.CardRenders);
                Assert.Equal(1, images.ProfileRenders);
                Assert.All(Files(card), path => Assert.True(File.Exists(path), path));

                Assert.Equal(CardDrawing.Drawn, await waifu.DrawInBackgroundAsync(card, true));
                Assert.Equal(2, images.CardRenders);
                Assert.Equal(2, images.ProfileRenders);
            }
            finally
            {
                Cleanup(card);
            }
        }

        [Fact]
        public async Task BackgroundDrawing_WithoutThePicture_SavesNothing_AndForcedRemovesTheOldImages()
        {
            Dir.Create();
            var images = new GatedImages { PictureFails = true };
            var waifu = NewWaifu(images);
            var card = new Card { Id = NewId(), Quality = Quality.Broken };
            try
            {
                Assert.Equal(CardDrawing.NoPicture, await waifu.DrawInBackgroundAsync(card, false));
                Assert.All(Files(card), path => Assert.False(File.Exists(path), path));
                Assert.Equal(0, images.ProfileRenders);

                images.PictureFails = false;
                await waifu.GenerateAndSaveCardAsync(card);
                Assert.All(Files(card), path => Assert.True(File.Exists(path), path));

                images.PictureFails = true;
                await waifu.DrawInBackgroundAsync(card, true);
                Assert.All(Files(card), path => Assert.False(File.Exists(path), path));
            }
            finally
            {
                Cleanup(card);
            }
        }

        // a drawing on demand keeps saving the card without the picture, as before
        [Fact]
        public async Task DrawingOnDemand_WithoutThePicture_StillSavesTheCard()
        {
            Dir.Create();
            using var server = new LocalHttpServer(ctx =>
            {
                ctx.Response.StatusCode = 404;
                ctx.Response.Close();
                return Task.CompletedTask;
            });
            var waifu = NewWaifu(new ImageProcessing(null, null, true));
            var card = new Card { Id = NewId(), Quality = Quality.Broken, Image = server.BaseUrl + "char.png" };
            try
            {
                await waifu.DrawInBackgroundAsync(card, false);
                Assert.All(Files(card), path => Assert.False(File.Exists(path), path));

                await waifu.GenerateAndSaveCardAsync(card);
                Assert.All(Files(card), path => Assert.True(File.Exists(path), path));
            }
            finally
            {
                Cleanup(card);
            }
        }

        [Fact]
        public async Task CardImage_NotReadyInTime_IsNull_AndIsSavedLater()
        {
            Dir.Create();
            var images = new GatedImages { Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
            var queue = Queue(new Clock());
            var waifu = NewWaifu(images, queue);
            var card = new Card { Id = NewId(), Quality = Quality.Broken };
            try
            {
                Assert.Null(await waifu.GetCardImageAsync(card, CardImageType.Profile, TimeSpan.FromMilliseconds(100)));
                Assert.Contains("1 requests not ready in time (503)", queue.FlushReport());

                // a second request waits for the same drawing instead of failing at once
                var second = waifu.GetCardImageAsync(card, CardImageType.Profile, TimeSpan.FromSeconds(10));
                images.Gate.SetResult();

                Assert.Equal(Files(card)[1], await second);
                Assert.All(Files(card), path => Assert.True(File.Exists(path), path));
                Assert.Equal(1, images.CardRenders);
                Assert.Equal(Files(card)[0], await waifu.GetCardImageAsync(card, CardImageType.Small, TimeSpan.FromSeconds(1)));
                Assert.False(File.Exists($"{Dir.CardsMiniatures}/{card.Id}.webp"));
            }
            finally
            {
                Cleanup(card);
            }
        }
    }

    public class CardImageApiTests : IDisposable
    {
        private readonly ApiServer _api = new ApiServer();

        public void Dispose() => _api.Dispose();

        [Theory]
        [InlineData("bogus")]
        [InlineData("7")]
        public async Task UnknownImageType_Returns400(string type)
        {
            var res = await _api.Client.GetAsync($"/api/waifu/card/5?type={type}");

            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        }

        // the files are checked before the database, which the test server does not have
        [Theory]
        [InlineData("", "", "webp")]
        [InlineData("?type=profile", "profile/", "webp")]
        [InlineData("?type=Small", "", "webp")]
        [InlineData("?type=Profile", "profile/", "gif")]
        public async Task ExistingImages_RedirectToTheCdn_ForEveryType(string query, string folder, string ext)
        {
            Dir.Create();
            var id = 960000000000UL + (ulong)Environment.TickCount64 % 1000000;
            var files = new[] { $"{Dir.Cards}/{id}.{ext}", $"{Dir.CardsInProfiles}/{id}.{ext}" };
            try
            {
                foreach (var path in files)
                    File.WriteAllBytes(path, new byte[] { 1 });

                var res = await _api.Client.GetAsync($"/api/waifu/card/{id}{query}");

                Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
                Assert.Equal($"https://cdn2.shinden.eu/{folder}{id}.{ext}", res.Headers.Location?.ToString());
            }
            finally
            {
                foreach (var path in files.Where(File.Exists))
                    File.Delete(path);
            }
        }
    }
}
