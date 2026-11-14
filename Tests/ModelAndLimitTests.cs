using System;
using System.Linq;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
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

        // builds SQL only, never connects
        internal class MySqlContext : DatabaseContext
        {
            public MySqlContext() : base(new FakeConfig()) { }

            protected override void OnConfiguring(DbContextOptionsBuilder builder)
                => builder.UseMySql("Server=localhost;Database=sanakan", new MySqlServerVersion(new Version(8, 4, 0)));
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
        public void SystemAnalytics_HasMeasureDateIndex()
            => Assert.True(HasSimpleIndex<SystemAnalytics>(new SqliteContext(), nameof(SystemAnalytics.MeasureDate)));

        [Fact]
        public void TransferAnalytics_HasDiscordIdDateIndex()
            => Assert.True(HasCompositeIndex<TransferAnalytics>(new SqliteContext(), nameof(TransferAnalytics.DiscordId), nameof(TransferAnalytics.Date)));

        [Fact]
        public void CardPvPStats_HasCoveringIndexForTheProfileCounts()
        {
            var entity = new SqliteContext().Model.FindEntityType(typeof(CardPvPStats));
            Assert.Contains(entity.GetIndexes(), i => i.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(CardPvPStats.GameDeckId), nameof(CardPvPStats.Type), nameof(CardPvPStats.Result) }));
        }

        // varchar keeps the text in the row; Name and Title together must fit the 3072 byte index key in utf8mb4
        [Fact]
        public void Cards_TextColumnsHaveMaxLengths()
        {
            var entity = new SqliteContext().Model.FindEntityType(typeof(Card));
            int? Max(string name) => entity.FindProperty(name).GetMaxLength();

            Assert.Equal(255, Max(nameof(Card.Name)));
            Assert.Equal(500, Max(nameof(Card.Title)));
            Assert.Equal(500, Max(nameof(Card.Image)));
            Assert.Equal(500, Max(nameof(Card.CustomImage)));
            Assert.Equal(500, Max(nameof(Card.CustomBorder)));
            Assert.True((Max(nameof(Card.Name)) + Max(nameof(Card.Title))) * 4 <= 3072);
        }

        [Fact]
        public void Cards_CutTooLongNamesAndTitles_WithoutSplittingACharacter()
        {
            var card = new Card { Name = new string('n', 300), Title = new string('t', 254) + "😀" + new string('t', 300) };
            var emoji = new Card { Title = new string('t', 499) + "😀" };

            Assert.Equal(255, card.Name.Length);
            Assert.Equal(500, card.Title.Length);
            Assert.EndsWith("😀", card.Title.Substring(0, 256));
            Assert.Equal(new string('t', 499), emoji.Title);
            Assert.Null(new Card { Name = null }.Name);
            Assert.Equal("Rem", new Card { Name = "Rem" }.Name);
        }

        [Fact]
        public void Cards_HaveTheIndexesTheQueriesUse()
        {
            var indexes = new SqliteContext().Model.FindEntityType(typeof(Card)).GetIndexes().Select(i => string.Join(",", i.Properties.Select(p => p.Name))).ToList();
            Assert.Contains("GameDeckId,Active,CardPower", indexes);
            Assert.DoesNotContain("GameDeckId,Active", indexes);
            Assert.Contains("GameDeckId,Rarity,FromFigure,Quality,RestartCnt", indexes);
            Assert.Contains("GameDeckId", indexes);
            Assert.Contains("Name,Title", indexes);
            Assert.Contains("PictureKind,CustomImageDate,Id", indexes);
            Assert.Contains("GameDeckId,PictureKind,CustomImageDate,Id", indexes);
            Assert.Contains("GameDeckId,Character", indexes);
            Assert.Contains("GameDeckId,Fatigue", indexes);
            Assert.Contains("GameDeckId,Cursed", indexes);
            Assert.Contains("WhoWantsCount,Character", indexes);
            Assert.Contains("AWhoWantsCount,Character", indexes);
            Assert.Contains("Rarity,Quality,BorderOverflow", indexes);
        }

        [Fact]
        public void Cards_RarityIndex_FollowsTheSortDirections()
        {
            // index directions live only in the design-time model
            var index = new SqliteContext().GetService<IDesignTimeModel>().Model.FindEntityType(typeof(Card)).GetIndexes()
                .Single(i => i.GetDatabaseName() == DatabaseContext.CardsRarityIndex);

            Assert.Equal(new[] { nameof(Card.Rarity), nameof(Card.Quality), nameof(Card.BorderOverflow) }, index.Properties.Select(p => p.Name));
            Assert.Equal(new[] { true, false, false }, index.IsDescending);

            using var mySql = new MySqlContext();
            Assert.Contains("CREATE INDEX `IX_Cards_Rarity_Quality_BorderOverflow` ON `Cards` (`Rarity` DESC, `Quality`, `BorderOverflow`);",
                mySql.Database.GenerateCreateScript());
        }

        [Fact]
        public void TelemetryTables_HaveKeysForTheirQueries()
        {
            var ctx = new SqliteContext();
            Assert.Equal(new[] { nameof(GameStat.Date), nameof(GameStat.Metric), nameof(GameStat.Label) },
                ctx.Model.FindEntityType(typeof(GameStat)).FindPrimaryKey().Properties.Select(p => p.Name));
            Assert.Equal(new[] { nameof(PlayerDay.Date), nameof(PlayerDay.UserId) },
                ctx.Model.FindEntityType(typeof(PlayerDay)).FindPrimaryKey().Properties.Select(p => p.Name));
            Assert.True(HasSimpleIndex<DuelResult>(ctx, nameof(DuelResult.Date)));
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
