using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using WaifuModule = Sanakan.Modules.PocketWaifu;
using Xunit;

namespace Artifacts
{
    // the harem and both card game profiles read only what they show and show the same as with the whole profile
    public class CardGameProfileLoadingTests : IDisposable
    {
        private const ulong Player = 10;
        private const ulong Other = 20;
        private const ulong Favorite = 1002;

        private readonly WaifuCommandHarness _h = new WaifuCommandHarness();

        public void Dispose() => _h.Dispose();

        private Card NewCard(ulong id, ulong character, Rarity rarity, bool ultimate = false, Quality quality = Quality.Broken, int restarts = 0) => new Card
        {
            Id = id, Character = character, Name = $"postać {id}", Rarity = rarity, FromFigure = ultimate, Quality = quality, RestartCnt = restarts,
            Affection = 60, Attack = 50, Defence = 20, Health = 100, MarketValue = 1, IsTradable = true, Tags = new List<Tag>(), CreationDate = _h.Time.Now()
        };

        private async Task SeedAsync(ulong premium = 0, ulong waifu = Favorite)
        {
            using var db = await _h.CreateAsync();

            var user = new User().Default(Player, _h.Time.Now());
            var deck = user.GameDeck;
            deck.Waifu = waifu;
            deck.PremiumWaifu = premium;
            deck.CTCnt = 3200;
            deck.Karma = 12.5;
            deck.GlobalPVPRank = 1500;
            deck.ExpContainer.Level = ExpContainerLevel.Level2;
            deck.ExpContainer.ExpCount = 321;
            user.Stats.ReleasedCards = 4;
            user.Stats.DestroyedCards = 7;

            var tag = new Tag { Name = "konie", GameDeck = deck };
            deck.Tags.Add(tag);
            // inserted out of id order on purpose
            deck.Cards.Add(NewCard(905, 1005, Rarity.SSS, true, Quality.Gamma));
            deck.Cards.Add(NewCard(901, 1001, Rarity.SSS, restarts: 2));
            deck.Cards.Add(NewCard(903, Favorite, Rarity.A, restarts: 3));
            deck.Cards.Add(NewCard(902, 1001, Rarity.SS));
            deck.Cards.Add(NewCard(904, Favorite, Rarity.B));
            deck.Cards.Add(NewCard(906, 1006, Rarity.SSS, true, Quality.Alpha));
            deck.Cards.Add(NewCard(907, 1007, Rarity.SSS, true, Quality.Gamma));
            deck.Cards.Add(NewCard(908, 1008, Rarity.E));
            deck.Cards.Single(x => x.Id == 902).Tags.Add(tag);
            foreach (var (type, result) in new[] { (FightType.NewVersus, FightResult.Win), (FightType.NewVersus, FightResult.Win),
                (FightType.NewVersus, FightResult.Lose), (FightType.NewVersus, FightResult.Draw), (FightType.Versus, FightResult.Win),
                (FightType.BattleRoyale, FightResult.Win) })
                deck.PvPStats.Add(new CardPvPStats { Type = type, Result = result });
            db.Users.Add(user);

            var other = new User().Default(Other, _h.Time.Now());
            other.GameDeck.Cards.Add(NewCard(951, Favorite, Rarity.SSS, true, Quality.Omega, 9));
            other.GameDeck.PvPStats.Add(new CardPvPStats { Type = FightType.NewVersus, Result = FightResult.Win });
            db.Users.Add(other);
            await db.SaveChangesAsync();
        }

        private User Full(ulong id)
        {
            using var db = _h.Open();
            return db.GetCachedFullUserAsync(id).GetAwaiter().GetResult();
        }

        [Fact]
        public async Task CardGameProfile_FromTheDatabaseSummary_IsTheSameAsFromTheWholeProfile()
        {
            await SeedAsync();
            var now = _h.Time.Now();

            using var db = _h.Open();
            var narrow = await db.GetUserWithWaifuCardsAsync(Player);
            var summary = await WaifuModule.CardGameSummary.LoadAsync(db, Player);

            var expected = WaifuModule.BuildCardGameProfile(Full(Player), now).Description;
            Assert.Equal(expected, WaifuModule.BuildCardGameProfile(narrow, summary, now).Description);
            Assert.Contains("**Posiadane karty**: 8", expected);
            Assert.Contains("**SSS**: 1 **SS**: 1", expected);
            Assert.Contains("**[3]**", expected);
            Assert.Contains("**Restartów na kartach**: 5", expected);
            Assert.Contains("**PVP** Rozegrane: 4 Wygrane: 2", expected);
        }

        [Fact]
        public async Task CardGameSummary_OfAnEmptyDeck_HasNothing()
        {
            using (var db = await _h.CreateAsync())
            {
                db.Users.Add(new User().Default(Player, _h.Time.Now()));
                await db.SaveChangesAsync();
            }

            using var read = _h.Open();
            var summary = await WaifuModule.CardGameSummary.LoadAsync(read, Player);

            Assert.Equal(0, summary.Total);
            Assert.Equal(0, summary.Ultimate);
            Assert.Equal(0, summary.Played);
            Assert.Equal(WaifuModule.BuildCardGameProfile(Full(Player), _h.Time.Now()).Description,
                WaifuModule.BuildCardGameProfile(await read.GetUserWithWaifuCardsAsync(Player), summary, _h.Time.Now()).Description);
        }

        [Theory]
        [InlineData(0ul, Favorite, 903ul)]
        [InlineData(902ul, Favorite, 902ul)]
        [InlineData(951ul, Favorite, null)]
        [InlineData(0ul, 0ul, null)]
        public async Task WaifuCard_IsTheSameAsFromTheWholeProfile(ulong premium, ulong waifu, ulong? expected)
        {
            await SeedAsync(premium, waifu);

            using var db = _h.Open();
            var narrow = await db.GetUserWithWaifuCardsAsync(Player);

            Assert.Equal(expected, narrow.GameDeck.GetWaifuCard()?.Id);
            Assert.Equal(Full(Player).GameDeck.GetWaifuCard()?.Id, narrow.GameDeck.GetWaifuCard()?.Id);
            Assert.Equal(321, narrow.GameDeck.ExpContainer.ExpCount, 6);
            Assert.Equal(4, narrow.Stats.ReleasedCards);
            Assert.Null(narrow.GameDeck.PvPStats);
        }

        [Fact]
        public async Task WaifuCards_OfAMissingPlayer_AreNull()
        {
            await SeedAsync();

            using var db = _h.Open();
            Assert.Null(await db.GetUserWithWaifuCardsAsync(12345));
        }

        [Fact]
        public async Task Harem_GetsTheWholeCollectionWithTagsInIdOrder()
        {
            await SeedAsync();

            using var db = _h.Open();
            var cards = await db.GetAllCardsAsync(Player);

            Assert.Equal(Full(Player).GameDeck.Cards.Select(x => x.Id).OrderBy(x => x), cards.Select(x => x.Id));
            Assert.Equal("konie", Assert.Single(cards.Single(x => x.Id == 902).Tags).Name);
            Assert.Equal(new ulong[] { 902 }, new Sanakan.Services.PocketWaifu.Waifu(null, null, null, new ListLogger(), null, null, null, _h.Time, _h.Shinden, _h.Tags, _h.Config).GetListInRightOrder(cards, Sanakan.Services.PocketWaifu.HaremType.Tag, "konie").Select(x => x.Id));
            Assert.Empty(await db.GetAllCardsAsync(12345));
        }
    }
}
