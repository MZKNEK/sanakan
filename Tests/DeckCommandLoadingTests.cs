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
    // wyprawa, waifu, ofiaruj, zniszcz, uwolnij, klatka, wyzwól, poświęć and loteria load only the cards they work on
    public class DeckCommandLoadingTests : IDisposable
    {
        private const ulong Player = 10;
        private const ulong FavoriteCharacter = 1002;

        private readonly WaifuCommandHarness _h = new WaifuCommandHarness();

        public void Dispose() => _h.Dispose();

        private Card NewCard(ulong id, ulong character, Dere dere = Dere.Deredere) => new Card
        {
            Id = id, Character = character, Name = $"postać {id}", Rarity = Rarity.A, Dere = dere, Affection = 60, Attack = 50, Defence = 20, Health = 100,
            ExpCnt = 10, MarketValue = 1, IsTradable = true, Tags = new List<Tag>(), CreationDate = _h.Time.Now()
        };

        private async Task SeedAsync(int alreadyOnExpedition = 1)
        {
            using var db = await _h.CreateAsync();

            var user = new User().Default(Player, _h.Time.Now());
            var deck = user.GameDeck;
            deck.Karma = 2500;
            deck.CTCnt = 1000;
            deck.Waifu = FavoriteCharacter;
            deck.ExpContainer.Level = ExpContainerLevel.Level1;
            deck.Cards.Add(NewCard(601, 1001, Dere.Yami));
            deck.Cards.Add(NewCard(602, FavoriteCharacter, Dere.Raito));
            deck.Cards.Add(NewCard(603, FavoriteCharacter));
            var caged = NewCard(604, 1004);
            caged.InCage = true;
            deck.Cards.Add(caged);
            caged = NewCard(605, 1005);
            caged.InCage = true;
            deck.Cards.Add(caged);
            deck.Cards.Add(NewCard(606, 1006));
            deck.Cards.Add(NewCard(607, 1007));
            var locked = NewCard(609, 1009);
            locked.IsTradable = false;
            deck.Cards.Add(locked);
            for (ulong i = 0; i < (ulong)alreadyOnExpedition; i++)
            {
                var away = NewCard(700 + i, 1700 + i);
                away.Expedition = CardExpedition.NormalItemWithExp;
                away.ExpeditionDate = _h.Time.Now();
                deck.Cards.Add(away);
            }
            deck.Items.Add(ItemType.BetterIncreaseUpgradeCnt.ToItem(15));
            deck.Items.Add(ItemType.LotteryTicket.ToItem(3));
            db.Users.Add(user);
            await db.SaveChangesAsync();

            var trash = await db.Tags.SingleAsync(x => x.Id == _h.Tags.GetTagId(TagType.TrashBin));
            var favorite = await db.Tags.SingleAsync(x => x.Id == _h.Tags.GetTagId(TagType.Favorite));
            (await db.Cards.Include(x => x.Tags).SingleAsync(x => x.Id == 606)).Tags.Add(trash);
            var both = await db.Cards.Include(x => x.Tags).SingleAsync(x => x.Id == 607);
            both.Tags.Add(trash);
            both.Tags.Add(favorite);
            await db.SaveChangesAsync();
        }

        private Task<string> RunAsync(Func<Sanakan.Modules.PocketWaifu, Task> command) => _h.RunAsync(Player, command);

        private User Read()
        {
            using var db = _h.Open();
            return db.Users.AsNoTracking().Include(x => x.Stats).Include(x => x.GameDeck).ThenInclude(x => x.Cards).ThenInclude(x => x.Tags)
                .Include(x => x.GameDeck).ThenInclude(x => x.Items).Include(x => x.GameDeck).ThenInclude(x => x.ExpContainer).AsSplitQuery().Single(x => x.Id == Player);
        }

        private static Card Card(User user, ulong id) => user.GameDeck.Cards.SingleOrDefault(x => x.Id == id);

        [Fact]
        public async Task Expedition_SendsTheCards()
        {
            await SeedAsync();

            // the reply goes out in the background with the player's avatar, which the test user does not have
            await RunAsync(x => x.SendCardToExpeditionAsync(CardExpedition.NormalItemWithExp, 601, 603));

            var user = Read();
            Assert.Equal(CardExpedition.NormalItemWithExp, Card(user, 601).Expedition);
            Assert.Equal(CardExpedition.NormalItemWithExp, Card(user, 603).Expedition);
            Assert.Equal(CardExpedition.None, Card(user, 602).Expedition);
            Assert.Equal(9, user.GameDeck.Cards.Count);
        }

        [Fact]
        public async Task Expedition_CountsTheCardsAlreadyAway()
        {
            await SeedAsync(alreadyOnExpedition: 9);

            var reply = await RunAsync(x => x.SendCardToExpeditionAsync(CardExpedition.NormalItemWithExp, 601, 603));

            Assert.Contains("(obecnie: 9)", reply);
            Assert.Equal(CardExpedition.None, Card(Read(), 601).Expedition);
        }

        [Fact]
        public async Task Waifu_LowersTheCardsOfThePreviousFavorite()
        {
            await SeedAsync();

            Assert.Contains("ustawił", await RunAsync(x => x.SetProfileWaifuAsync(601)));

            var user = Read();
            Assert.Equal(1001ul, user.GameDeck.Waifu);
            Assert.Equal(55, Card(user, 602).Affection, 6);
            Assert.Equal(55, Card(user, 603).Affection, 6);
            Assert.Equal(60, Card(user, 601).Affection, 6);
            Assert.Equal(60, Card(user, 606).Affection, 6);
        }

        [Fact]
        public async Task WaifuReset_LowersTheCardsOfTheFavorite()
        {
            await SeedAsync();

            Assert.Contains("zresetował", await RunAsync(x => x.SetProfileWaifuAsync(0)));

            var user = Read();
            Assert.Equal(0ul, user.GameDeck.Waifu);
            Assert.Equal(55, Card(user, 602).Affection, 6);
            Assert.Equal(55, Card(user, 603).Affection, 6);
            Assert.Equal(60, Card(user, 601).Affection, 6);
        }

        [Fact]
        public async Task Donate_UsesTheItemsAlreadyOwned()
        {
            await SeedAsync();

            Assert.Contains("nowy charakter to Yato", await RunAsync(x => x.ChangeCardAsync(601)));

            var user = Read();
            Assert.Equal(Dere.Yato, Card(user, 601).Dere);
            Assert.Equal(3, user.GameDeck.Items.Single(x => x.Type == ItemType.BetterIncreaseUpgradeCnt).Count);
            Assert.Equal(1, user.Stats.YatoUpgrades);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task DiscardingByIds_RemovesOnlyThoseCards(bool release)
        {
            await SeedAsync();

            await RunAsync(x => release ? x.ReleaseCardAsync(601, 603) : x.DestroyCardAsync(601, 603));

            var user = Read();
            Assert.Null(Card(user, 601));
            Assert.Null(Card(user, 603));
            Assert.Equal(7, user.GameDeck.Cards.Count);
            Assert.Equal(release ? 2 : 0, user.Stats.ReleasedCards);
            Assert.Equal(release ? 0 : 2, user.Stats.DestroyedCards);
            Assert.True(user.GameDeck.ExpContainer.ExpCount > 0);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task DiscardingTheTrash_SkipsTheFavorites(bool release)
        {
            await SeedAsync();

            var reply = await RunAsync(x => release ? x.ReleaseCardAsync() : x.DestroyCardAsync());

            Assert.Contains("Nie udało się", reply);
            var user = Read();
            Assert.Null(Card(user, 606));
            Assert.NotNull(Card(user, 607));
            Assert.Equal(8, user.GameDeck.Cards.Count);
        }

        [Fact]
        public async Task Cage_OpensAllCagedCards()
        {
            await SeedAsync();

            Assert.Contains("wyciągnął 2 karty", await RunAsync(x => x.OpenCageAsync()));

            var user = Read();
            Assert.False(Card(user, 604).InCage);
            Assert.False(Card(user, 605).InCage);
            Assert.Equal(2500.01, user.GameDeck.Karma, 6);
        }

        [Fact]
        public async Task Cage_TakesOneCard_AndTheOthersMissIt()
        {
            await SeedAsync();

            Assert.Contains("wyciągnął 1 kartę", await RunAsync(x => x.OpenCageAsync(604)));

            var user = Read();
            Assert.False(Card(user, 604).InCage);
            Assert.True(Card(user, 605).InCage);
            Assert.Equal(59.7, Card(user, 605).Affection, 6);
            Assert.Equal(60, Card(user, 601).Affection, 6);
        }

        [Fact]
        public async Task Unleash_MakesTheCardTradable()
        {
            await SeedAsync();

            Assert.Contains("wyzwolił kartę", await RunAsync(x => x.UnleashCardAsync(609)));

            var user = Read();
            Assert.True(Card(user, 609).IsTradable);
            Assert.Equal(850, user.GameDeck.CTCnt);
        }

        [Fact]
        public async Task Sacrifice_GivesTheExperienceToTheCard()
        {
            await SeedAsync();

            Assert.Contains("ulepszył kartę", await RunAsync(x => x.SacraficeCardMultiAsync(601, 603, 606)));

            var user = Read();
            Assert.Null(Card(user, 603));
            Assert.Null(Card(user, 606));
            Assert.True(Card(user, 601).ExpCnt > 10);
            Assert.Equal(2, user.Stats.SacraficeCards);
            Assert.Equal(7, user.GameDeck.Cards.Count);
        }

        [Fact]
        public async Task Lottery_UsesTheTickets()
        {
            await SeedAsync();

            Assert.Contains("wygrał na loterii", await RunAsync(x => x.GoToLotteryAsync(3)));

            var user = Read();
            Assert.DoesNotContain(user.GameDeck.Items, x => x.Type == ItemType.LotteryTicket);
            Assert.Equal(3, user.Stats.LotteryTicketsUsed);
            Assert.Equal(9, user.GameDeck.Cards.Count);
        }

        [Fact]
        public async Task LotteryKarmaReversal_FlipsTheLoadedCards()
        {
            await SeedAsync();
            var lottery = new Lottery(_h.Shinden);

            using (var db = _h.Open())
            {
                var user = await db.GetUserOrCreateForLotteryAsync(Player, _h.Time);
                Assert.Equal(new ulong[] { 601, 602 }, user.GameDeck.Cards.Select(x => x.Id).OrderBy(x => x));
                // the rewards are drawn with a crypto random, so draw until the reversal comes
                for (int i = 0; i < 20000 && user.Stats.ReversedKarmaCnt == 0; i++)
                    await lottery.GetAndApplyRewardAsync(user);
                Assert.Equal(1, user.Stats.ReversedKarmaCnt);
                await db.SaveChangesAsync();
            }

            var after = Read();
            Assert.Equal(Dere.Raito, Card(after, 601).Dere);
            Assert.Equal(Dere.Yami, Card(after, 602).Dere);
            Assert.Equal(Dere.Deredere, Card(after, 603).Dere);
            Assert.Equal(-2500, after.GameDeck.Karma, 0);
        }
    }
}
