using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services.PocketWaifu;
using Xunit;

namespace Artifacts
{
    // reset, chest, chest creation, figure end, premium waifu, wishlist view, tag order and the character pool load only what they use
    public class RareCommandLoadingTests : IDisposable
    {
        private const ulong Player = 10;
        private const ulong Favorite = 1002;

        private readonly WaifuCommandHarness _h = new WaifuCommandHarness();

        public void Dispose() => _h.Dispose();

        private Card NewCard(ulong id, ulong character, Rarity rarity) => new Card
        {
            Id = id, Character = character, Name = $"postać {id}", Rarity = rarity, Affection = 60, Attack = 50, Defence = 20, Health = 100,
            ExpCnt = 10, MarketValue = 1, IsTradable = true, Tags = new List<Tag>(), CreationDate = _h.Time.Now()
        };

        private async Task SeedAsync()
        {
            using var db = await _h.CreateAsync();

            var user = new User().Default(Player, _h.Time.Now());
            var deck = user.GameDeck;
            deck.Waifu = Favorite;
            deck.PremiumWaifu = 803;
            deck.CTCnt = 1000;
            user.TcCnt = 5000;
            deck.ExpContainer.Level = ExpContainerLevel.Level1;
            deck.ExpContainer.ExpCount = 100;
            deck.Cards.Add(NewCard(801, 1001, Rarity.SSS));
            deck.Cards.Add(NewCard(802, 1001, Rarity.A));
            deck.Cards.Add(NewCard(803, Favorite, Rarity.A));
            deck.Cards.Add(NewCard(804, Favorite, Rarity.B));
            deck.Cards.Add(NewCard(805, 1005, Rarity.SSS));
            deck.Cards.Add(NewCard(806, 1006, Rarity.C));
            deck.Items.Add(ItemType.BetterIncreaseUpgradeCnt.ToItem(5));
            deck.Items.Add(ItemType.BloodOfYourWaifu.ToItem(5));
            deck.Wishes.Add(new WishlistObject { Type = WishlistObjectType.Character, ObjectId = 77, ObjectName = "x", Entry = WishlistEntryType.Normal });
            deck.Figures.Add(new Figure
            {
                Character = 1009, Name = "figurka", IsFocus = true, ExpCnt = 5000, SkeletonQuality = Quality.Alpha, HeadQuality = Quality.Alpha,
                BodyQuality = Quality.Alpha, LeftArmQuality = Quality.Alpha, RightArmQuality = Quality.Alpha, LeftLegQuality = Quality.Alpha,
                RightLegQuality = Quality.Alpha, ClothesQuality = Quality.Alpha, CompletionDate = new DateTime(2026, 1, 1)
            });
            db.Users.Add(user);
            db.WishlistCountData.Add(new Sanakan.Database.Models.Analytics.WishlistCount { Id = 77, Name = "x", Count = 4, ACount = 4 });
            await db.SaveChangesAsync();
        }

        private Task<string> RunAsync(Func<Sanakan.Modules.PocketWaifu, Task> command) => _h.RunAsync(Player, command);

        private User Read()
        {
            using var db = _h.Open();
            return db.Users.AsNoTracking().Include(x => x.Stats).Include(x => x.GameDeck).ThenInclude(x => x.Cards)
                .Include(x => x.GameDeck).ThenInclude(x => x.Items).Include(x => x.GameDeck).ThenInclude(x => x.Figures)
                .Include(x => x.GameDeck).ThenInclude(x => x.ExpContainer).AsSplitQuery().Single(x => x.Id == Player);
        }

        private static Card Card(User user, ulong id) => user.GameDeck.Cards.SingleOrDefault(x => x.Id == id);

        [Fact]
        public async Task Reset_RestartsOnlyThatCard()
        {
            await SeedAsync();

            Assert.Contains("zrestartował", await RunAsync(x => x.ResetCardAsync(801)));

            var user = Read();
            Assert.Equal(Rarity.E, Card(user, 801).Rarity);
            Assert.Equal(1, Card(user, 801).RestartCnt);
            Assert.Equal(Rarity.SSS, Card(user, 805).Rarity);
            Assert.Equal(6, user.GameDeck.Cards.Count);
        }

        [Fact]
        public async Task Chest_GivesExperienceToTheCard()
        {
            await SeedAsync();
            using (var db = _h.Open())
            {
                (await db.Figures.SingleAsync()).IsFocus = false;
                await db.SaveChangesAsync();
            }

            Assert.Contains("przeniesiono", await RunAsync(x => x.TransferExpFromChestAsync(802, 20)));

            var user = Read();
            Assert.Equal(30, Card(user, 802).ExpCnt, 6);
            Assert.Equal(80, user.GameDeck.ExpContainer.ExpCount, 6);
            Assert.Equal(992, user.GameDeck.CTCnt);
        }

        [Fact]
        public async Task ChestCreation_SacrificesTheCard_AndUsesTheOwnedBlood()
        {
            await SeedAsync();

            Assert.Contains("skrzynię", await RunAsync(x => x.CreateChestAsync(805)));

            var user = Read();
            Assert.Null(Card(user, 805));
            Assert.Equal(5, user.GameDeck.Cards.Count);
            Assert.Equal(ExpContainerLevel.Level2, user.GameDeck.ExpContainer.Level);
            Assert.True(user.GameDeck.Items.Sum(x => x.Count) < 10);
        }

        [Fact]
        public async Task FigureEnd_AddsTheUltimateCard()
        {
            await SeedAsync();

            // everything is saved before the reply, which needs the player's avatar the test user does not have
            await Assert.ThrowsAsync<NullReferenceException>(() => RunAsync(x => x.EndCreatingFigureAsync()));

            var user = Read();
            Assert.Equal(7, user.GameDeck.Cards.Count);
            var figure = Assert.Single(user.GameDeck.Figures);
            Assert.True(figure.IsComplete);
            Assert.Equal(1009ul, Card(user, figure.CreatedCardId).Character);
        }

        [Fact]
        public async Task PremiumWaifu_LowersThePreviousPremiumAndFavoriteCards()
        {
            await SeedAsync();

            Assert.Contains("Kupiłeś waifu", await RunAsync(x => x.SetCardAsPremiumWaifuAsync(802)));

            var user = Read();
            Assert.Equal(802ul, user.GameDeck.PremiumWaifu);
            Assert.Equal(1001ul, user.GameDeck.Waifu);
            Assert.Equal(60 - 50 - 15, Card(user, 803).Affection, 6);
            Assert.Equal(60 - 15, Card(user, 804).Affection, 6);
            Assert.Equal(60, Card(user, 805).Affection, 6);
            Assert.Equal(4000, user.TcCnt);
        }

        [Fact]
        public async Task WishlistView_ChangesTheCountsOfTheWishes()
        {
            await SeedAsync();

            await RunAsync(x => x.SetWishlistViewAsync(false));

            using var db = _h.Open();
            Assert.True(db.GameDecks.AsNoTracking().Single(x => x.Id == Player).WishlistIsPrivate);
            Assert.Equal(3, db.WishlistCountData.AsNoTracking().Single(x => x.Id == 77).Count);
        }

        [Fact]
        public async Task TagOrder_AndCharacterPool_ChangeOnlyTheirFields()
        {
            await SeedAsync();

            await RunAsync(x => x.SortTagsAsync(TagsOrder.Alphabetically));
            await RunAsync(x => x.SetCharacterPoolAsync());

            var user = Read();
            Assert.Equal(TagsOrder.Alphabetically, user.GameDeck.TagsOrder);
            Assert.Equal(CharacterPoolType.All, user.PoolType);
            Assert.Equal(6, user.GameDeck.Cards.Count);
        }
    }
}
