using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using Sanakan.Api.Controllers;
using Sanakan.Config.Model;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services;
using Sanakan.Services.Executor;
using Sanakan.Services.PocketWaifu;
using Sanakan.Services.Session;
using Sanakan.Services.Supervisor;
using Xunit;

namespace Artifacts
{
    public class ShopOverflowTests
    {
        private static Waifu NewWaifu() => new Waifu(null, null, null, new ListLogger(), null, null, null, new FixedTime(), null, null, new FakeConfig());

        [Fact]
        public void ShopCost_DoesNotWrapToNegative()
        {
            // 3 * 715827883 przekracza int.MaxValue
            var cost = Waifu.GetShopCost(715827883, 3);

            Assert.Equal(2147483649L, cost);
            Assert.False(NewWaifu().CheckIfUserCanBuy(ShopType.Normal, new User { TcCnt = 100 }, cost));
        }

        [Fact]
        public void ShopCost_MaxValues_StayPositive()
        {
            Assert.Equal((long)int.MaxValue * 99999, Waifu.GetShopCost(int.MaxValue, 99999));
        }

        [Fact]
        public void RemovingLargeCost_DoesNotGiveMoney()
        {
            var user = new User { TcCnt = 5_000_000_000 };
            NewWaifu().RemoveMoneyFromUser(ShopType.Normal, user, Waifu.GetShopCost(1_000_000, 3000));
            Assert.Equal(2_000_000_000, user.TcCnt);
        }
    }

    public class GalleryOverflowTests
    {
        [Fact]
        public void GalleryPrice_DoesNotWrapToNegative()
        {
            // 100 * 21474837 przekracza int.MaxValue
            Assert.Equal(2147483700L, UserExtension.CalculatePriceOfIncGallery(21474837));
            Assert.Equal(100L * uint.MaxValue, UserExtension.CalculatePriceOfIncGallery(uint.MaxValue));
        }

        [Fact]
        public void GalleryLimit_RejectsCountThatOverflowsSlots()
        {
            var deck = new GameDeck { CardsInGallery = 10 };

            Assert.True(deck.CanIncGalleryLimit(3));
            Assert.False(deck.CanIncGalleryLimit(0));
            Assert.False(deck.CanIncGalleryLimit(uint.MaxValue));
            Assert.False(deck.CanIncGalleryLimit((uint)(int.MaxValue / 5)));
        }
    }

    public class QueueFullApiTests
    {
        [Fact]
        public async Task UpdateCardInfo_WhenQueueIsFull_Returns503()
        {
            var executor = new FakeExecutor { Accept = false };
            var controller = ControllerHarness.Attach(new WaifuController(null, null, executor, null, new FakeConfig(), new FixedTime(),
                new MemoryCache(new MemoryCacheOptions()), null, null));

            var result = await controller.UpdateCardInfoAsync(5, new Sanakan.Api.Models.CharacterCardInfoUpdate());

            Assert.Equal(503, result.StatusOf());
            Assert.Contains("queue is full", result.MessageOf());
        }

        [Fact]
        public void Controllers_NeverIgnoreTryAddResult()
        {
            var dir = Path.Combine(RepoPaths.Src, "Api", "Controllers");
            var ignored = Directory.GetFiles(dir, "*.cs")
                .SelectMany(f => File.ReadAllLines(f).Select((line, i) => (file: Path.GetFileName(f), line: line.Trim(), no: i + 1)))
                .Where(x => x.line.StartsWith("await _executor.TryAdd("))
                .Select(x => $"{x.file}:{x.no}")
                .ToList();

            Assert.Empty(ignored);
        }
    }

    public class SupervisorCommandTests
    {
        [Theory]
        [InlineData("isSuper", false, Supervisor.SupervisionCommand.Status)]
        [InlineData("  ISSUPER ", false, Supervisor.SupervisionCommand.Status)]
        [InlineData("activesuper", true, Supervisor.SupervisionCommand.Activate)]
        [InlineData("activesuper", false, Supervisor.SupervisionCommand.None)]
        [InlineData("isSuper?", false, Supervisor.SupervisionCommand.None)]
        [InlineData("", false, Supervisor.SupervisionCommand.None)]
        [InlineData(null, false, Supervisor.SupervisionCommand.None)]
        public void ParseCommand(string content, bool debug, Supervisor.SupervisionCommand expected)
        {
            Assert.Equal(expected, Supervisor.ParseSupervisionCommand(content, debug));
        }
    }

    public class SubscriptionTests
    {
        private static readonly DateTime Now = new DateTime(2026, 10, 4, 12, 0, 0);

        [Fact]
        public void Extend_NewStatus_StartsFromNow()
        {
            var status = StatusType.Globals.NewTimeStatus(1);

            status.ExtendSubscription(Now);

            Assert.Equal(Now.AddMonths(1), status.EndsAt);
            Assert.True(status.BValue);
            Assert.True(status.IsActive(Now.AddDays(20)));
        }

        [Fact]
        public void Extend_LongExpiredStatus_StartsFromNow()
        {
            var status = new TimeStatus { Type = StatusType.Globals, EndsAt = Now.AddYears(-2), BValue = false };

            status.ExtendSubscription(Now);

            Assert.Equal(Now.AddMonths(1), status.EndsAt);
        }

        [Fact]
        public void Extend_ActiveStatus_AddsToRemainingTime()
        {
            var status = new TimeStatus { Type = StatusType.Globals, EndsAt = Now.AddDays(10), BValue = true };

            status.ExtendSubscription(Now);

            Assert.Equal(Now.AddDays(10).AddMonths(1), status.EndsAt);
        }

        [Fact]
        public void Start_MarksSubscriptionForExpiryCheck()
        {
            var status = StatusType.RainbowColor.NewTimeStatus(1);

            status.StartSubscription(Now);

            Assert.True(status.BValue);
            Assert.Equal(Now.AddMonths(1), status.EndsAt);
        }
    }

    public class WishlistCountTests
    {
        [Theory]
        [InlineData(WishlistObjectType.Character, true)]
        [InlineData(WishlistObjectType.Title, false)]
        [InlineData(WishlistObjectType.Card, false)]
        public void OnlyCharactersAffectWishlistCount(WishlistObjectType type, bool expected)
        {
            Assert.Equal(expected, new WishlistObject { Type = type, ObjectId = 5 }.AffectsWishlistCount());
        }
    }

    public class SessionManagerConcurrencyTests
    {
        private static SessionManager NewManager()
        {
            var manager = new SessionManager(new DiscordSocketClient(), new FakeExecutor(), new ListLogger());
            manager.Initialize(new EmptyServiceProvider());
            return manager;
        }

        [Fact]
        public async Task ParallelAdd_ForSameOwner_AddsExactlyOne()
        {
            var manager = NewManager();
            var user = Users.Discord(1);

            var results = await Task.WhenAll(Enumerable.Range(0, 200)
                .Select(_ => Task.Run(() => manager.TryAddSession(new Session(user)))));

            Assert.Equal(1, results.Count(x => x));
        }

        [Fact]
        public async Task ParallelAddCheckAndKill_DoesNotThrow()
        {
            var manager = NewManager();

            await Task.WhenAll(Enumerable.Range(0, 2000).Select(i => Task.Run(async () =>
            {
                var user = Users.Discord((ulong)(i % 50) + 1);
                await manager.TryAddSession(new Session(user));
                manager.SessionExist(user, typeof(Session));
                manager.SessionExist(new Session(user));
                await manager.KillSessionIfExistAsync(new Session(user));
            })));
        }

        [Fact]
        public async Task SessionDisposedTwice_RunsOnDisposeOnce()
        {
            var manager = NewManager();
            var user = Users.Discord(1);
            var disposed = 0;
            await manager.TryAddSession(new Session(user) { OnDispose = () => { Interlocked.Increment(ref disposed); return Task.CompletedTask; } });

            await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => manager.KillSessionIfExistAsync(new Session(user)))));

            Assert.Equal(1, disposed);
        }

        [Fact]
        public async Task DisposedSession_IsInvalidButStillKnowsOwners()
        {
            var session = new Session(Users.Discord(1));
            session.MarkAsAdded();

            await session.DisposeAsync();

            Assert.False(session.IsValid());
            Assert.True(session.IsOwner(Users.Discord(1)));
            Assert.Equal(1ul, session.GetOwner().Id);
            Assert.True(await session.GetExecutable(null).ExecuteAsync(new EmptyServiceProvider()));
        }
    }

    public class UserBasedExecutorPriorityTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

        private static Executable Gated(string name, ulong owner, TaskCompletionSource gate, List<string> log, Priority priority = Priority.Normal)
            => new Executable(name, new Func<Task>(async () =>
            {
                lock (log) log.Add($"start {name}");
                await gate.Task;
            }), owner, priority);

        [Fact]
        public async Task HighPriority_RunsBeforeEarlierNormalTaskOfSameOwner()
        {
            var executor = new UserBasedExecutor(new ListLogger());
            executor.Initialize(new EmptyServiceProvider());
            var log = new List<string>();
            var busy = new TaskCompletionSource();
            var rest = new TaskCompletionSource();
            rest.SetResult();

            await executor.TryAdd(Gated("busy", 1, busy, log), Timeout);
            await UserBasedExecutorBehaviorTests.WaitUntil(() => UserBasedExecutorBehaviorTests.Snapshot(log).Length == 1);

            var normal = Gated("normal", 1, rest, log);
            var high = Gated("high", 1, rest, log, Priority.High);
            await executor.TryAdd(normal, Timeout);
            await executor.TryAdd(high, Timeout);

            busy.SetResult();
            await Task.WhenAll(normal.WaitAsync(), high.WaitAsync()).WaitAsync(Timeout);

            Assert.Equal(new[] { "start busy", "start high", "start normal" }, UserBasedExecutorBehaviorTests.Snapshot(log));
        }

        [Fact]
        public async Task GlobalTask_WaitingTooLong_StopsNewTasksUntilItRuns()
        {
            var executor = new UserBasedExecutor(new ListLogger(), TimeSpan.FromMilliseconds(200));
            executor.Initialize(new EmptyServiceProvider());
            var log = new List<string>();
            var userGate = new TaskCompletionSource();
            var globalGate = new TaskCompletionSource();
            var otherGate = new TaskCompletionSource();

            await executor.TryAdd(Gated("u1", 1, userGate, log), Timeout);
            await UserBasedExecutorBehaviorTests.WaitUntil(() => UserBasedExecutorBehaviorTests.Snapshot(log).Length == 1);

            var global = Gated("global", 0, globalGate, log);
            await executor.TryAdd(global, Timeout);
            await Task.Delay(300);

            var other = Gated("u2", 2, otherGate, log);
            await executor.TryAdd(other, Timeout);
            await Task.Delay(150);
            Assert.DoesNotContain("start u2", UserBasedExecutorBehaviorTests.Snapshot(log));

            userGate.SetResult();
            await UserBasedExecutorBehaviorTests.WaitUntil(() => UserBasedExecutorBehaviorTests.Snapshot(log).Contains("start global"));
            Assert.DoesNotContain("start u2", UserBasedExecutorBehaviorTests.Snapshot(log));

            globalGate.SetResult();
            otherGate.SetResult();
            await other.WaitAsync().WaitAsync(Timeout);
        }
    }

    public class DereRerollTests
    {
        private static Func<Dere> Sequence(params Dere[] values)
        {
            var queue = new Queue<Dere>(values);
            return () => queue.Dequeue();
        }

        [Fact]
        public void SingleItem_StillRollsOnce()
        {
            var card = new Card { Dere = Dere.Tsundere };

            var used = Waifu.RerollDereUntil(card, Dere.Kuudere, 1, Sequence(Dere.Dandere));

            Assert.Equal(1, used);
            Assert.Equal(Dere.Dandere, card.Dere);
        }

        [Fact]
        public void TargetHit_StopsAndChargesOnlyUsedRolls()
        {
            var card = new Card { Dere = Dere.Tsundere };

            var used = Waifu.RerollDereUntil(card, Dere.Kuudere, 10, Sequence(Dere.Dandere, Dere.Kuudere));

            Assert.Equal(2, used);
            Assert.Equal(Dere.Kuudere, card.Dere);
        }

        [Fact]
        public void TargetMissed_ChargesExactlyOneItemPerRoll()
        {
            var card = new Card { Dere = Dere.Tsundere };
            var rolls = 0;

            var used = Waifu.RerollDereUntil(card, Dere.Kuudere, 3, () => { rolls++; return Dere.Dandere; });

            Assert.Equal(3, used);
            Assert.Equal(3, rolls);
        }
    }

    public class QuizQueryTests
    {
        [Fact]
        public void QuestionWithAnswers_IsValidQuery()
        {
            var config = new FakeConfig();
            config.Model.ConnectionString = "Server=127.0.0.1;Database=test;Uid=test;Pwd=test;";

            using var db = new Sanakan.Database.DatabaseContext(config);
            var sql = db.Questions.Include(x => x.Answers).Where(x => x.Id == 1).ToQueryString();

            Assert.Contains("Answers", sql);
        }

        [Fact]
        public void QuizController_DoesNotIncludeScalarAnswer()
        {
            var source = File.ReadAllText(Path.Combine(RepoPaths.Src, "Api", "Controllers", "QuizController.cs"));
            Assert.DoesNotMatch(new Regex(@"Include\(x => x\.Answer\)"), source);
        }
    }

    public class UpgradeSacrificeTests
    {
        private static Card Target() => new Card { Id = 1, Tags = new List<Tag>() };
        private static Card Sac() => new Card { Id = 2, Tags = new List<Tag>(), Expedition = CardExpedition.None };

        [Fact]
        public void ValidSacrifice_IsAllowed()
        {
            Assert.Null(Sac().GetUpgradeSacrificeBlockReason(Target(), false));
        }

        [Fact]
        public void SameCard_IsBlocked()
        {
            var card = Target();
            Assert.NotNull(card.GetUpgradeSacrificeBlockReason(card, false));
        }

        [Fact]
        public void ProtectedStates_AreBlocked()
        {
            var onExpedition = Sac(); onExpedition.Expedition = CardExpedition.NormalItemWithExp;
            var inCage = Sac(); inCage.InCage = true;
            var active = Sac(); active.Active = true;

            Assert.NotNull(onExpedition.GetUpgradeSacrificeBlockReason(Target(), false));
            Assert.NotNull(inCage.GetUpgradeSacrificeBlockReason(Target(), false));
            Assert.NotNull(active.GetUpgradeSacrificeBlockReason(Target(), false));
            Assert.NotNull(Sac().GetUpgradeSacrificeBlockReason(Target(), true));
        }
    }

    public class NullBoosterPackTests
    {
        private static WaifuController NewController(FakeExecutor executor)
            => ControllerHarness.Attach(new WaifuController(null, null, executor, null, new FakeConfig(), new FixedTime(),
                new MemoryCache(new MemoryCacheOptions()), null, null));

        [Fact]
        public async Task GivePacks_NullBody_Returns500WithoutException()
        {
            var executor = new FakeExecutor();
            var controller = NewController(executor);

            var result = await controller.GiveUserAPacksAsync(5, null);

            Assert.Equal(500, result.StatusOf());
            Assert.Empty(executor.Added);
        }

        [Fact]
        public async Task OpenPacks_NullBody_Returns500WithoutException()
        {
            var controller = NewController(new FakeExecutor());

            var result = await controller.GiveShindenUserAPacksAndOpenAsync(5, null);
            Assert.Null(result.Value);
            Assert.Equal(500, result.StatusOf());
        }
    }

    public class RegisterUserTests
    {
        private static Shinden.Models.IUserSearch Found(ulong id, string name)
        {
            var mock = new Mock<Shinden.Models.IUserSearch>();
            mock.SetupGet(x => x.Id).Returns(id);
            mock.SetupGet(x => x.Name).Returns(name);
            return mock.Object;
        }

        [Fact]
        public void PickUser_PrefersExactName()
        {
            var picked = UserController.PickShindenUser(new[] { Found(1, "Karnaval"), Found(2, "karna") }, "Karna");
            Assert.Equal(2ul, picked.Id);
        }

        [Fact]
        public void PickUser_FallsBackToFirstResult()
        {
            Assert.Equal(1ul, UserController.PickShindenUser(new[] { Found(1, "Karnaval"), Found(2, "Karnus") }, "Karna").Id);
        }

        [Fact]
        public void PickUser_EmptyOrNull_ReturnsNull()
        {
            Assert.Null(UserController.PickShindenUser(new Shinden.Models.IUserSearch[0], "Karna"));
            Assert.Null(UserController.PickShindenUser(null, "Karna"));
        }

        [Fact]
        public async Task Register_NullModel_Returns500()
        {
            var controller = ControllerHarness.Attach(new UserController(null, null, new ListLogger(), new FakeExecutor(),
                new FakeConfig(), new FixedTime(), new MemoryCache(new MemoryCacheOptions())));

            var result = await controller.RegisterUserAsync(null);

            Assert.Equal(500, result.StatusOf());
        }
    }

    public class RichMessageTests
    {
        private static RichMessageController NewController(params RichMessageConfig[] configs)
        {
            var config = new FakeConfig();
            config.Model.RMConfig = configs.ToList();
            return ControllerHarness.Attach(new RichMessageController(new DiscordSocketClient(), config, new ListLogger()));
        }

        [Fact]
        public async Task Delete_UnknownMessage_Returns404()
        {
            var controller = NewController(new RichMessageConfig { GuildId = 1, ChannelId = 2 });

            var result = await controller.DeleteRichMessageAsync(123);

            Assert.Equal(404, result.StatusOf());
        }

        [Fact]
        public async Task Delete_BrokenWebhook_DoesNotStopOtherTargets()
        {
            var controller = NewController(
                new RichMessageConfig { WebHookUrl = "not-a-webhook" },
                new RichMessageConfig { GuildId = 1, ChannelId = 2 });

            var result = await controller.DeleteRichMessageAsync(123);

            Assert.Equal(404, result.StatusOf());
        }

        [Fact]
        public async Task Modify_UnknownMessage_Returns404()
        {
            var controller = NewController(new RichMessageConfig { WebHookUrl = "not-a-webhook" });

            var result = await controller.ModifyeRichMessageAsync(123, new Sanakan.Api.Models.RichMessage().Example());

            Assert.Equal(404, result.StatusOf());
        }

        [Fact]
        public async Task Delete_WithoutConfig_Returns404()
        {
            var controller = NewController();

            var result = await controller.DeleteRichMessageAsync(123);

            Assert.Equal(404, result.StatusOf());
        }
    }

    public class DaemonizerTests
    {
        private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(50);

        private static (Daemonizer daemon, ConcurrentQueue<int> exits) Create(Func<ConnectionState> state, bool enabled = true)
        {
            var config = new FakeConfig();
            config.Model.Demonization = enabled;
            var exits = new ConcurrentQueue<int>();
            return (new Daemonizer(state, new ListLogger(), config, Short, exits.Enqueue), exits);
        }

        [Fact]
        public async Task StillDisconnected_ExitsWithRestartCode()
        {
            var (daemon, exits) = Create(() => ConnectionState.Disconnected);

            daemon.HandleDisconnected();
            await UserBasedExecutorBehaviorTests.WaitUntil(() => exits.Count > 0);

            Assert.Equal(new[] { 1 }, exits);
        }

        [Fact]
        public async Task ReconnectedEvent_CancelsCheck()
        {
            var (daemon, exits) = Create(() => ConnectionState.Disconnected);

            daemon.HandleDisconnected();
            daemon.HandleConnected();
            await Task.Delay(300);

            Assert.Empty(exits);
        }

        [Fact]
        public async Task ConnectedAtCheckTime_DoesNotExit()
        {
            var (daemon, exits) = Create(() => ConnectionState.Connected);

            daemon.HandleDisconnected();
            await Task.Delay(300);

            Assert.Empty(exits);
        }

        [Fact]
        public async Task Disabled_DoesNotExit()
        {
            var (daemon, exits) = Create(() => ConnectionState.Disconnected, enabled: false);

            daemon.HandleDisconnected();
            await Task.Delay(300);

            Assert.Empty(exits);
        }
    }

    public class RemovedCodeTests
    {
        [Fact]
        public void SynchronizedExecutor_IsGone()
        {
            Assert.Null(typeof(UserBasedExecutor).Assembly.GetType("Sanakan.Services.Executor.SynchronizedExecutor"));
        }
    }

    public static class RepoPaths
    {
        public static string Src
        {
            get
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "Sanakan.csproj")))
                    dir = dir.Parent;

                return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("repo root"), "src");
            }
        }
    }
}
