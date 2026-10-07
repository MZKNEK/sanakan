using System.Linq;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Sanakan.Api.Controllers;
using Sanakan.Database;
using Sanakan.Database.Models;
using Sanakan.Database.Models.Analytics;
using Sanakan.Database.Models.Management;
using Xunit;

namespace Artifacts
{
    public class ModelIndexTests
    {
        private class SqliteContext : DatabaseContext
        {
            public SqliteContext() : base(new FakeConfig()) { }

            protected override void OnConfiguring(DbContextOptionsBuilder builder)
                => builder.UseSqlite("Data Source=:memory:");
        }

        private static bool HasSimpleIndex<T>(DatabaseContext ctx, string property)
        {
            var entity = ctx.Model.FindEntityType(typeof(T));
            return entity.GetIndexes().Any(i => i.Properties.Any(p => p.Name == property));
        }

        private static bool HasCompositeIndex<T>(DatabaseContext ctx, params string[] properties)
        {
            var entity = ctx.Model.FindEntityType(typeof(T));
            return entity.GetIndexes().Any(i => properties.All(p => i.Properties.Any(x => x.Name == p)));
        }

        [Fact]
        public void TimeStatus_HasGuildIndex()
            => Assert.True(HasSimpleIndex<TimeStatus>(new SqliteContext(), nameof(TimeStatus.Guild)));

        [Fact]
        public void TimeStatus_HasUserIndex()
            => Assert.True(HasSimpleIndex<TimeStatus>(new SqliteContext(), nameof(TimeStatus.UserId)));

        [Fact]
        public void TagCardRelation_HasCardIdIndex()
            => Assert.True(HasSimpleIndex<TagCardRelation>(new SqliteContext(), nameof(TagCardRelation.CardId)));

        [Fact]
        public void MuteModifier_HasUserGuildIndex()
            => Assert.True(HasCompositeIndex<MuteModifier>(new SqliteContext(), nameof(MuteModifier.User), nameof(MuteModifier.Guild)));

        [Fact]
        public void UserAnalytics_HasUserAndGuildIndexes()
        {
            var ctx = new SqliteContext();
            Assert.True(HasCompositeIndex<UserAnalytics>(ctx, nameof(UserAnalytics.UserId), nameof(UserAnalytics.MeasureDate)));
            Assert.True(HasCompositeIndex<UserAnalytics>(ctx, nameof(UserAnalytics.GuildId), nameof(UserAnalytics.MeasureDate)));
        }

        [Fact]
        public void SystemAnalytics_HasMeasureDateIndex()
            => Assert.True(HasSimpleIndex<SystemAnalytics>(new SqliteContext(), nameof(SystemAnalytics.MeasureDate)));

        [Fact]
        public void TransferAnalytics_HasDiscordIdDateIndex()
            => Assert.True(HasCompositeIndex<TransferAnalytics>(new SqliteContext(), nameof(TransferAnalytics.DiscordId), nameof(TransferAnalytics.Date)));

        [Fact]
        public void CommandsAnalytics_HasUserAndGuildIndexes()
        {
            var ctx = new SqliteContext();
            Assert.True(HasCompositeIndex<CommandsAnalytics>(ctx, nameof(CommandsAnalytics.UserId), nameof(CommandsAnalytics.Date)));
            Assert.True(HasCompositeIndex<CommandsAnalytics>(ctx, nameof(CommandsAnalytics.GuildId), nameof(CommandsAnalytics.Date)));
        }
    }

    public class OffsetClampTests
    {
        private static int Clamp(uint offset)
        {
            var method = typeof(WaifuController).GetMethod("ClampOffset", BindingFlags.NonPublic | BindingFlags.Static);
            return (int)method.Invoke(null, new object[] { offset });
        }

        [Fact]
        public void SmallOffset_IsUnchanged() => Assert.Equal(5, Clamp(5));

        [Fact]
        public void MaxUIntOffset_DoesNotOverflow() => Assert.Equal(int.MaxValue, Clamp(uint.MaxValue));

        [Fact]
        public void OffsetAboveIntMax_IsClamped() => Assert.Equal(int.MaxValue, Clamp((uint)int.MaxValue + 1));
    }
}
