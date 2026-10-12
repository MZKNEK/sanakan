using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Sanakan.Database;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services.Executor;
using Sanakan.Services.PocketWaifu;
using Xunit;
using WaifuModule = Sanakan.Modules.PocketWaifu;

namespace Artifacts
{
    public class DuelConcurrencyTests : IDisposable
    {
        private readonly SqliteConnection _connection = new SqliteConnection("Data Source=:memory:");
        private readonly DiscordSocketClient _client = new DiscordSocketClient();
        private readonly FixedTime _time = new FixedTime();
        // Only a guard against hangs; a busy CI runner can delay queued jobs.
        private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(30);

        private sealed class TestDatabase : DatabaseContext
        {
            private readonly SqliteConnection _connection;
            public TestDatabase(SqliteConnection connection) : base(new FakeConfig()) => _connection = connection;
            protected override void OnConfiguring(DbContextOptionsBuilder builder) => builder.UseSqlite(_connection);
        }

        private sealed class TestModule : WaifuModule
        {
            private readonly Func<DatabaseContext> _factory;
            public int ContextsCreated;
            public Mock<ISocketMessageChannel> ReplyChannel { get; set; }
            public TestModule(IExecutor executor, FixedTime time, Func<DatabaseContext> factory)
                : base((Waifu)FormatterServices.GetUninitializedObject(typeof(Waifu)), null, new ListLogger(), null, null,
                    new FakeConfig(), executor, null, time, null, null, null, null)
            {
                _factory = factory;
                Config = new FakeConfig();
                Logger = new ListLogger();
            }
            protected override DatabaseContext CreateDuelDatabaseContext()
            {
                Interlocked.Increment(ref ContextsCreated);
                return _factory();
            }
        }

        public DuelConcurrencyTests()
        {
            _connection.Open();
            using var db = Open();
            db.Database.EnsureCreated();
            foreach (ulong id in new ulong[] { 61, 62, 63 })
            {
                var user = new User().Default(id, _time.Now());
                var deck = user.GameDeck;
                deck.MatachMakingRatio = deck.GlobalPVPRank = deck.SeasonalPVPRank = 1000;
                deck.CardsInDeck = 1;
                deck.DeckPower = 300;
                deck.Cards.Add(new Card
                {
                    Id = id, Character = id, Name = "Character", Active = true,
                    Health = id == 63 ? 10 : 999, Attack = id == 63 ? 1 : 999, Defence = 99,
                    Rarity = Rarity.E, Dere = Dere.Deredere, Tags = new List<Tag>()
                });
                db.Users.Add(user);
            }
            db.SaveChanges();
        }

        private DatabaseContext Open() => new TestDatabase(_connection);
        public void Dispose() { _client.Dispose(); _connection.Dispose(); }

        private TestModule Module(IExecutor executor, ulong caller)
        {
            var module = new TestModule(executor, _time, Open);
            var channel = new Mock<ISocketMessageChannel>();
            channel.SetReturnsDefault(Task.FromResult(Mock.Of<IUserMessage>()));
            module.ReplyChannel = channel;
            var context = new SocketCommandContext(_client, SocketTestObjects.Message(SocketTestObjects.User(caller), ".duel", channel.Object));
            typeof(ModuleBase<SocketCommandContext>).GetProperty("Context").SetValue(module, context);
            return module;
        }

        private static Task Queue(TestModule module) => (Task)typeof(WaifuModule)
            .GetMethod("QueueDuelAsync", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(module, new object[] { SocketTestObjects.User(63) });

        [Fact]
        public async Task CommonOpponent_IsLockedAndReloadedAfterSelectionWithoutBlockingOtherUsers()
        {
            using var executor = new UserBasedExecutor(new ListLogger());
            executor.Initialize(new EmptyServiceProvider());
            var jobs = new ConcurrentQueue<Executable>();
            var forwarding = new Mock<IExecutor>();
            forwarding.Setup(x => x.TryAdd(It.IsAny<IExecutable>(), It.IsAny<TimeSpan>()))
                .Returns<IExecutable, TimeSpan>((job, timeout) => { jobs.Enqueue((Executable)job); return executor.TryAdd(job, timeout); });
            var first = Module(forwarding.Object, 61);
            var second = Module(forwarding.Object, 62);
            var blockerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseBlocker = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var blocker = new Executable("enemy update", async () =>
            {
                blockerStarted.TrySetResult();
                await releaseBlocker.Task;
                using var db = Open();
                var enemy = await db.GameDecks.SingleAsync(x => x.Id == 63);
                enemy.MatachMakingRatio = enemy.GlobalPVPRank = enemy.SeasonalPVPRank = 1100;
                await db.SaveChangesAsync();
            }, 63);
            Assert.True(await executor.TryAdd(blocker, TimeSpan.FromSeconds(1)));
            await blockerStarted.Task.WaitAsync(MaxWait);
            try
            {
                var selectFirst = new Executable("select first", () => Queue(first), 61);
                var selectSecond = new Executable("select second", () => Queue(second), 62);
                Assert.True(await executor.TryAdd(selectFirst, TimeSpan.FromSeconds(1)));
                Assert.True(await executor.TryAdd(selectSecond, TimeSpan.FromSeconds(1)));
                Assert.True(await selectFirst.WaitAsync(MaxWait));
                Assert.True(await selectSecond.WaitAsync(MaxWait));
                Assert.Equal(2, jobs.Count);
                Assert.Contains(jobs, x => x.GetOwners().SequenceEqual(new ulong[] { 61, 63 }));
                Assert.Contains(jobs, x => x.GetOwners().SequenceEqual(new ulong[] { 62, 63 }));
                var unrelated = new Executable("unrelated", () => Task.CompletedTask, 64);
                Assert.True(await executor.TryAdd(unrelated, TimeSpan.FromSeconds(1)));
                Assert.True(await unrelated.WaitAsync(MaxWait));
                Assert.Equal(0, first.ContextsCreated);
                Assert.Equal(0, second.ContextsCreated);
            }
            finally { releaseBlocker.TrySetResult(); }

            foreach (var job in jobs) Assert.True(await job.WaitAsync(MaxWait));
            var expectedEnemy = new GameDeck { MatachMakingRatio = 1100, GlobalPVPRank = 1100, SeasonalPVPRank = 1100 };
            foreach (var _ in new[] { 1, 2 })
                new GameDeck { MatachMakingRatio = 1000, GlobalPVPRank = 1000, SeasonalPVPRank = 1000 }
                    .CalculatePVPParams(expectedEnemy, FightResult.Win);

            using var read = Open();
            var actualEnemy = await read.GameDecks.SingleAsync(x => x.Id == 63);
            Assert.Equal(expectedEnemy.MatachMakingRatio, actualEnemy.MatachMakingRatio, 10);
            Assert.Equal(expectedEnemy.GlobalPVPRank, actualEnemy.GlobalPVPRank);
            Assert.Equal(expectedEnemy.SeasonalPVPRank, actualEnemy.SeasonalPVPRank);
            foreach (ulong id in new ulong[] { 61, 62 })
            {
                var player = await read.GetUserOrCreateAsync(id);
                Assert.Equal(1UL, player.GameDeck.PVPDailyGamesPlayed);
                Assert.Equal(FightResult.Win, Assert.Single(player.GameDeck.PvPStats).Result);
                Assert.True(player.GameDeck.PVPCoins > 0);
            }
        }

        [Fact]
        public async Task QueuedDuel_RechecksDailyLimitBeforeChargingAnotherGame()
        {
            using (var db = Open())
            {
                var user = await db.GetUserOrCreateAsync(61);
                user.GameDeck.PVPDailyGamesPlayed = 9;
                user.TimeStatuses.Add(new TimeStatus { Type = StatusType.Pvp, EndsAt = _time.Now().AddDays(1) });
                await db.SaveChangesAsync();
            }
            var jobs = new List<IExecutable>();
            var executor = new Mock<IExecutor>();
            executor.Setup(x => x.TryAdd(It.IsAny<IExecutable>(), It.IsAny<TimeSpan>()))
                .Callback<IExecutable, TimeSpan>((job, _) => jobs.Add(job)).ReturnsAsync(true);
            var module = Module(executor.Object, 61);
            await Queue(module);
            await Queue(module);
            foreach (var job in jobs) await job.ExecuteAsync(new EmptyServiceProvider());
            using var read = Open();
            var player = await read.GetUserOrCreateAsync(61);
            Assert.Equal(10UL, player.GameDeck.PVPDailyGamesPlayed);
            Assert.Single(player.GameDeck.PvPStats);
            Assert.Equal(60, player.GameDeck.PVPCoins);
        }

        [Fact]
        public async Task OpponentWithoutActiveCards_DoesNotConsumeDailyGame()
        {
            IExecutable queued = null;
            var executor = new Mock<IExecutor>();
            executor.Setup(x => x.TryAdd(It.IsAny<IExecutable>(), It.IsAny<TimeSpan>()))
                .Callback<IExecutable, TimeSpan>((job, _) => queued = job).ReturnsAsync(true);
            await Queue(Module(executor.Object, 61));
            using (var db = Open())
            {
                (await db.Cards.SingleAsync(x => x.Id == 63)).Active = false;
                await db.SaveChangesAsync();
            }
            await queued.ExecuteAsync(new EmptyServiceProvider());
            using var read = Open();
            var player = await read.GetUserOrCreateAsync(61);
            Assert.Equal(0UL, player.GameDeck.PVPDailyGamesPlayed);
            Assert.Empty(player.GameDeck.PvPStats);
            Assert.Equal(0, player.GameDeck.PVPCoins);
        }

        [Theory]
        [InlineData(0, "dziś już nie")]
        [InlineData(1, "zbyt słabą")]
        [InlineData(2, "na nowo ustawić")]
        public async Task InvalidCaller_IsRejectedBeforeSelectingAnOpponent(int reason, string expectedReply)
        {
            using (var db = Open())
            {
                var user = await db.GetUserOrCreateAsync(61);
                if (reason == 0)
                {
                    user.GameDeck.PVPDailyGamesPlayed = 10;
                    user.TimeStatuses.Add(new TimeStatus { Type = StatusType.Pvp, EndsAt = _time.Now().AddDays(1) });
                }
                else if (reason == 1) user.GameDeck.Cards.Single().Active = false;
                else user.GameDeck.DeckPower = -1;
                await db.SaveChangesAsync();
            }
            var executor = new Mock<IExecutor>(MockBehavior.Strict);
            var module = Module(executor.Object, 61);
            await module.MakeADuelAsync();
            var reply = Assert.Single(module.ReplyChannel.Invocations.Where(x => x.Method.Name == "SendMessageAsync"));
            Assert.Contains(expectedReply, ((Embed)reply.Arguments[2]).Description);
            Assert.Equal(1, module.ContextsCreated);
        }

        [Fact]
        public async Task FullQueue_ReportsRejectionWithoutStartingAFight()
        {
            var executor = new Mock<IExecutor>();
            executor.Setup(x => x.TryAdd(It.IsAny<IExecutable>(), It.IsAny<TimeSpan>())).ReturnsAsync(false);
            var module = Module(executor.Object, 61);
            await Queue(module);
            var reply = Assert.Single(module.ReplyChannel.Invocations.Where(x => x.Method.Name == "SendMessageAsync"));
            Assert.Contains("kolejka pojedynków jest pełna", ((Embed)reply.Arguments[2]).Description);
            Assert.Equal(0, module.ContextsCreated);
        }
    }
}
