using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Discord.WebSocket;
using Moq;
using Sanakan.Config.Model;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services;
using Sanakan.Services.Executor;
using Xunit;

namespace Artifacts
{
    public class ExperiencePersistenceTests
    {
        private static ExperienceManager Manager(DiscordSocketClient client, IExecutor executor, FixedTime time)
        {
            var config = new FakeConfig();
            config.Model.Prefix = ".";
            config.Model.Exp = new ConfigExp { CharPerPoint = 60, MinPerMessage = 0.00005, MaxPerMessage = 5 };
            return new ExperienceManager(client, executor, config, null, time, new ListLogger());
        }

        private static object Invoke(ExperienceManager manager, string method, params object[] args)
            => typeof(ExperienceManager).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(manager, args);

        private static T Read<T>(ExperienceManager manager, string name)
            => (T)typeof(ExperienceManager).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager);

        private static Task Record(ExperienceManager manager, SocketGuildUser user, int chars, bool calculateExp = true, bool command = false)
        {
            Invoke(manager, "CountMessage", user.Id, command);
            return (Task)Invoke(manager, "CalculateExpAndCreateTaskAsync", user,
                SocketTestObjects.Message(user, new string('a', chars)), calculateExp);
        }

        [Fact]
        public async Task RejectedSave_RetainsProgressAndNextMessageRetriesIt()
        {
            using var client = new DiscordSocketClient();
            var executor = new Mock<IExecutor>();
            executor.SetupSequence(x => x.TryAdd(It.IsAny<IExecutable>(), It.IsAny<TimeSpan>()))
                .ReturnsAsync(false).ReturnsAsync(true);
            var time = new FixedTime();
            var manager = Manager(client, executor.Object, time);
            var user = SocketTestObjects.User(61);

            await Record(manager, user, 60);
            var previousSave = Read<Dictionary<ulong, DateTime>>(manager, "_saved")[user.Id];
            time.Value = time.Value.AddMinutes(1);
            await Record(manager, user, 270, command: true);

            Assert.Equal(5.5, Read<Dictionary<ulong, double>>(manager, "_exp")[user.Id]);
            Assert.Equal(2UL, Read<Dictionary<ulong, ulong>>(manager, "_messages")[user.Id]);
            Assert.Equal(1UL, Read<Dictionary<ulong, ulong>>(manager, "_commands")[user.Id]);
            Assert.Equal(330UL, Read<Dictionary<ulong, ulong>>(manager, "_characters")[user.Id]);
            Assert.Equal(previousSave, Read<Dictionary<ulong, DateTime>>(manager, "_saved")[user.Id]);

            await Record(manager, user, 60);

            executor.Verify(x => x.TryAdd(It.IsAny<IExecutable>(), It.IsAny<TimeSpan>()), Times.Exactly(2));
            Assert.Equal(0.5, Read<Dictionary<ulong, double>>(manager, "_exp")[user.Id]);
            Assert.Equal(0UL, Read<Dictionary<ulong, ulong>>(manager, "_messages")[user.Id]);
            Assert.Equal(0UL, Read<Dictionary<ulong, ulong>>(manager, "_commands")[user.Id]);
            Assert.Equal(0UL, Read<Dictionary<ulong, ulong>>(manager, "_characters")[user.Id]);
            Assert.Equal(time.Value, Read<Dictionary<ulong, DateTime>>(manager, "_saved")[user.Id]);
        }

        [Fact]
        public async Task PendingQueueDecision_DoesNotDiscardCounters()
        {
            using var client = new DiscordSocketClient();
            var accepted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var executor = new Mock<IExecutor>();
            executor.Setup(x => x.TryAdd(It.IsAny<IExecutable>(), It.IsAny<TimeSpan>())).Returns(accepted.Task);
            var manager = Manager(client, executor.Object, new FixedTime());
            var user = SocketTestObjects.User(62);

            var recording = Record(manager, user, 300);
            Assert.False(recording.IsCompleted);
            Assert.Equal(5, Read<Dictionary<ulong, double>>(manager, "_exp")[user.Id]);
            Assert.Equal(1UL, Read<Dictionary<ulong, ulong>>(manager, "_messages")[user.Id]);
            accepted.SetResult(true);
            await recording;
            Assert.Equal(0, Read<Dictionary<ulong, double>>(manager, "_exp")[user.Id]);
            Assert.Equal(0UL, Read<Dictionary<ulong, ulong>>(manager, "_messages")[user.Id]);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task SaveInterval_FlushesCountersEvenWithoutWholeExpPoints(bool calculateExp)
        {
            using var client = new DiscordSocketClient();
            var executor = new Mock<IExecutor>();
            executor.Setup(x => x.TryAdd(It.IsAny<IExecutable>(), It.IsAny<TimeSpan>())).ReturnsAsync(true);
            var time = new FixedTime();
            var manager = Manager(client, executor.Object, time);
            var user = SocketTestObjects.User(63);

            await Record(manager, user, 15, calculateExp);
            time.Value = time.Value.AddMinutes(29);
            await Record(manager, user, 15, calculateExp);
            executor.Verify(x => x.TryAdd(It.IsAny<IExecutable>(), It.IsAny<TimeSpan>()), Times.Never);
            time.Value = time.Value.AddMinutes(2);
            await Record(manager, user, 15, calculateExp);

            executor.Verify(x => x.TryAdd(It.IsAny<IExecutable>(), It.IsAny<TimeSpan>()), Times.Once);
            Assert.Equal(0UL, Read<Dictionary<ulong, ulong>>(manager, "_messages")[user.Id]);
            Assert.Equal(0UL, Read<Dictionary<ulong, ulong>>(manager, "_characters")[user.Id]);
            Assert.Equal(calculateExp ? 0.75 : 0, Read<Dictionary<ulong, double>>(manager, "_exp")[user.Id]);
        }

        [Fact]
        public void CountersOnlySave_DoesNotGrantExpOrChangeFloodPenalty()
        {
            using var client = new DiscordSocketClient();
            var time = new FixedTime();
            var manager = Manager(client, Mock.Of<IExecutor>(), time);
            var user = new User().Default(64, time.Now());
            var flood = StatusType.Flood.NewTimeStatus();
            flood.IValue = 50;
            flood.EndsAt = time.Now().AddMinutes(5);
            user.TimeStatuses.Add(flood);

            Invoke(manager, "ApplyProgress", user, 0L, 3UL, 1UL, 180UL);

            Assert.Equal(10, user.ExpCnt);
            Assert.Equal(3UL, user.MessagesCnt);
            Assert.Equal(1UL, user.CommandsCnt);
            Assert.Equal(180UL, user.CharacterCntFromDate);
            Assert.Equal(50, flood.IValue);
            Assert.Equal(time.Now().AddMinutes(5), flood.EndsAt);
        }

        [Fact]
        public void ExpSave_StillAppliesFloodPenaltyAndAtLeastOneEarnedPoint()
        {
            using var client = new DiscordSocketClient();
            var time = new FixedTime();
            var manager = Manager(client, Mock.Of<IExecutor>(), time);
            var user = new User().Default(65, time.Now());
            user.TimeStatuses.Add(new TimeStatus { Type = StatusType.Flood, IValue = 21, EndsAt = time.Now().AddMinutes(1) });

            Invoke(manager, "ApplyProgress", user, 1L, 2UL, 0UL, 60UL);

            Assert.Equal(11, user.ExpCnt);
            Assert.Equal(2UL, user.MessagesCnt);
            Assert.Equal(20, Assert.Single(user.TimeStatuses).IValue);
        }
    }
}
