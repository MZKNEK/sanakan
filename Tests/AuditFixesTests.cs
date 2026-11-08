using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Discord.Commands;
using Microsoft.EntityFrameworkCore;
using Moq;
using Sanakan;
using Sanakan.Api.Controllers;
using Sanakan.Config.Model;
using Sanakan.Database;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Preconditions;
using Sanakan.Services;
using Sanakan.Services.PocketWaifu;
using Sanakan.Services.ScamImages;
using Sanakan.TypeReaders;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Artifacts
{
    public class UltimatePowerLimitTests
    {
        private static Card Ultimate(long bonus) => new Card { FromFigure = true, Quality = Quality.Alpha, AttackBonus = (int)bonus };

        [Fact]
        public void ManyPills_CannotExceedLimit()
        {
            var card = Ultimate(4800);
            var points = 90L * ItemType.IncreaseUltimateAll.UltimatePowerPoints();

            Assert.Equal(ExecutionResult.EStatus.Error, card.CanUpgradePower(points).Status);
        }

        [Fact]
        public void PillsUpToLimit_AreAllowed()
        {
            Assert.NotEqual(ExecutionResult.EStatus.Error, Ultimate(4800).CanUpgradePower(100).Status);
            Assert.Equal(ExecutionResult.EStatus.Error, Ultimate(4800).CanUpgradePower(101).Status);
        }

        [Fact]
        public void PowerPoints_MatchAppliedBonuses()
        {
            Assert.Equal(5, ItemType.IncreaseUltimateAttack.UltimatePowerPoints());
            Assert.Equal(3, ItemType.IncreaseUltimateDefence.UltimatePowerPoints());
            Assert.Equal(5, ItemType.IncreaseUltimateHealth.UltimatePowerPoints());
            Assert.Equal(12, ItemType.IncreaseUltimateAll.UltimatePowerPoints());
        }

        [Theory]
        [InlineData(ItemType.DereReRoll)]
        [InlineData(ItemType.CardParamsReRoll)]
        public void RerollItems_AreAlwaysUsedOnce(ItemType type)
        {
            Assert.True(type.ShouldForceSingleUseOnCheck());
        }
    }

    public class HaremTagFilterTests
    {
        private static Waifu NewWaifu() => new Waifu(null, null, null, new ListLogger(), null, null, null, new FixedTime(), null, null, new FakeConfig());

        private static Card WithTags(ulong id, params string[] tags)
            => new Card { Id = id, Tags = tags.Select(x => new Tag { Name = x }).ToList() };

        private static readonly List<Card> Cards = new List<Card>
        {
            WithTags(1, "konie", "ulubione"),
            WithTags(2, "ulubione"),
            WithTags(3, "konie"),
            WithTags(4),
        };

        [Fact]
        public void Tag_WithManyTags_KeepsCardsWithAllOfThem()
        {
            var res = NewWaifu().GetListInRightOrder(Cards, HaremType.Tag, "konie ulubione");
            Assert.Equal(new ulong[] { 1 }, res.Select(x => x.Id));
        }

        [Fact]
        public void NoTag_WithManyTags_ExcludesCardsWithAnyOfThem()
        {
            var res = NewWaifu().GetListInRightOrder(Cards, HaremType.NoTag, "konie ulubione");
            Assert.Equal(new ulong[] { 4 }, res.Select(x => x.Id));
        }
    }

    public class ProfileImageSaveTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), $"sanakan-split-{Guid.NewGuid():N}");

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        private static Func<System.Net.HttpListenerContext, Task> ServePng(int width, int height)
        {
            using var image = new Image<Rgba32>(width, height);
            using var ms = new MemoryStream();
            image.SaveAsPng(ms);
            var body = ms.ToArray();
            return async ctx =>
            {
                ctx.Response.ContentType = "image/png";
                ctx.Response.ContentLength64 = body.Length;
                await ctx.Response.OutputStream.WriteAsync(body);
            };
        }

        [Theory]
        [InlineData(1920, 1080)]
        [InlineData(500, 1200)]
        [InlineData(800, 500)]
        [InlineData(300, 200)]
        public async Task BackgroundAndStyle_AnyAspectRatio_IsSplitToExactSizes(int width, int height)
        {
            using var server = new LocalHttpServer(ServePng(width, height));
            var top = Path.Combine(_dir, "top.webp");
            var bot = Path.Combine(_dir, "bot.webp");

            await new ImageProcessing(null, null, true).SplitImageToBackgroundAndStyleAsync(server.BaseUrl + "img.png",
                top, new Size(750, 160), bot, new Size(750, 340));

            using (var image = Image.Load(top))
                Assert.Equal((750, 160), (image.Width, image.Height));
            using (var image = Image.Load(bot))
                Assert.Equal((750, 340), (image.Width, image.Height));
        }

        [Fact]
        public async Task RejectedDownload_DoesNotSetCustomBorder()
        {
            using var server = new LocalHttpServer(ServePng(8193, 1));
            var card = new Card { Id = 987654321 };

            await Assert.ThrowsAnyAsync<Exception>(() =>
                new ImageProcessing(null, null, true).SaveCardBorderImageFromUrlAsync(server.BaseUrl + "wide.png", card));

            Assert.Null(card.CustomBorder);
        }

        [Fact]
        public async Task RejectedDownload_DoesNotSetCustomImage()
        {
            using var server = new LocalHttpServer(ServePng(8193, 1));
            var card = new Card { Id = 987654322 };

            await Assert.ThrowsAnyAsync<Exception>(() =>
                new ImageProcessing(null, null, true).SaveCardImageFromUrlAsync(server.BaseUrl + "wide.png", card));

            Assert.Null(card.CustomImage);
        }
    }

    public class SiteProfileCacheTests : IDisposable
    {
        private const ulong DiscordId = 96001;
        private const ulong ShindenId = 96777;

        private readonly string _path = Path.Combine(Path.GetTempPath(), $"sanakan-profile-{Guid.NewGuid():N}.db");
        private readonly FakeConfig _config = new FakeConfig();
        // cache entries expire relative to the real clock
        private readonly FixedTime _time = new FixedTime { Value = DateTime.Now };

        public SiteProfileCacheTests()
        {
            _config.Model.DatabaseProvider = DatabaseProvider.Sqlite;
            _config.Model.ConnectionString = $"Data Source={_path}";

            using var db = new DatabaseContext(_config);
            db.EnsureReady();
            var user = new User().Default(DiscordId, _time.Now());
            user.Shinden = ShindenId;
            db.Users.Add(user);
            db.SaveChanges();
        }

        public void Dispose()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(_path); } catch (IOException) { }
        }

        private async Task<long> ProfileCtAsync()
        {
            TagHelper tags;
            using (var db = new DatabaseContext(_config))
                tags = await TagHelper.CreateAsync(db);

            var waifu = new Waifu(null, null, null, new ListLogger(), null, null, null, _time, null, null, _config);
            var controller = ControllerHarness.Attach(new WaifuController(null, waifu, new FakeExecutor(), tags, _config, _time, null, null, null, null));
            var profile = (await controller.GetUserWaifuProfileAsync(ShindenId)).Value;
            return profile.Wallet["CT"];
        }

        [Fact]
        public async Task DeckChange_RefreshesCachedProfile()
        {
            Assert.Equal(0, await ProfileCtAsync());

            using (var db = new DatabaseContext(_config))
            {
                var deck = await db.GameDecks.FirstAsync(x => x.UserId == DiscordId);
                deck.CTCnt = 7;
                await db.SaveChangesAsync();
            }

            Assert.Equal(7, await ProfileCtAsync());
        }
    }

    public class ProfileConfigAliasTests
    {
        private static TypeReaderResult Read(string input)
        {
            var message = new Mock<Discord.IUserMessage>();
            message.SetupGet(x => x.Attachments).Returns(new List<Discord.IAttachment>());
            var context = new Mock<ICommandContext>();
            context.SetupGet(x => x.Message).Returns(message.Object);
            return new ProfileConfigTypeReader().ReadAsync(context.Object, input, null).GetAwaiter().GetResult();
        }

        [Theory]
        [InlineData("overlay visibility", "widoczność nakładki")]
        [InlineData("ultra overlay visibility", "widoczność ultra nakładki")]
        public void EnglishAlias_WorksLikePolishName(string english, string polish)
        {
            var en = Read(english);
            var pl = Read(polish);

            Assert.True(pl.IsSuccess);
            Assert.True(en.IsSuccess, en.ErrorReason);
            Assert.Equal(((ProfileConfig)pl.Values.First().Value).Type, ((ProfileConfig)en.Values.First().Value).Type);
        }

        [Fact]
        public void ShortAlias_StillWorks()
        {
            Assert.Equal(ProfileConfigType.Overlay, ((ProfileConfig)Read("overlay https://sanakan.pl/a.png").Values.First().Value).Type);
        }
    }

    public class CooldownRefundModule : ModuleBase<ICommandContext>
    {
        [Command("cooldowntest"), DelayNextUseBy(30)]
        public Task RunAsync() => Task.CompletedTask;
    }

    public class CooldownRefundTests
    {
        [Fact]
        public async Task Refund_RemovesUsersCooldown()
        {
            var module = await new CommandService().AddModuleAsync<CooldownRefundModule>(null);
            var command = module.Commands.Single();
            var entries = (ConcurrentDictionary<(string, ulong), DateTime>)typeof(DelayNextUseBy)
                .GetField("_entries", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);

            entries[(command.Name, 555UL)] = DateTime.Now;
            entries[(command.Name, 556UL)] = DateTime.Now;

            DelayNextUseBy.Refund(command, 555);

            Assert.False(entries.ContainsKey((command.Name, 555UL)));
            Assert.True(entries.TryRemove((command.Name, 556UL), out _));
        }
    }

    public class ExpeditionAffectionTests
    {
        [Fact]
        public void UltimateMedium_ReportsOnlyAffectionActuallyLost()
        {
            var card = new Card { Affection = 0, Expedition = CardExpedition.UltimateMedium };
            double cost = 30;

            card.DecAffectionOnExpeditionBy(ref cost);

            Assert.Equal(-10, card.Affection);
            Assert.Equal(10, cost);
        }
    }

    public class PerceptualHashFrameTests
    {
        private static Image<Rgba32> Gradient(bool inverted)
        {
            var image = new Image<Rgba32>(64, 64);
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    var v = (byte)((x * 4 + y * 2) % 256);
                    if (inverted) v = (byte)(255 - v);
                    image[x, y] = new Rgba32(v, v, v);
                }
            return image;
        }

        [Fact]
        public void AnimatedImage_HashMatchesFullDecode()
        {
            using var first = Gradient(false);
            using var second = Gradient(true);
            using var animation = first.Clone();
            animation.Frames.AddFrame(second.Frames.RootFrame);

            using var ms = new MemoryStream();
            animation.SaveAsGif(ms);

            ms.Position = 0;
            ulong[] allFrames;
            using (var decoded = Image.Load<Rgba32>(ms))
            {
                Assert.Equal(2, decoded.Frames.Count);
                allFrames = PerceptualHash.FromImageOrientations(decoded);
            }

            ms.Position = 0;
            Assert.Equal(allFrames, PerceptualHash.FromStreamOrientations(ms));
        }
    }

    public class ScamAnimationSkipTests
    {
        private static Image<Rgba32> Pattern(bool inverted)
        {
            var image = new Image<Rgba32>(64, 64);
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    var v = (byte)(x < 32 ? 30 : 220);
                    if (inverted) v = (byte)(255 - v);
                    image[x, y] = new Rgba32(v, v, v);
                }
            return image;
        }

        private static MemoryStream Gif(Image image)
        {
            var ms = new MemoryStream();
            image.SaveAsGif(ms);
            ms.Position = 0;
            return ms;
        }

        [Fact]
        public void AnimatedWebp_IsDetectedAsAnimated()
        {
            using var first = Pattern(false);
            using var second = Pattern(true);
            using var animation = first.Clone();
            animation.Frames.AddFrame(second.Frames.RootFrame);

            using var ms = new MemoryStream();
            animation.SaveAsWebp(ms);
            ms.Position = 0;
            Assert.True(PerceptualHash.IsAnimated(ms));

            using var still = new MemoryStream();
            first.SaveAsWebp(still);
            still.Position = 0;
            Assert.False(PerceptualHash.IsAnimated(still));
        }

        [Fact]
        public void AnimatedImage_IsNeverMatched()
        {
            var path = Path.Combine(Path.GetTempPath(), $"sanakan-scam-anim-{Guid.NewGuid():N}.txt");
            try
            {
                using var first = Pattern(false);
                using var second = Pattern(true);
                var store = new ScamImageStore(path);
                using (var still = Gif(first))
                    store.Add(PerceptualHash.FromStream(still));

                var scanner = new ScamImageScanner(new ImageProcessing(null, null, true), store, null);

                using var animation = first.Clone();
                animation.Frames.AddFrame(second.Frames.RootFrame);
                using (var animated = Gif(animation))
                {
                    Assert.True(PerceptualHash.IsAnimated(animated));
                    Assert.Equal(0, animated.Position);
                    Assert.False(scanner.IsScamStream(animated));
                }

                using (var still = Gif(first))
                    Assert.True(scanner.IsScamStream(still));
            }
            finally
            {
                try { File.Delete(path); } catch (IOException) { }
            }
        }
    }

    public class LevelUpBadgeTests
    {
        private static Func<System.Net.HttpListenerContext, Task> ServeOpaqueAvatar()
        {
            using var image = new Image<Rgb24>(300, 300, new Rgb24(200, 30, 30));
            using var ms = new MemoryStream();
            image.SaveAsPng(ms);
            var body = ms.ToArray();
            return async ctx =>
            {
                ctx.Response.ContentType = "image/png";
                ctx.Response.ContentLength64 = body.Length;
                await ctx.Response.OutputStream.WriteAsync(body);
            };
        }

        [Fact]
        public async Task OpaqueAvatar_HasNoBlackCorners_AndBadgeIsDoubleSize()
        {
            using var server = new LocalHttpServer(ServeOpaqueAvatar());

            using var badge = await new ImageProcessing(null, null, true).GetLevelUpBadgeAsync("gracz", 2,
                server.BaseUrl + "avatar.png", new Discord.Color(30, 200, 30));

            Assert.Equal(280, badge.Height);

            // (44, 44) lies between the avatar's rounded corner and the frame's, so it must show the frame color, not black
            var corner = badge[44, 44];
            Assert.True(corner.G > 150 && corner.R < 100, $"corner pixel was {corner}");

            var center = badge[140, 140];
            Assert.True(center.R > 150 && center.G < 100, $"avatar pixel was {center}");
        }
    }

    public class CacheTagRegistryTests
    {
        private const long Idle = 1000;

        [Fact]
        public void Sweep_RemovesOnlyIdleTags_AndCancelsThem()
        {
            var registry = new CacheTagRegistry();
            var oldToken = registry.Use("old", 0);
            var freshToken = registry.Use("fresh", 1500);

            Assert.Equal(1, registry.Sweep(2000, Idle, 100));

            Assert.Equal(1, registry.Count);
            Assert.True(oldToken.IsCancellationRequested);
            Assert.False(freshToken.IsCancellationRequested);
        }

        [Fact]
        public void Sweep_OverLimit_RemovesLeastRecentlyUsedHalf()
        {
            var registry = new CacheTagRegistry();
            var tokens = Enumerable.Range(0, 10).Select(i => registry.Use($"t{i}", i)).ToList();

            registry.Sweep(10, Idle, 8);

            Assert.Equal(4, registry.Count);
            Assert.All(tokens.Take(6), x => Assert.True(x.IsCancellationRequested));
            Assert.All(tokens.Skip(6), x => Assert.False(x.IsCancellationRequested));
        }

        [Fact]
        public void UsingTag_KeepsItAlive()
        {
            var registry = new CacheTagRegistry();
            registry.Use("a", 0);
            var token = registry.Use("a", 1800);

            Assert.Equal(0, registry.Sweep(2000, Idle, 100));
            Assert.False(token.IsCancellationRequested);
        }

        [Fact]
        public void Expire_CancelsAndRemoves()
        {
            var registry = new CacheTagRegistry();
            var token = registry.Use("a", 0);

            registry.Expire("a");

            Assert.True(token.IsCancellationRequested);
            Assert.Equal(0, registry.Count);
            Assert.False(registry.Use("a", 1).IsCancellationRequested);
        }

        [Fact]
        public void ShouldSweep_AfterIntervalOrOverLimit()
        {
            var registry = new CacheTagRegistry();
            registry.Sweep(1000, Idle, 100);

            Assert.False(registry.ShouldSweep(1500, 1000, 100));
            Assert.True(registry.ShouldSweep(2000, 1000, 100));

            registry.Use("a", 1500);
            registry.Use("b", 1500);
            Assert.True(registry.ShouldSweep(1500, 1000, 1));
        }

        [Fact]
        public void EntryOptions_NeverOutliveMaxLifetime()
        {
            var max = QueryCache.MaxEntryLifetime;

            Assert.Equal(max, QueryCache.Copy(new Microsoft.Extensions.Caching.Memory.MemoryCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(5) }).AbsoluteExpirationRelativeToNow);
            Assert.Equal(max, QueryCache.Copy(new Microsoft.Extensions.Caching.Memory.MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(3) }).AbsoluteExpirationRelativeToNow);
            Assert.True(QueryCache.Copy(new Microsoft.Extensions.Caching.Memory.MemoryCacheEntryOptions { AbsoluteExpiration = DateTimeOffset.Now.AddDays(3) }).AbsoluteExpiration <= DateTimeOffset.Now + max);

            var shorter = QueryCache.Copy(new Microsoft.Extensions.Caching.Memory.MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15) });
            Assert.Equal(TimeSpan.FromMinutes(15), shorter.AbsoluteExpirationRelativeToNow);
        }
    }

    public class TokenAttemptWindowTests
    {
        [Fact]
        public void FailuresOlderThanLockDuration_DoNotAddUp()
        {
            var time = new FixedTime { Value = new DateTime(2026, 1, 1) };
            var guard = new Sanakan.Api.TokenAttemptGuard(time);

            for (int i = 0; i < Sanakan.Api.TokenAttemptGuard.MaxAttempts; i++)
            {
                guard.RegisterFailure("ip");
                time.Value = time.Value.AddDays(2);
            }

            Assert.False(guard.IsLocked("ip", out _));
        }

        [Fact]
        public void FailuresWithinLockDuration_Lock()
        {
            var time = new FixedTime { Value = new DateTime(2026, 1, 1) };
            var guard = new Sanakan.Api.TokenAttemptGuard(time);

            for (int i = 0; i < Sanakan.Api.TokenAttemptGuard.MaxAttempts; i++)
            {
                guard.RegisterFailure("ip");
                time.Value = time.Value.AddHours(5);
            }

            Assert.True(guard.IsLocked("ip", out _));
        }
    }

    public class TokenAttemptUnlockTests
    {
        private static Sanakan.Api.TokenAttemptGuard Locked(params string[] keys)
        {
            var guard = new Sanakan.Api.TokenAttemptGuard(new FixedTime { Value = DateTime.Now });
            foreach (var key in keys)
                for (int i = 0; i < Sanakan.Api.TokenAttemptGuard.MaxAttempts; i++)
                    guard.RegisterFailure(key);
            return guard;
        }

        [Fact]
        public void GetLocked_ListsLockedClients()
        {
            var guard = Locked("1.1.1.1", "2.2.2.2");
            guard.RegisterFailure("3.3.3.3");

            Assert.Equal(new[] { "1.1.1.1", "2.2.2.2" }, guard.GetLocked().Select(x => x.Key).OrderBy(x => x));
        }

        [Fact]
        public void Unlock_OneClient()
        {
            var guard = Locked("1.1.1.1", "2.2.2.2");

            Assert.Equal(1, guard.Unlock("1.1.1.1"));
            Assert.Equal(0, guard.Unlock("9.9.9.9"));

            Assert.False(guard.IsLocked("1.1.1.1", out _));
            Assert.True(guard.IsLocked("2.2.2.2", out _));
        }

        [Fact]
        public void Unlock_All_ClearsLocksAndCounters()
        {
            var guard = Locked("1.1.1.1");
            guard.RegisterFailure("3.3.3.3");
            guard.RegisterFailure("3.3.3.3");

            Assert.Equal(2, guard.Unlock());
            Assert.Empty(guard.GetLocked());

            guard.RegisterFailure("3.3.3.3");
            Assert.False(guard.IsLocked("3.3.3.3", out _));
        }

        [Fact]
        public void ApiHost_UsesBotGuardInstance()
        {
            var config = new FakeConfig();
            var guard = new Sanakan.Api.TokenAttemptGuard(new FixedTime());
            var bot = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
            Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<Sanakan.Api.ITokenAttemptGuard>(bot, guard);

            using var host = Sanakan.Api.BotWebHost.CreateHost(config, bot, "http://localhost:0");

            Assert.Same(guard, host.Services.GetService(typeof(Sanakan.Api.ITokenAttemptGuard)));
        }
    }
}
