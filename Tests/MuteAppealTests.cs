using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Sanakan.Api.Controllers;
using Sanakan.Database;
using Sanakan.Database.Models.Management;
using Sanakan.Services;
using Xunit;

namespace Artifacts
{
    public class MuteAppealTests : IDisposable
    {
        private const ulong Guild = 10;
        private const ulong UserId = 20;
        private static readonly DateTime Now = new DateTime(2026, 11, 14, 20, 0, 0);

        private readonly SqliteConnection _connection = new SqliteConnection("Data Source=:memory:");

        private sealed class TestDatabase : DatabaseContext
        {
            private readonly SqliteConnection _connection;
            public TestDatabase(SqliteConnection connection) : base(new FakeConfig()) => _connection = connection;
            protected override void OnConfiguring(DbContextOptionsBuilder builder) => builder.UseSqlite(_connection);
        }

        public MuteAppealTests()
        {
            _connection.Open();
            using var db = Open();
            db.Database.EnsureCreated();
        }

        public void Dispose() => _connection.Dispose();

        private DatabaseContext Open() => new TestDatabase(_connection);

        private async Task AddMuteAsync(DateTime start, long hours, params ulong[] roles)
        {
            using var db = Open();
            db.Penalties.Add(new PenaltyInfo
            {
                Guild = Guild,
                User = UserId,
                Type = PenaltyType.Mute,
                Reason = "spam",
                StartDate = start,
                DurationInHours = hours,
                Roles = roles.Select(x => new OwnedRole { Role = x }).ToList(),
            });
            await db.SaveChangesAsync();
        }

        private async Task<MuteAppeals.Lift> LiftAsync(string key, DateTime now, ulong user = UserId)
        {
            using var db = Open();
            var lift = await MuteAppeals.PrepareLiftAsync(db, Guild, user, key, now);
            if (lift.Result == MuteAppeals.Result.Ok)
                await MuteAppeals.SaveLiftAsync(db, lift.Penalty, key, now);
            return lift;
        }

        private async Task<MuteAppeals.Restore> RestoreAsync(string key, DateTime now, ulong user = UserId)
        {
            using var db = Open();
            return await MuteAppeals.RestoreAsync(db, Guild, user, key, now);
        }

        private List<PenaltyInfo> Penalties()
        {
            using var db = Open();
            return db.Penalties.Include(x => x.Roles).AsNoTracking().ToList();
        }

        [Fact]
        public async Task Lift_RemovesTheMuteAndKeepsItUnderTheKey()
        {
            await AddMuteAsync(Now.AddHours(-2), 10, 5, 6);

            var lift = await LiftAsync("appeal:1", Now);

            Assert.Equal(MuteAppeals.Result.Ok, lift.Result);
            Assert.Equal(Now.AddHours(8), lift.End);
            Assert.Empty(Penalties());
            using var db = Open();
            Assert.Empty(db.OwnedRoles);
            var kept = db.LiftedPenalties.Single();
            Assert.Equal(("appeal:1", Guild, UserId, "spam", Now.AddHours(-2), 10L, Now), (kept.Key, kept.Guild, kept.User, kept.Reason, kept.StartDate, kept.DurationInHours, kept.LiftedAt));
            Assert.Equal(new ulong[] { 5, 6 }, kept.RoleIds());
            Assert.Null(kept.RestoredAt);
        }

        [Fact]
        public async Task Lift_RepeatedKeyIsAlready_AndAnotherUsersKeyIsRejected()
        {
            await AddMuteAsync(Now.AddHours(-2), 10);
            await LiftAsync("appeal:1", Now);

            Assert.Equal(MuteAppeals.Result.Already, (await LiftAsync("appeal:1", Now.AddMinutes(1))).Result);
            Assert.Equal(MuteAppeals.Result.KeyMismatch, (await LiftAsync("appeal:1", Now, user: 99)).Result);
        }

        [Fact]
        public async Task Lift_WithoutARunningMuteIsNone()
        {
            Assert.Equal(MuteAppeals.Result.None, (await LiftAsync("appeal:1", Now)).Result);

            await AddMuteAsync(Now.AddHours(-11), 10);
            Assert.Equal(MuteAppeals.Result.None, (await LiftAsync("appeal:1", Now)).Result);
            Assert.Single(Penalties());
        }

        [Fact]
        public async Task Restore_PutsBackExactlyTheTimeThatWasLeft()
        {
            await AddMuteAsync(Now.AddHours(-2), 10, 5, 6);
            await LiftAsync("appeal:1", Now.AddMinutes(-30));

            var later = Now.AddDays(3);
            var restore = await RestoreAsync("appeal:1", later);

            Assert.Equal(MuteAppeals.Result.Ok, restore.Result);
            Assert.False(restore.Extended);
            Assert.Equal(TimeSpan.FromMinutes(8 * 60 + 30), restore.Remaining);
            Assert.Equal(later.AddMinutes(8 * 60 + 30), restore.End);

            var mute = Penalties().Single();
            Assert.Equal(("spam", 10L, PenaltyType.Mute), (mute.Reason, mute.DurationInHours, mute.Type));
            Assert.Equal(later.AddMinutes(8 * 60 + 30), PenaltyFormat.End(mute));
            Assert.Equal(new ulong[] { 5, 6 }, mute.Roles.Select(x => x.Role).OrderBy(x => x));
            Assert.True(MuteAppeals.IsActive(mute, later));

            Assert.Equal(MuteAppeals.Result.Already, (await RestoreAsync("appeal:1", later.AddMinutes(1))).Result);
            Assert.Single(Penalties());
        }

        [Fact]
        public async Task Restore_AddsTheTimeLeftToAMuteGivenInTheMeantime()
        {
            await AddMuteAsync(Now.AddHours(-2), 10, 5, 6);
            await LiftAsync("appeal:1", Now);

            var later = Now.AddDays(1);
            await AddMuteAsync(later.AddHours(-1), 3, 5, 7);

            var restore = await RestoreAsync("appeal:1", later);

            Assert.Equal(MuteAppeals.Result.Ok, restore.Result);
            Assert.True(restore.Extended);
            Assert.Equal(later.AddHours(2 + 8), restore.End);

            var mute = Penalties().Single();
            Assert.Equal(later.AddHours(10), PenaltyFormat.End(mute));
            Assert.Equal(3L, mute.DurationInHours);
            Assert.Equal(new ulong[] { 5, 6, 7 }, mute.Roles.Select(x => x.Role).OrderBy(x => x));
        }

        [Fact]
        public async Task ExtendedMute_LiftedAgain_KeepsTheWholeTime()
        {
            await AddMuteAsync(Now.AddHours(-2), 10);
            await LiftAsync("appeal:1", Now);
            await AddMuteAsync(Now.AddHours(1), 3);
            await RestoreAsync("appeal:1", Now.AddHours(1));

            // the merged mute starts in the future, which must not shorten what is left
            var lift = await LiftAsync("appeal:2", Now.AddHours(1));
            Assert.Equal(Now.AddHours(1 + 3 + 8), lift.End);

            var restore = await RestoreAsync("appeal:2", Now.AddDays(2));
            Assert.Equal(TimeSpan.FromHours(11), restore.Remaining);
            Assert.Equal(Now.AddDays(2).AddHours(11), PenaltyFormat.End(Penalties().Single()));
        }

        [Fact]
        public async Task Restore_UnknownOrForeignKeyIsRejected()
        {
            Assert.Equal(MuteAppeals.Result.UnknownKey, (await RestoreAsync("appeal:1", Now)).Result);

            await AddMuteAsync(Now.AddHours(-2), 10);
            await LiftAsync("appeal:1", Now);

            Assert.Equal(MuteAppeals.Result.KeyMismatch, (await RestoreAsync("appeal:1", Now, user: 99)).Result);
            Assert.Empty(Penalties());
        }

        [Fact]
        public async Task PermanentMute_RoundTripsWithoutOverflow()
        {
            await AddMuteAsync(Now.AddHours(-2), long.MaxValue / 2);
            await LiftAsync("appeal:1", Now);

            var restore = await RestoreAsync("appeal:1", Now.AddDays(1));

            Assert.Equal(MuteAppeals.Result.Ok, restore.Result);
            Assert.True(restore.End > Now.AddYears(900));
        }

        [Theory]
        [InlineData(0.5, "1 minuta")]
        [InlineData(30, "30 minut")]
        [InlineData(90, "1 godzina 30 minut")]
        [InlineData(120, "2 godziny")]
        [InlineData(25 * 60 + 2, "1 dzień 1 godzina 2 minuty")]
        public void RemainingTime_IsWrittenInPolish(double minutes, string text)
            => Assert.Equal(text, PenaltyFormat.Duration(TimeSpan.FromMinutes(minutes)));

        [Fact]
        public void Body_AcceptsIdsAsTextOrNumbers()
        {
            var text = JsonConvert.DeserializeObject<ModerationController.MuteAppeal>("{\"guild\":\"428569745579704321\",\"user\":\"123\",\"key\":\"appeal:1\"}");
            var number = JsonConvert.DeserializeObject<ModerationController.MuteAppeal>("{\"guild\":428569745579704321,\"user\":123,\"key\":\"appeal:1\"}");

            Assert.Equal((428569745579704321UL, 123UL), (text.Guild.Value, text.User.Value));
            Assert.Equal((428569745579704321UL, 123UL), (number.Guild.Value, number.User.Value));
        }
    }
}
