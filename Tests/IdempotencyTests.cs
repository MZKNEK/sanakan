using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sanakan.Api.Controllers;
using Sanakan.Api.Models;
using Sanakan.Database;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services;
using Sanakan.Services.Executor;
using Sanakan.Services.Time;
using Xunit;

namespace Artifacts
{
    public class IdempotencyTests : IDisposable
    {
        private const ulong DiscordId = 91001;
        private const ulong ShindenId = 92001;
        private const ulong OtherDiscordId = 91002;
        private const ulong OtherShindenId = 92002;
        // new users start with their own pack
        private const string RewardName = "Nagroda";

        private readonly SqliteConnection _connection = new SqliteConnection("Data Source=:memory:");
        private readonly FakeExecutor _executor = new FakeExecutor();
        private readonly FixedTime _time = new FixedTime();
        private bool _failSave;

        private sealed class TestDatabase : DatabaseContext
        {
            private readonly SqliteConnection _connection;
            private readonly IdempotencyTests _owner;
            public TestDatabase(SqliteConnection connection, IdempotencyTests owner) : base(new FakeConfig())
            {
                _connection = connection;
                _owner = owner;
            }
            protected override void OnConfiguring(DbContextOptionsBuilder builder) => builder.UseSqlite(_connection);

            public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
            {
                if (_owner._failSave)
                    throw new DbUpdateException("save failed");
                return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            }
        }

        private sealed class TestController : WaifuController
        {
            private readonly Func<DatabaseContext> _factory;
            public TestController(IExecutor executor, ISystemTime time, Func<DatabaseContext> factory)
                : base(null, null, executor, null, new FakeConfig(), time, null, null, null) => _factory = factory;
            protected override DatabaseContext CreateDatabaseContext() => _factory();
        }

        public IdempotencyTests()
        {
            _connection.Open();
            using var db = Open();
            db.Database.EnsureCreated();
            foreach (var (discord, shinden) in new[] { (DiscordId, ShindenId), (OtherDiscordId, OtherShindenId) })
            {
                var user = new User().Default(discord, _time.Now());
                user.Shinden = shinden;
                db.Users.Add(user);
            }
            db.SaveChanges();
        }

        public void Dispose() => _connection.Dispose();

        private TestDatabase Open() => new TestDatabase(_connection, this);

        private TestController Controller() => ControllerHarness.Attach(new TestController(_executor, _time, Open));

        private static string NewKey() => $"shinden-waifu-{Guid.NewGuid():N}";

        private static List<CardBoosterPack> Packs(int count = 2) => new List<CardBoosterPack>
        {
            new CardBoosterPack { Name = RewardName, Count = count },
        };

        private int PackCount(ulong discordId)
        {
            using var db = Open();
            return db.BoosterPacks.AsQueryable().Count(x => x.GameDeckId == discordId && x.Name == RewardName);
        }

        private int RecordCount()
        {
            using var db = Open();
            return db.IdempotencyRecords.AsQueryable().Count();
        }

        private async Task<ActionResult<UserWithToken>> Send(string key, ulong shindenId = ShindenId, int count = 2, TestController controller = null)
            => await (controller ?? Controller()).GiveShindenUserAPacksAsync(shindenId, Packs(count), key);

        private async Task RunQueuedAsync()
        {
            IExecutable task;
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (!_executor.Added.TryDequeue(out task))
            {
                Assert.True(DateTime.UtcNow < deadline, "nothing was queued");
                await Task.Delay(10);
            }

            try { await task.ExecuteAsync(new EmptyServiceProvider()); }
            catch (Exception) { }
        }

        private static bool Replayed(TestController controller)
            => controller.Response.Headers.TryGetValue(Idempotency.ReplayedHeader, out var value) && value == "true";

        [Fact]
        public async Task FirstRequest_AddsPacksAfterSave()
        {
            _executor.RunImmediately = true;
            var controller = Controller();

            var res = await Send(NewKey(), controller: controller);

            Assert.IsType<UserWithToken>(res.Value);
            Assert.False(Replayed(controller));
            Assert.Equal(1, PackCount(DiscordId));
            Assert.Equal(1, RecordCount());
        }

        [Fact]
        public async Task Replay_DoesNotAddPacksAgain()
        {
            _executor.RunImmediately = true;
            var key = NewKey();
            await Send(key);

            var controller = Controller();
            var res = await Send(key, controller: controller);

            Assert.IsType<UserWithToken>(res.Value);
            Assert.Equal(DiscordId, res.Value.User.Id);
            Assert.True(Replayed(controller));
            Assert.Equal(1, PackCount(DiscordId));
            Assert.Single(_executor.Added);
        }

        [Fact]
        public async Task RetryWhileRunning_Returns409_ThenReplay()
        {
            var key = NewKey();
            var first = Send(key);

            var running = await Send(key);
            Assert.Equal(409, running.StatusOf());

            await RunQueuedAsync();
            Assert.IsType<UserWithToken>((await first).Value);

            var controller = Controller();
            var replay = await Send(key, controller: controller);
            Assert.IsType<UserWithToken>(replay.Value);
            Assert.True(Replayed(controller));
            Assert.Equal(1, PackCount(DiscordId));
        }

        [Fact]
        public async Task SameKey_OtherShindenUser_Returns422()
        {
            _executor.RunImmediately = true;
            var key = NewKey();
            await Send(key);

            var res = await Send(key, OtherShindenId);

            Assert.Equal(422, res.StatusOf());
            Assert.Equal(0, PackCount(OtherDiscordId));
        }

        [Fact]
        public async Task SameKey_OtherShindenUserWhileRunning_Returns422()
        {
            var key = NewKey();
            var first = Send(key);

            Assert.Equal(422, (await Send(key, OtherShindenId)).StatusOf());

            await RunQueuedAsync();
            await first;
        }

        [Fact]
        public async Task SameKey_DifferentBody_Returns422()
        {
            _executor.RunImmediately = true;
            var key = NewKey();
            await Send(key, count: 2);

            Assert.Equal(422, (await Send(key, count: 3)).StatusOf());
            Assert.Equal(1, PackCount(DiscordId));
        }

        [Theory]
        [InlineData("")]
        [InlineData("klucz z spacją")]
        [InlineData("zażółć")]
        public async Task InvalidKey_Returns400(string key)
        {
            var res = await Send(key);

            Assert.Equal(400, res.StatusOf());
            Assert.Empty(_executor.Added);
        }

        [Fact]
        public async Task TooLongKey_Returns400()
        {
            Assert.Equal(400, (await Send(new string('a', Idempotency.MaxKeyLength + 1))).StatusOf());
            Assert.True(Idempotency.IsValidKey(new string('a', Idempotency.MaxKeyLength)));
        }

        [Fact]
        public async Task UnknownUser_Returns404_AndKeyStaysFree()
        {
            var key = NewKey();
            Assert.Equal(404, (await Send(key, 99999)).StatusOf());

            _executor.RunImmediately = true;
            Assert.IsType<UserWithToken>((await Send(key)).Value);
            Assert.Equal(1, PackCount(DiscordId));
        }

        [Fact]
        public async Task QueueFull_Returns503_AndKeyStaysFree()
        {
            var key = NewKey();
            _executor.Accept = false;
            Assert.Equal(503, (await Send(key)).StatusOf());

            _executor.Accept = true;
            _executor.RunImmediately = true;
            Assert.IsType<UserWithToken>((await Send(key)).Value);
            Assert.Equal(1, PackCount(DiscordId));
        }

        [Fact]
        public async Task FailedSave_Returns500_NothingSaved_AndKeyStaysFree()
        {
            var key = NewKey();
            var first = Send(key);
            _failSave = true;
            await RunQueuedAsync();
            _failSave = false;

            Assert.Equal(500, (await first).StatusOf());
            Assert.Equal(0, PackCount(DiscordId));
            Assert.Equal(0, RecordCount());

            _executor.RunImmediately = true;
            Assert.IsType<UserWithToken>((await Send(key)).Value);
            Assert.Equal(1, PackCount(DiscordId));
        }

        [Fact]
        public async Task WithoutKey_BehavesAsBefore()
        {
            var res = await Send(null);

            Assert.IsType<UserWithToken>(res.Value);
            Assert.Equal(0, PackCount(DiscordId));

            await RunQueuedAsync();
            Assert.Equal(1, PackCount(DiscordId));
            Assert.Equal(0, RecordCount());

            _executor.RunImmediately = true;
            await Send(null);
            Assert.Equal(2, PackCount(DiscordId));
        }

        [Fact]
        public async Task ExpiredKey_AddsPacksAgain()
        {
            _executor.RunImmediately = true;
            var key = NewKey();
            await Send(key);

            _time.Value += Idempotency.Retention + TimeSpan.FromMinutes(1);
            var controller = Controller();
            Assert.IsType<UserWithToken>((await Send(key, controller: controller)).Value);

            Assert.False(Replayed(controller));
            Assert.Equal(2, PackCount(DiscordId));
            Assert.Equal(1, RecordCount());
        }

        [Fact]
        public async Task DeleteExpired_RemovesOnlyOldRecords()
        {
            var now = _time.Now();
            using (var db = Open())
            {
                db.IdempotencyRecords.Add(new IdempotencyRecord { Scope = "test", Key = "old", RequestHash = "h", CreatedAt = now - Idempotency.Retention - TimeSpan.FromMinutes(1) });
                db.IdempotencyRecords.Add(new IdempotencyRecord { Scope = "test", Key = "fresh", RequestHash = "h", CreatedAt = now - TimeSpan.FromHours(47) });
                await db.SaveChangesAsync();
            }

            using (var db = Open())
                Assert.Equal(1, await Idempotency.DeleteExpiredAsync(db, now));

            using var read = Open();
            Assert.Equal("fresh", (await read.IdempotencyRecords.AsQueryable().SingleAsync()).Key);
        }
    }
}
