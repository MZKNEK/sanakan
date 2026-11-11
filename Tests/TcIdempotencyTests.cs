using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sanakan.Api.Controllers;
using Sanakan.Database;
using Sanakan.Database.Models;
using Sanakan.Database.Models.Analytics;
using Sanakan.Extensions;
using Sanakan.Services;
using Xunit;

namespace Artifacts
{
    public class TcIdempotencyTests : IDisposable
    {
        private const ulong DiscordId = 93001;
        private const ulong ShindenId = 94001;
        private const ulong OtherDiscordId = 93002;
        private const ulong OtherShindenId = 94002;
        private const long InitialTc = 1000;
        private readonly SqliteConnection _connection = new SqliteConnection("Data Source=:memory:");
        private readonly FakeExecutor _executor = new FakeExecutor();
        private readonly FixedTime _time = new FixedTime();

        private sealed class TestDatabase : DatabaseContext
        {
            private readonly SqliteConnection _connection;
            public TestDatabase(SqliteConnection connection) : base(new FakeConfig()) => _connection = connection;
            protected override void OnConfiguring(DbContextOptionsBuilder builder) => builder.UseSqlite(_connection);
        }

        private sealed class TestController : UserController
        {
            private readonly Func<DatabaseContext> _factory;
            public TestController(FakeExecutor executor, FixedTime time, Func<DatabaseContext> factory)
                : base(null, null, new ListLogger(), executor, new FakeConfig(), time, null, null, null)
                => _factory = factory;
            protected override DatabaseContext CreateDatabaseContext() => _factory();
        }

        public TcIdempotencyTests()
        {
            _connection.Open();
            Sanakan.Database.SqliteText.RegisterPow(_connection);
            using var db = Open();
            db.Database.EnsureCreated();
            foreach (var (discord, shinden) in new[] { (DiscordId, ShindenId), (OtherDiscordId, OtherShindenId) })
            {
                var user = new User().Default(discord, _time.Now());
                user.Shinden = shinden;
                user.TcCnt = InitialTc;
                db.Users.Add(user);
            }
            db.SaveChanges();
        }

        public void Dispose() => _connection.Dispose();
        private TestDatabase Open() => new TestDatabase(_connection);
        private TestController Controller() => ControllerHarness.Attach(new TestController(_executor, _time, Open));
        private static string NewKey() => $"tc-{Guid.NewGuid():N}";

        private Task<IActionResult> Send(bool byDiscord, string key, long value = 100, TestController controller = null, ulong? id = null)
        {
            controller ??= Controller();
            return byDiscord
                ? controller.ModifyPointsTCDiscordAsync(id ?? DiscordId, value, key)
                : controller.ModifyPointsTCAsync(id ?? ShindenId, value, key);
        }

        private long Balance(ulong discordId = DiscordId)
        {
            using var db = Open();
            return db.Users.AsQueryable().Single(x => x.Id == discordId).TcCnt;
        }

        private int RecordCount()
        {
            using var db = Open();
            return db.IdempotencyRecords.AsQueryable().Count();
        }

        private int TransferCount()
        {
            using var db = Open();
            return db.TransferData.AsQueryable().Count();
        }

        private async Task RunQueuedAsync()
        {
            Assert.True(_executor.Added.TryDequeue(out var task));
            try { await task.ExecuteAsync(new EmptyServiceProvider()); }
            catch (Exception) { }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Replay_DoesNotChangeTcOrRepeatTransfer(bool byDiscord)
        {
            _executor.RunImmediately = true;
            var key = NewKey();
            var first = Controller();
            Assert.Equal(200, (await Send(byDiscord, key, controller: first)).StatusOf());
            Assert.False(first.Response.Headers.ContainsKey(Idempotency.ReplayedHeader));

            var replay = Controller();
            Assert.Equal(200, (await Send(byDiscord, key, controller: replay)).StatusOf());

            Assert.Equal("true", replay.Response.Headers[Idempotency.ReplayedHeader].ToString());
            Assert.Equal(InitialTc + 100, Balance());
            Assert.Equal(1, RecordCount());
            Assert.Equal(1, TransferCount());
            Assert.Single(_executor.Added);
            using var db = Open();
            var transfer = await db.TransferData.SingleAsync();
            Assert.Equal(100, transfer.Value);
            Assert.Equal(InitialTc, transfer.ValueBefore);
            Assert.Equal(InitialTc + 100, transfer.ExpectedValue);
            Assert.Equal(DiscordId, transfer.DiscordId);
            Assert.Equal(ShindenId, transfer.ShindenId);
            Assert.Equal(byDiscord ? TransferSource.ByDiscordId : TransferSource.ByShindenId, transfer.Source);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task RunningTransaction_Returns409_ThenReplaysSavedSuccess(bool byDiscord)
        {
            var key = NewKey();
            var pending = Send(byDiscord, key);
            Assert.False(pending.IsCompleted);
            Assert.Equal(InitialTc, Balance());
            Assert.Equal(0, RecordCount());
            Assert.Equal(409, (await Send(byDiscord, key)).StatusOf());

            await RunQueuedAsync();
            Assert.Equal(200, (await pending.WaitAsync(TimeSpan.FromSeconds(30))).StatusOf());
            Assert.Equal(200, (await Send(byDiscord, key)).StatusOf());
            Assert.Equal(InitialTc + 100, Balance());
            Assert.Equal(1, TransferCount());
        }

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, false)]
        [InlineData(true, true)]
        [InlineData(false, true)]
        public async Task ReusedKey_WithOtherAmountOrUser_Returns422(bool byDiscord, bool whileRunning)
        {
            _executor.RunImmediately = !whileRunning;
            var key = NewKey();
            var first = Send(byDiscord, key);

            Assert.Equal(422, (await Send(byDiscord, key, value: 101)).StatusOf());
            Assert.Equal(422, (await Send(byDiscord, key, id: byDiscord ? OtherDiscordId : OtherShindenId)).StatusOf());

            if (whileRunning) await RunQueuedAsync();
            Assert.Equal(200, (await first.WaitAsync(TimeSpan.FromSeconds(30))).StatusOf());
            Assert.Equal(InitialTc + 100, Balance());
            Assert.Equal(InitialTc, Balance(OtherDiscordId));
            Assert.Equal(1, TransferCount());
        }

        [Theory]
        [InlineData(true, "")]
        [InlineData(false, "")]
        [InlineData(true, "has space")]
        [InlineData(false, "zażółć")]
        public async Task InvalidKey_Returns400WithoutQueueing(bool byDiscord, string key)
        {
            Assert.Equal(400, (await Send(byDiscord, key)).StatusOf());
            Assert.Equal(400, (await Send(byDiscord, new string('a', 65))).StatusOf());
            Assert.Empty(_executor.Added);
            Assert.Equal(InitialTc, Balance());
            Assert.Equal(0, RecordCount());
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task PresentEmptyHeader_DoesNotFallBackToLegacyMode(bool byDiscord)
        {
            var controller = Controller();
            controller.Request.Headers[Idempotency.KeyHeader] = "";

            Assert.Equal(400, (await Send(byDiscord, null, controller: controller)).StatusOf());
            Assert.Empty(_executor.Added);
            Assert.Equal(InitialTc, Balance());
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task QueueFull_DoesNotRememberKeyAndAllowsRetry(bool byDiscord)
        {
            var key = NewKey();
            _executor.Accept = false;
            Assert.Equal(503, (await Send(byDiscord, key)).StatusOf());
            Assert.Equal(0, RecordCount());
            Assert.Equal(InitialTc, Balance());

            _executor.Accept = true;
            _executor.RunImmediately = true;
            Assert.Equal(200, (await Send(byDiscord, key)).StatusOf());
            Assert.Equal(InitialTc + 100, Balance());
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task UnknownUser_DoesNotRememberKeyAndAllowsRetry(bool byDiscord)
        {
            var key = NewKey();
            Assert.Equal(404, (await Send(byDiscord, key, id: 99999)).StatusOf());
            Assert.Equal(0, RecordCount());

            _executor.RunImmediately = true;
            Assert.Equal(200, (await Send(byDiscord, key)).StatusOf());
            Assert.Equal(InitialTc + 100, Balance());
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task SaveFailure_RollsBackTcTransferAndKey_AndAllowsRetry(bool byDiscord)
        {
            var key = NewKey();
            using (var db = Open())
                await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER RejectTcRecord BEFORE INSERT ON IdempotencyRecords BEGIN SELECT RAISE(ABORT, 'test failure'); END;");
            var pending = Send(byDiscord, key);
            await RunQueuedAsync();

            Assert.Equal(500, (await pending.WaitAsync(TimeSpan.FromSeconds(30))).StatusOf());
            Assert.Equal(InitialTc, Balance());
            Assert.Equal(0, RecordCount());
            Assert.Equal(0, TransferCount());
            using (var db = Open())
                await db.Database.ExecuteSqlRawAsync("DROP TRIGGER RejectTcRecord;");

            _executor.RunImmediately = true;
            Assert.Equal(200, (await Send(byDiscord, key)).StatusOf());
            Assert.Equal(InitialTc + 100, Balance());
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task WithoutHeader_ReturnsBeforeSave_AndEachRequestChangesTc(bool byDiscord)
        {
            Assert.Equal(200, (await Send(byDiscord, null)).StatusOf());
            Assert.Equal(InitialTc, Balance());
            await RunQueuedAsync();
            Assert.Equal(InitialTc + 100, Balance());

            _executor.RunImmediately = true;
            Assert.Equal(200, (await Send(byDiscord, null)).StatusOf());
            Assert.Equal(InitialTc + 200, Balance());
            Assert.Equal(2, TransferCount());
            Assert.Equal(0, RecordCount());
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task ExpiredKey_CanBeUsedAgain(bool byDiscord)
        {
            _executor.RunImmediately = true;
            var key = NewKey();
            Assert.Equal(200, (await Send(byDiscord, key)).StatusOf());
            _time.Value += Idempotency.Retention + TimeSpan.FromMinutes(1);

            Assert.Equal(200, (await Send(byDiscord, key)).StatusOf());
            Assert.Equal(InitialTc + 200, Balance());
            Assert.Equal(1, RecordCount());
            Assert.Equal(2, TransferCount());
        }

        [Theory]
        [InlineData(true, -100L)]
        [InlineData(false, -100L)]
        [InlineData(true, 0L)]
        [InlineData(false, 0L)]
        public async Task ZeroAndNegativeAmounts_KeepExistingSemanticsAndAreNotRepeated(bool byDiscord, long amount)
        {
            _executor.RunImmediately = true;
            var key = NewKey();
            Assert.Equal(200, (await Send(byDiscord, key, amount)).StatusOf());
            Assert.Equal(200, (await Send(byDiscord, key, amount)).StatusOf());
            Assert.Equal(InitialTc + amount, Balance());
            Assert.Equal(1, TransferCount());
        }

        [Fact]
        public async Task SameKey_IsSeparateForDiscordShindenAndPacks()
        {
            _executor.RunImmediately = true;
            var key = NewKey();
            using (var db = Open())
            {
                db.IdempotencyRecords.Add(new IdempotencyRecord
                {
                    Scope = "shinden-boosterpack", Key = key, OwnerId = ShindenId,
                    RequestHash = Idempotency.Hash("packs"), CreatedAt = _time.Now()
                });
                await db.SaveChangesAsync();
            }

            Assert.Equal(200, (await Send(true, key)).StatusOf());
            Assert.Equal(200, (await Send(false, key)).StatusOf());
            Assert.Equal(InitialTc + 200, Balance());
            Assert.Equal(3, RecordCount());
        }

        // the site relies on the key being checked before the account: a saved transfer stays a replay after unlinking
        [Fact]
        public async Task SavedTransfer_IsReplayed_AfterTheAccountWasUnlinked()
        {
            _executor.RunImmediately = true;
            var key = NewKey();
            Assert.Equal(200, (await Send(false, key)).StatusOf());
            using (var db = Open())
            {
                (await db.Users.SingleAsync(x => x.Id == DiscordId)).Shinden = 0;
                await db.SaveChangesAsync();
            }

            var replay = Controller();
            Assert.Equal(200, (await Send(false, key, controller: replay)).StatusOf());
            Assert.Equal("true", replay.Response.Headers[Idempotency.ReplayedHeader].ToString());
            Assert.Equal(InitialTc + 100, Balance());
            Assert.Equal(1, TransferCount());
        }

        [Fact]
        public async Task ShindenRelinkedWhileWaiting_DoesNotCreditAnotherDiscordAccount()
        {
            var key = NewKey();
            var pending = Send(false, key);
            using (var db = Open())
            {
                (await db.Users.SingleAsync(x => x.Id == DiscordId)).Shinden = 0;
                (await db.Users.SingleAsync(x => x.Id == OtherDiscordId)).Shinden = ShindenId;
                await db.SaveChangesAsync();
            }
            await RunQueuedAsync();

            Assert.Equal(404, (await pending.WaitAsync(TimeSpan.FromSeconds(30))).StatusOf());
            Assert.Equal(InitialTc, Balance());
            Assert.Equal(InitialTc, Balance(OtherDiscordId));
            Assert.Equal(0, RecordCount());

            _executor.RunImmediately = true;
            Assert.Equal(200, (await Send(false, key)).StatusOf());
            Assert.Equal(InitialTc + 100, Balance(OtherDiscordId));
        }
    }
}
