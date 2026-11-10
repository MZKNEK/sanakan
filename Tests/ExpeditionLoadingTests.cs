using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Sanakan.Database;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services.PocketWaifu;
using Xunit;

namespace Artifacts
{
    // wyprawa koniec loads only the returning card. Collections that are not loaded stay null, so running every event
    // and many random expedition ends through such a user fails if any branch needs more than was loaded.
    public class ExpeditionLoadingTests : IDisposable
    {
        private const ulong Player = 10, Card = 501;

        private readonly FixedTime _time = new FixedTime();
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"sanakan-expedition-{Guid.NewGuid():N}.db");

        private sealed class TestDatabase : DatabaseContext
        {
            private readonly string _path;
            public TestDatabase(string path) : base(new FakeConfig()) => _path = path;
            protected override void OnConfiguring(DbContextOptionsBuilder builder) => builder.UseSqlite($"Data Source={_path}");
        }

        public void Dispose()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(_path); } catch (IOException) { }
        }

        private async Task<TagHelper> SeedAsync(CardExpedition expedition)
        {
            using var db = new TestDatabase(_path);
            db.Database.EnsureCreated();
            var tags = await TagHelper.CreateAsync(db);

            var user = new User().Default(Player, _time.Now());
            user.GameDeck.Karma = 50;
            user.GameDeck.Cards.Add(new Card { Id = Card, Character = 77, Name = "postać", Rarity = Rarity.A, Affection = 20, Attack = 50, Defence = 20,
                Health = 100, MarketValue = 3, Quality = Quality.Broken, IsTradable = true, Tags = new List<Tag>(), CreationDate = new DateTime(2026, 1, 1),
                Expedition = expedition, ExpeditionDate = _time.Now().AddHours(-30), ExpeditionEndDate = _time.Now().AddHours(-1) });
            user.GameDeck.Cards.Add(new Card { Id = 502, Character = 78, Name = "inna", Rarity = Rarity.B, Tags = new List<Tag>(), CreationDate = new DateTime(2026, 1, 1) });
            user.GameDeck.Items.Add(ItemType.AffectionRecoveryBig.ToItem(2));
            db.Users.Add(user);
            await db.SaveChangesAsync();
            return tags;
        }

        private Waifu NewWaifu(TagHelper tags)
            => new Waifu(null, null, new Events(_time), new ListLogger(), new Expedition(_time), null, null, _time, null, tags, new FakeConfig());

        public static IEnumerable<object[]> Expeditions()
            => Enum.GetValues<CardExpedition>().Where(x => x != CardExpedition.None).Select(x => new object[] { x });

        [Theory]
        [MemberData(nameof(Expeditions))]
        public async Task EveryEvent_WorksOnTheNarrowUser(CardExpedition expedition)
        {
            await SeedAsync(expedition);
            var events = new Events(_time);

            foreach (var e in Enum.GetValues<EventType>())
            {
                using var db = new TestDatabase(_path);
                var user = await db.GetUserOrCreateWithCardsAsync(Player, Card);
                var card = user.GetCard(Card);
                var msg = "";

                events.ExecuteEvent(e, user, card, ref msg);
                await db.SaveChangesAsync();
                // the other card was never loaded and must stay as it was
                Assert.True(db.Cards.Any(x => x.Id == 502));
                await ReseedIfNeededAsync(expedition);
            }
        }

        private async Task ReseedIfNeededAsync(CardExpedition expedition)
        {
            using var db = new TestDatabase(_path);
            if (await db.Cards.AnyAsync(x => x.Id == Card))
                return;
            db.Database.EnsureDeleted();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            await SeedAsync(expedition);
        }

        [Theory]
        [MemberData(nameof(Expeditions))]
        public async Task RandomExpeditionEnds_WorkOnTheNarrowUser(CardExpedition expedition)
        {
            var tags = await SeedAsync(expedition);
            var waifu = NewWaifu(tags);

            for (int i = 0; i < 60; i++)
            {
                using var db = new TestDatabase(_path);
                var user = await db.GetUserOrCreateWithCardsAsync(Player, Card);
                var message = waifu.EndExpedition(user, user.GetCard(Card));
                Assert.False(string.IsNullOrEmpty(message));
                if (i == 59)
                    await db.SaveChangesAsync();
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task DiscardAfterReturn_WorksOnTheNarrowUser(bool release)
        {
            await SeedAsync(CardExpedition.NormalItemWithExp);

            using (var db = new TestDatabase(_path))
            {
                var user = await db.GetUserOrCreateWithCardsAsync(Player, Card);
                var card = user.GetCard(Card);
                card.DestroyOrRelease(user, release, 0.14);
                user.GameDeck.Cards.Remove(card);
                await db.SaveChangesAsync();
            }

            using (var db = new TestDatabase(_path))
            {
                Assert.False(await db.Cards.AnyAsync(x => x.Id == Card));
                Assert.True(await db.Cards.AnyAsync(x => x.Id == 502));
            }
        }
    }
}
