using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord.WebSocket;
using Microsoft.Extensions.Caching.Memory;
using Sanakan.Api.Controllers;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services.Executor;
using Sanakan.Services.PocketWaifu;
using Sanakan.Services.Session;
using Xunit;

namespace Artifacts
{
    // testy zachowania, które działało przed poprawkami i ma działać dalej

    public class ShopBehaviorTests
    {
        private static Waifu NewWaifu() => new Waifu(null, null, null, new ListLogger(), null, null, null, new FixedTime(), null, null, new FakeConfig());

        private static User NewUser(long tc = 0, long ac = 0, long pc = 0) => new User
        {
            Id = 7,
            TcCnt = tc,
            AcCnt = ac,
            Stats = new UserStats(),
            GameDeck = new GameDeck { PVPCoins = pc, Items = new List<Item>(), BoosterPacks = new List<BoosterPack>(), Figures = new List<Figure>() },
        };

        [Theory]
        [InlineData(ShopType.Normal)]
        [InlineData(ShopType.Pvp)]
        [InlineData(ShopType.Activity)]
        public void CheckIfUserCanBuy_UsesShopCurrency(ShopType type)
        {
            var waifu = NewWaifu();
            var user = NewUser(tc: type == ShopType.Normal ? 100 : 0, ac: type == ShopType.Activity ? 100 : 0, pc: type == ShopType.Pvp ? 100 : 0);

            Assert.True(waifu.CheckIfUserCanBuy(type, user, 100));
            Assert.False(waifu.CheckIfUserCanBuy(type, user, 101));
        }

        [Fact]
        public void RemoveMoneyAndStats_ChangeTheRightFields()
        {
            var waifu = NewWaifu();
            var user = NewUser(tc: 100, ac: 100, pc: 100);

            waifu.RemoveMoneyFromUser(ShopType.Normal, user, 10);
            waifu.RemoveMoneyFromUser(ShopType.Activity, user, 20);
            waifu.RemoveMoneyFromUser(ShopType.Pvp, user, 30);
            waifu.IncreaseMoneySpentOnCards(ShopType.Normal, user, 5);
            waifu.IncreaseMoneySpentOnCookies(ShopType.Normal, user, 6);

            Assert.Equal(90, user.TcCnt);
            Assert.Equal(80, user.AcCnt);
            Assert.Equal(70, user.GameDeck.PVPCoins);
            Assert.Equal(5, user.Stats.WastedTcOnCards);
            Assert.Equal(6, user.Stats.WastedTcOnCookies);
        }

        [Fact]
        public void NormalShop_FirstItemIsCheapFood()
        {
            var first = NewWaifu().GetItemsWithCost().First();
            Assert.Equal(3, first.Cost);
            Assert.Equal(ItemType.AffectionRecoverySmall, first.Item.Type);
        }

        [Fact]
        public async Task ExecuteShop_ListInfoAndInvalidInput_DoNotTouchDatabase()
        {
            var waifu = NewWaifu();
            var user = Users.Discord(5);

            var list = await waifu.ExecuteShopAsync(ShopType.Normal, new FakeConfig(), user, 0, "0");
            Assert.Contains("Sklepik", list.Description);

            var tooFar = await waifu.ExecuteShopAsync(ShopType.Normal, new FakeConfig(), user, 999, "1");
            Assert.Contains("nie odnaleziono", tooFar.Description);

            var info = await waifu.ExecuteShopAsync(ShopType.Normal, new FakeConfig(), user, 1, "info");
            Assert.Contains(waifu.GetItemsWithCost().First().Item.Name, info.Description);

            var garbage = await waifu.ExecuteShopAsync(ShopType.Normal, new FakeConfig(), user, 1, "abc");
            Assert.Contains("bohomazy", garbage.Description);
        }
    }

    public class TimeStatusBehaviorTests
    {
        private static readonly DateTime Now = new DateTime(2026, 10, 4, 12, 0, 0);

        [Fact]
        public void NewStatus_IsNotActive()
        {
            var status = StatusType.Globals.NewTimeStatus(5);
            Assert.Equal(DateTime.MinValue, status.EndsAt);
            Assert.Equal(5ul, status.Guild);
            Assert.False(status.IsActive(Now));
        }

        [Fact]
        public void Status_IsActiveUntilEndDate()
        {
            var status = StatusType.Color.NewTimeStatus();
            status.EndsAt = Now.AddMinutes(1);
            Assert.True(status.IsActive(Now));
            Assert.False(status.IsActive(Now.AddMinutes(2)));
        }
    }

    public class SessionManagerBehaviorTests
    {
        private static SessionManager NewManager()
        {
            var manager = new SessionManager(new DiscordSocketClient(), new FakeExecutor(), new ListLogger());
            manager.Initialize(new EmptyServiceProvider());
            return manager;
        }

        [Fact]
        public async Task AddedSession_ExistsAndBlocksSecondOfSameType()
        {
            var manager = NewManager();
            var user = Users.Discord(1);

            Assert.True(await manager.TryAddSession(new Session(user)));
            Assert.True(manager.SessionExist(user, typeof(Session)));
            Assert.False(await manager.TryAddSession(new Session(user)));
        }

        [Fact]
        public async Task DifferentOwners_CanHaveSessionsAtTheSameTime()
        {
            var manager = NewManager();

            Assert.True(await manager.TryAddSession(new Session(Users.Discord(1))));
            Assert.True(await manager.TryAddSession(new Session(Users.Discord(2))));
        }

        [Fact]
        public async Task Participant_IsTreatedAsOwner()
        {
            var manager = NewManager();
            var session = new Session(Users.Discord(1));
            session.AddParticipant(Users.Discord(2));

            Assert.True(await manager.TryAddSession(session));
            Assert.False(await manager.TryAddSession(new Session(Users.Discord(2))));
        }

        [Fact]
        public async Task Kill_DisposesAndRemovesSession()
        {
            var manager = NewManager();
            var user = Users.Discord(1);
            var disposed = 0;
            var session = new Session(user) { OnDispose = () => { disposed++; return Task.CompletedTask; } };

            await manager.TryAddSession(session);
            await manager.KillSessionIfExistAsync(new Session(user));

            Assert.Equal(1, disposed);
            Assert.False(manager.SessionExist(user, typeof(Session)));
            Assert.True(await manager.TryAddSession(new Session(user)));
        }

        [Fact]
        public async Task SessionExecutable_RunsOnExecuteWithAllOwners()
        {
            var session = new Session(Users.Discord(1)) { OnExecute = (_, _) => Task.FromResult(false) };
            session.AddParticipant(Users.Discord(2));

            var exe = session.GetExecutable(null);

            Assert.Equal(new ulong[] { 1, 1, 2 }, exe.GetOwners());
            Assert.False(await exe.ExecuteAsync(new EmptyServiceProvider()));
        }
    }

    public class UserBasedExecutorBehaviorTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

        private static UserBasedExecutor NewExecutor()
        {
            var executor = new UserBasedExecutor(new ListLogger());
            executor.Initialize(new EmptyServiceProvider());
            return executor;
        }

        private static Executable Gated(string name, ulong owner, TaskCompletionSource gate, List<string> log, Priority priority = Priority.Normal)
            => new Executable(name, new Func<Task>(async () =>
            {
                lock (log) log.Add($"start {name}");
                await gate.Task;
                lock (log) log.Add($"end {name}");
            }), owner, priority);

        [Fact]
        public async Task DifferentOwners_RunInParallel()
        {
            var executor = NewExecutor();
            var gate = new TaskCompletionSource();
            var log = new List<string>();

            var a = Gated("a", 1, gate, log);
            var b = Gated("b", 2, gate, log);
            Assert.True(await executor.TryAdd(a, Timeout));
            Assert.True(await executor.TryAdd(b, Timeout));

            await WaitUntil(() => log.Count == 2);
            gate.SetResult();
            await Task.WhenAll(a.WaitAsync(), b.WaitAsync()).WaitAsync(Timeout);
        }

        [Fact]
        public async Task SameOwner_RunsInOrderOneAtATime()
        {
            var executor = NewExecutor();
            var gate = new TaskCompletionSource();
            var log = new List<string>();

            var bGate = new TaskCompletionSource();
            var a = Gated("a", 1, gate, log);
            var b = Gated("b", 1, bGate, log);

            await executor.TryAdd(a, Timeout);
            await executor.TryAdd(b, Timeout);
            await WaitUntil(() => log.Count == 1);
            await Task.Delay(100);
            Assert.Equal(new[] { "start a" }, Snapshot(log));

            gate.SetResult();
            bGate.SetResult();
            await b.WaitAsync().WaitAsync(Timeout);
            Assert.Equal(new[] { "start a", "end a", "start b", "end b" }, Snapshot(log));
        }

        [Fact]
        public async Task GlobalTask_RunsAlone()
        {
            var executor = NewExecutor();
            var userGate = new TaskCompletionSource();
            var globalGate = new TaskCompletionSource();
            var log = new List<string>();

            var user = Gated("u", 1, userGate, log);
            var global = Gated("g", 0, globalGate, log);

            await executor.TryAdd(user, Timeout);
            await WaitUntil(() => log.Count == 1);
            await executor.TryAdd(global, Timeout);
            await Task.Delay(100);
            Assert.DoesNotContain("start g", Snapshot(log));

            userGate.SetResult();
            await WaitUntil(() => Snapshot(log).Contains("start g"));

            var other = Gated("o", 2, new TaskCompletionSource(), log);
            await executor.TryAdd(other, Timeout);
            await Task.Delay(100);
            Assert.DoesNotContain("start o", Snapshot(log));

            globalGate.SetResult();
            await WaitUntil(() => Snapshot(log).Contains("start o"));
        }

        [Fact]
        public async Task FullQueue_RejectsNewTask()
        {
            var executor = NewExecutor();
            var gate = new TaskCompletionSource();
            var log = new List<string>();

            var global = Gated("g", 0, gate, log);
            await executor.TryAdd(global, Timeout);
            await WaitUntil(() => log.Count == 1);

            for (ulong i = 1; i <= 100; i++)
                Assert.True(await executor.TryAdd(Gated($"t{i}", i, gate, log), TimeSpan.FromMilliseconds(100)));

            Assert.False(await executor.TryAdd(Gated("over", 999, gate, log), TimeSpan.FromMilliseconds(100)));
            gate.SetResult();
        }

        [Fact]
        public async Task FailingTask_DoesNotBlockOwner()
        {
            var executor = NewExecutor();
            var failing = new Executable("f", new Func<Task>(() => throw new InvalidOperationException()), 1);
            var next = new Executable("n", new Func<Task>(() => Task.CompletedTask), 1);

            await executor.TryAdd(failing, Timeout);
            await executor.TryAdd(next, Timeout);

            await next.WaitAsync().WaitAsync(Timeout);
        }

        internal static string[] Snapshot(List<string> log)
        {
            lock (log) return log.ToArray();
        }

        internal static async Task WaitUntil(Func<bool> condition)
        {
            var end = DateTime.UtcNow + Timeout;
            while (!condition())
            {
                if (DateTime.UtcNow > end)
                    throw new TimeoutException();
                await Task.Delay(10);
            }
        }
    }

    public class ControllerBehaviorTests
    {
        private static WaifuController NewWaifuController(FakeExecutor executor)
            => ControllerHarness.Attach(new WaifuController(null, null, executor, null, new FakeConfig(), new FixedTime(),
                new MemoryCache(new MemoryCacheOptions()), null, null));

        [Fact]
        public async Task UpdateCardInfo_EnqueuesTaskAndReturnsOk()
        {
            var executor = new FakeExecutor();
            var controller = NewWaifuController(executor);

            var result = await controller.UpdateCardInfoAsync(5, new Sanakan.Api.Models.CharacterCardInfoUpdate());

            Assert.Equal(200, result.StatusOf());
            Assert.Single(executor.Added);
        }

        [Fact]
        public async Task BoosterPacks_EmptyList_IsRejected()
        {
            var executor = new FakeExecutor();
            var controller = NewWaifuController(executor);

            var result = await controller.GiveUserAPacksAsync(5, new List<Sanakan.Api.Models.CardBoosterPack>());

            Assert.Equal(500, result.StatusOf());
            Assert.Empty(executor.Added);
        }

        [Fact]
        public async Task BoosterPacksOpen_EmptyList_IsRejected()
        {
            var controller = NewWaifuController(new FakeExecutor());

            var result = await controller.GiveShindenUserAPacksAndOpenAsync(5, new List<Sanakan.Api.Models.CardBoosterPack>());
            Assert.Null(result.Value);
            Assert.Equal(500, result.StatusOf());
        }

        [Fact]
        public void RichMessage_ExampleIsAvailable()
        {
            var controller = ControllerHarness.Attach(new RichMessageController(null, new FakeConfig(), new ListLogger()));
            Assert.NotNull(controller.GetExampleMsg().Value);
        }
    }
}
