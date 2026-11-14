using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Sanakan.Api.Models;
using Sanakan.Database;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Xunit;

namespace Artifacts
{
    // picture and curse sorts read columns the database computes, so they order the cards like the old expressions did
    public class CardSortColumnsTests : IDisposable
    {
        private const ulong Player = 10;

        private readonly WaifuCommandHarness _h = new WaifuCommandHarness();

        public void Dispose() => _h.Dispose();

        private static int OldPicture(Card x) => x.CustomImage == null ? (x.Image == null ? 0 : 1) : (x.IsAnimatedImage ? 3 : 2);

        private async Task SeedAsync()
        {
            using var db = await _h.CreateAsync();
            var user = new User().Default(Player, _h.Time.Now());
            ulong id = 100;
            foreach (var custom in new[] { null, "", "Saved/CI.webp" })
            foreach (var image in new[] { null, "https://cdn.shinden.eu/x.jpg" })
            foreach (var animated in new[] { false, true })
            foreach (var curse in new[] { CardCurse.None, CardCurse.LoweredStats, CardCurse.FoodBlockade })
            {
                id++;
                user.GameDeck.Cards.Add(new Card
                {
                    Id = id, Character = id, Name = $"c{id}", Rarity = Rarity.E, CustomImage = custom, Image = image, IsAnimatedImage = animated,
                    Curse = curse, CustomImageDate = new DateTime(2026, 1, 1).AddDays(id % 7), Tags = new List<Tag>(), CreationDate = _h.Time.Now()
                });
            }
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        [Theory]
        [InlineData(OrderType.Picture)]
        [InlineData(OrderType.PictureDes)]
        [InlineData(OrderType.Curse)]
        [InlineData(OrderType.CurseDes)]
        public async Task Sort_OrdersLikeTheOldExpression(OrderType type)
        {
            await SeedAsync();
            using var db = _h.Open();
            var all = await db.Cards.AsNoTracking().ToListAsync();

            var expected = (type switch
            {
                OrderType.Picture => all.OrderBy(OldPicture).ThenBy(x => x.CustomImageDate).ThenBy(x => x.Id),
                OrderType.PictureDes => all.OrderByDescending(OldPicture).ThenByDescending(x => x.CustomImageDate).ThenByDescending(x => x.Id),
                OrderType.Curse => all.OrderBy(x => x.Curse != CardCurse.None ? 1 : 0).ThenBy(x => x.Id),
                _ => all.OrderByDescending(x => x.Curse != CardCurse.None ? 1 : 0).ThenByDescending(x => x.Id),
            }).Select(x => x.Id).ToList();

            // SQLite cannot order by the ulong id tie-breaker, so the database is checked through the values it computed
            Assert.All(all, x => Assert.Equal(OldPicture(x), x.PictureKind));
            Assert.All(all, x => Assert.Equal(x.Curse != CardCurse.None, x.Cursed));
            Assert.Equal(expected, CardsQueryFilter.Use(type, all.AsQueryable()).Select(x => x.Id));
        }

        [Fact]
        public async Task Columns_FollowTheCardChanges()
        {
            await SeedAsync();
            using (var db = _h.Open())
            {
                var card = await db.Cards.SingleAsync(x => x.Id == 101);
                Assert.Equal(0, card.PictureKind);
                Assert.False(card.Cursed);

                card.CustomImage = "Saved/CI101.webp";
                card.IsAnimatedImage = true;
                card.Curse = CardCurse.BloodBlockade;
                await db.SaveChangesAsync();
                Assert.Equal(3, card.PictureKind);
                Assert.True(card.Cursed);
            }

            using var read = _h.Open();
            var saved = await read.Cards.AsNoTracking().SingleAsync(x => x.Id == 101);
            Assert.Equal(3, saved.PictureKind);
            Assert.True(saved.Cursed);
        }

        [Fact]
        public void Hint_GoesOnlyIntoTaggedQueries()
        {
            var tagged = "-- card-list\r\n\r\nSELECT `c`.`Id` FROM `Cards` AS `c`";

            Assert.Equal("-- card-list\r\n\r\nSELECT " + CardListHint.Hint + " `c`.`Id` FROM `Cards` AS `c`", CardListHint.Apply(tagged));
            Assert.Equal("\r\n-- card-list\r\n\r\nSELECT " + CardListHint.Hint + " 1", CardListHint.Apply("\r\n-- card-list\r\n\r\nSELECT 1"));
            Assert.Equal("SELECT 1", CardListHint.Apply("SELECT 1"));
            Assert.Equal("-- other\r\n\r\nSELECT 1", CardListHint.Apply("-- other\r\n\r\nSELECT 1"));
            Assert.Equal("-- card-list\r\n-- card-search\r\n\r\nSELECT " + CardListHint.SearchHint + " 1",
                CardListHint.Apply("-- card-list\r\n-- card-search\r\n\r\nSELECT 1"));
        }

        [Fact]
        public void SearchOverAllCards_KeepsMySqlOffTheRarityIndex()
        {
            using var db = new ModelIndexTests.MySqlContext();
            var text = "rem";

            var sql = CardListHint.Apply(db.Cards.AsQueryable().TagWith(CardListHint.Tag).TagWith(CardListHint.SearchTag)
                .Where(x => x.Name.Contains(text) || x.Title.Contains(text))
                .OrderByDescending(x => x.Rarity).ThenBy(x => x.Quality).ThenBy(x => x.BorderOverflow)
                .Select(x => x.Id).Skip(0).Take(30).ToQueryString());

            // the hint names the table by the alias EF gives it
            Assert.Contains("SELECT " + CardListHint.SearchHint + " `c`.`Id`", sql);
            Assert.Contains("FROM `Cards` AS `c`", sql);
            Assert.Contains("NO_ORDER_INDEX(c IX_Cards_Rarity_Quality_BorderOverflow)", CardListHint.SearchHint);
        }

        [Fact]
        public void CardLists_AreTaggedForTheHint()
        {
            using var db = _h.Open();

            var sql = db.Cards.AsQueryable().TagWith(CardListHint.Tag).Where(x => x.GameDeckId == Player).Select(x => x.Id).ToQueryString();

            Assert.StartsWith("-- " + CardListHint.Tag, sql);
        }
    }
}
