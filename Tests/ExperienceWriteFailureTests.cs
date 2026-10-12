using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Discord.WebSocket;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Sanakan.Config.Model;
using Sanakan.Database;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services;
using Sanakan.Services.Executor;
using Xunit;

namespace Artifacts
{
    public class ExperienceWriteFailureTests : IDisposable
    {
        private readonly SqliteConnection _connection = new SqliteConnection("Data Source=:memory:");
        private readonly DiscordSocketClient _client = new DiscordSocketClient();
        private readonly List<IExecutable> _jobs = new List<IExecutable>();
        private readonly FixedTime _time = new FixedTime();
        private readonly SocketGuildUser _user = SocketTestObjects.User(61);
        private readonly ExperienceManager _manager;
        private Func<Task> _beforeSave = () => Task.CompletedTask;

        private sealed class TestDatabase : DatabaseContext
        {
            private readonly SqliteConnection _connection;
            private readonly Func<Task> _beforeSave;
            public TestDatabase(SqliteConnection connection, Func<Task> beforeSave) : base(new FakeConfig())
                => (_connection, _beforeSave) = (connection, beforeSave);
            protected override void OnConfiguring(DbContextOptionsBuilder builder) => builder.UseSqlite(_connection);
            public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken token = default)
            {
                await _beforeSave();
                return await base.SaveChangesAsync(acceptAllChangesOnSuccess, token);
            }
        }

        private sealed class TestManager : ExperienceManager
        {
            private readonly Func<DatabaseContext> _factory;
            public TestManager(DiscordSocketClient client, IExecutor executor, FixedTime time, Func<DatabaseContext> factory)
                : base(client, executor, Config(), null, time, new ListLogger()) => _factory = factory;
            private static FakeConfig Config()
            {
                var config = new FakeConfig();
                config.Model.Prefix = ".";
                config.Model.Exp = new ConfigExp { CharPerPoint = 60, MinPerMessage = 0.00005, MaxPerMessage = 5 };
                return config;
            }
            protected override DatabaseContext CreateDatabaseContext() => _factory();
        }

        public ExperienceWriteFailureTests()
        {
            _connection.Open();
            using var db = Open();
            db.Database.EnsureCreated();
            db.Users.Add(new User().Default(_user.Id, _time.Now()));
            db.SaveChanges();
            var executor = new Mock<IExecutor>();
            executor.Setup(x => x.TryAdd(It.IsAny<IExecutable>(), It.IsAny<TimeSpan>()))
                .Callback<IExecutable, TimeSpan>((job, _) => _jobs.Add(job)).ReturnsAsync(true);
            _manager = new TestManager(_client, executor.Object, _time, Open);
        }

        private DatabaseContext Open() => new TestDatabase(_connection, () => _beforeSave());
        public void Dispose() { _client.Dispose(); _connection.Dispose(); }

        private T Read<T>(string name) => (T)typeof(ExperienceManager)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_manager);
        private object Invoke(string name, params object[] args) => typeof(ExperienceManager)
            .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_manager, args);
        private Task Record(int chars, bool calculateExp = true, bool command = false)
        {
            Invoke("CountMessage", _user.Id, command);
            return (Task)Invoke("CalculateExpAndCreateTaskAsync", _user, SocketTestObjects.Message(_user, new string('a', chars)), calculateExp);
        }
        private Task Execute(int index) => _jobs[index].ExecuteAsync(new EmptyServiceProvider());

        [Fact]
        public async Task FailedWrite_ReturnsProgressAndNextSaveIncludesItOnce()
        {
            await Record(300);
            Assert.Single(_jobs);
            Assert.Equal(0, Read<Dictionary<ulong, double>>("_exp")[_user.Id]);
            await Record(120, command: true);
            _beforeSave = () => throw new InvalidOperationException("Simulated write failure");
            await Assert.ThrowsAsync<Exception>(() => Execute(0));
            using (var db = Open())
            {
                var unchanged = await db.Users.SingleAsync();
                Assert.Equal(10, unchanged.ExpCnt);
                Assert.Equal(0UL, unchanged.MessagesCnt);
            }
            Assert.Equal(7, Read<Dictionary<ulong, double>>("_exp")[_user.Id]);
            Assert.Equal(2UL, Read<Dictionary<ulong, ulong>>("_messages")[_user.Id]);
            Assert.Equal(1UL, Read<Dictionary<ulong, ulong>>("_commands")[_user.Id]);
            Assert.Equal(420UL, Read<Dictionary<ulong, ulong>>("_characters")[_user.Id]);

            _beforeSave = () => Task.CompletedTask;
            await Record(60);
            Assert.Equal(2, _jobs.Count);
            await Execute(1);
            using (var db = Open())
            {
                var saved = await db.GetUserOrCreateSimpleAsync(_user.Id);
                Assert.Equal(18, saved.ExpCnt);
                Assert.Equal(3UL, saved.MessagesCnt);
                Assert.Equal(1UL, saved.CommandsCnt);
                Assert.Equal(480UL, saved.CharacterCntFromDate);
                Assert.Equal(100, Assert.Single(saved.TimeStatuses).IValue);
            }
            Assert.Equal(0, Read<Dictionary<ulong, double>>("_exp")[_user.Id]);
            Assert.Equal(0UL, Read<Dictionary<ulong, ulong>>("_messages")[_user.Id]);
            Assert.Equal(0UL, Read<Dictionary<ulong, ulong>>("_commands")[_user.Id]);
            Assert.Equal(0UL, Read<Dictionary<ulong, ulong>>("_characters")[_user.Id]);
        }

        [Fact]
        public async Task FailedIntervalSave_KeepsCountersForTheNextSave()
        {
            await Record(15, false);
            _time.Value = _time.Value.AddMinutes(31);
            await Record(15, false);
            Assert.Single(_jobs);
            _beforeSave = () => throw new InvalidOperationException("Simulated write failure");
            await Assert.ThrowsAsync<Exception>(() => Execute(0));
            _beforeSave = () => Task.CompletedTask;
            await Record(15, false);
            Assert.Single(_jobs);
            Assert.Equal(3UL, Read<Dictionary<ulong, ulong>>("_messages")[_user.Id]);
            Assert.Equal(45UL, Read<Dictionary<ulong, ulong>>("_characters")[_user.Id]);

            _time.Value = _time.Value.AddMinutes(31);
            await Record(15, false);
            Assert.Equal(2, _jobs.Count);
            await Execute(1);
            using var db = Open();
            var saved = await db.GetUserOrCreateSimpleAsync(_user.Id);
            Assert.Equal(10, saved.ExpCnt);
            Assert.Equal(4UL, saved.MessagesCnt);
            Assert.Equal(60UL, saved.CharacterCntFromDate);
            Assert.Empty(saved.TimeStatuses);
            Assert.Equal(0UL, Read<Dictionary<ulong, ulong>>("_messages")[_user.Id]);
        }

        [Fact]
        public async Task FailedBatch_DoesNotReplayAnotherBatchThatIsAlreadyQueued()
        {
            await Record(300);
            await Record(300);
            _beforeSave = () => throw new InvalidOperationException("Simulated write failure");
            await Assert.ThrowsAsync<Exception>(() => Execute(0));
            _beforeSave = () => Task.CompletedTask;
            await Record(60);
            Assert.Equal(3, _jobs.Count);
            await Execute(1);
            await Execute(2);
            using var db = Open();
            var saved = await db.GetUserOrCreateSimpleAsync(_user.Id);
            Assert.Equal(20, saved.ExpCnt);
            Assert.Equal(3UL, saved.MessagesCnt);
            Assert.Equal(660UL, saved.CharacterCntFromDate);
            Assert.Equal(0, Read<Dictionary<ulong, double>>("_exp")[_user.Id]);
        }
    }
}
