using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Xunit;

namespace Artifacts
{
    // ulepsz loads only the upgraded card and the card to sacrifice; the rest of the collection must stay untouched
    public class UpgradeLoadingTests : IDisposable
    {
        private const ulong Player = 10;

        private readonly WaifuCommandHarness _h = new WaifuCommandHarness();

        public void Dispose() => _h.Dispose();

        private static Card NewCard(ulong id, Rarity rarity, Quality quality = Quality.Broken) => new Card
        {
            Id = id, Character = id, Name = $"postać {id}", Rarity = rarity, Quality = quality, FromFigure = quality != Quality.Broken,
            UpgradesCnt = rarity == Rarity.SS ? 5 : 1, ExpCnt = 5000, Affection = 60, Attack = 50, Defence = 20, Health = 100,
            Tags = new List<Tag>(), CreationDate = new DateTime(2026, 1, 1)
        };

        private static Figure NewFigure(ulong character, bool focus, ulong createdCard) => new Figure
        {
            Character = character, Name = "figurka", IsFocus = focus, IsComplete = createdCard != 0, CreatedCardId = createdCard, ExpCnt = 5000,
            SkeletonQuality = Quality.Alpha, HeadQuality = Quality.Alpha, BodyQuality = Quality.Alpha, LeftArmQuality = Quality.Alpha,
            RightArmQuality = Quality.Alpha, LeftLegQuality = Quality.Alpha, RightLegQuality = Quality.Alpha, ClothesQuality = Quality.Alpha,
            CompletionDate = new DateTime(2026, 1, 1)
        };

        private async Task SeedAsync()
        {
            using var db = await _h.CreateAsync();

            var user = new User().Default(Player, _h.Time.Now());
            var deck = user.GameDeck;
            deck.Cards.Add(NewCard(501, Rarity.A));
            deck.Cards.Add(NewCard(502, Rarity.SS));
            deck.Cards.Add(NewCard(503, Rarity.SSS, Quality.Alpha));
            deck.Cards.Add(NewCard(504, Rarity.SSS, Quality.Alpha));
            deck.Cards.Add(NewCard(505, Rarity.SSS, Quality.Alpha));
            deck.Cards.Add(NewCard(506, Rarity.B));
            deck.Figures.Add(NewFigure(503, true, 0));
            deck.Figures.Add(NewFigure(504, false, 504));
            deck.Figures.Add(NewFigure(505, false, 505));
            deck.Items.Add(ItemType.SetCustomImage.ToItem(1));
            db.Users.Add(user);
            await db.SaveChangesAsync();

            var favorite = await db.Tags.SingleAsync(x => x.Id == _h.Tags.GetTagId(Sanakan.Services.PocketWaifu.TagType.Favorite));
            (await db.Cards.Include(x => x.Tags).SingleAsync(x => x.Id == 505)).Tags.Add(favorite);
            (await db.Cards.Include(x => x.Tags).SingleAsync(x => x.Id == 506)).Tags.Add(favorite);
            await db.SaveChangesAsync();
        }

        private Task<string> UpgradeAsync(ulong id, ulong sacrifice = 0) => _h.RunAsync(Player, x => x.UpgradeCardAsync(id, sacrifice));

        private (Dictionary<ulong, Card> Cards, GameDeck Deck, UserStats Stats) Read()
        {
            using var db = _h.Open();
            var user = db.Users.AsNoTracking().Include(x => x.Stats).Include(x => x.GameDeck).ThenInclude(x => x.Cards).ThenInclude(x => x.Tags)
                .Include(x => x.GameDeck).ThenInclude(x => x.Figures).Include(x => x.GameDeck).ThenInclude(x => x.Items).AsSplitQuery().Single(x => x.Id == Player);
            return (user.GameDeck.Cards.ToDictionary(x => x.Id), user.GameDeck, user.Stats);
        }

        [Fact]
        public async Task NormalUpgrade_ChangesOnlyTheCard()
        {
            await SeedAsync();

            var reply = await UpgradeAsync(501);

            Assert.Contains("ulepszył kartę", reply);
            var (cards, deck, stats) = Read();
            Assert.Equal(Rarity.S, cards[501].Rarity);
            Assert.Equal(6, cards.Count);
            Assert.Single(cards[506].Tags);
            Assert.Equal(3, deck.Figures.Count);
            Assert.Equal(1, stats.UpgaredCards);
            Assert.Equal(1, deck.Karma);
        }

        [Fact]
        public async Task FirstSSS_AddsTheRewardToTheItemsAlreadyOwned()
        {
            await SeedAsync();

            var reply = await UpgradeAsync(502);

            Assert.Contains("ulepszył kartę", reply);
            var (cards, deck, stats) = Read();
            Assert.Equal(Rarity.SSS, cards[502].Rarity);
            Assert.Equal(2, Assert.Single(deck.Items, x => x.Type == ItemType.SetCustomImage).Count);
            Assert.Equal(1, stats.UpgradedToSSS);
            using var db = _h.Open();
            Assert.Single(db.UserActivities.AsNoTracking().Where(x => x.Type == Sanakan.Database.Models.ActivityType.CreatedSSS));
        }

        [Fact]
        public async Task UltimateWithTheFocusedFigure_UsesUpTheFigure()
        {
            await SeedAsync();

            var reply = await UpgradeAsync(503);

            Assert.Contains("ulepszył kartę", reply);
            var (cards, deck, _) = Read();
            Assert.Equal(Quality.Beta, cards[503].Quality);
            Assert.Equal(new ulong[] { 504, 505 }, deck.Figures.Select(x => x.CreatedCardId).OrderBy(x => x));
            Assert.Equal(6, cards.Count);
        }

        [Fact]
        public async Task UltimateWithASacrificedCard_RemovesItAndItsFigure()
        {
            await SeedAsync();

            var reply = await UpgradeAsync(503, 504);

            Assert.Contains("ulepszył kartę", reply);
            var (cards, deck, _) = Read();
            Assert.Equal(Quality.Beta, cards[503].Quality);
            Assert.False(cards.ContainsKey(504));
            Assert.Equal(5, cards.Count);
            Assert.DoesNotContain(deck.Figures, x => x.CreatedCardId == 504);
            Assert.Equal(2, deck.Figures.Count);
        }

        [Fact]
        public async Task FavoriteSacrifice_IsRefused()
        {
            await SeedAsync();

            var reply = await UpgradeAsync(503, 505);

            Assert.Contains("ulubiona", reply);
            var (cards, deck, _) = Read();
            Assert.Equal(Quality.Alpha, cards[503].Quality);
            Assert.True(cards.ContainsKey(505));
            Assert.Equal(3, deck.Figures.Count);
        }

        [Theory]
        [InlineData(999ul, 0ul)]
        [InlineData(503ul, 999ul)]
        public async Task MissingCard_IsRefused(ulong id, ulong sacrifice)
        {
            await SeedAsync();

            Assert.Contains("nie posiadasz takiej karty", await UpgradeAsync(id, sacrifice));
            Assert.Equal(6, Read().Cards.Count);
        }
    }
}
