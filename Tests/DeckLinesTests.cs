using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services.PocketWaifu;
using Sanakan.Services.PocketWaifu.Fight;
using Xunit;

namespace Artifacts
{
    public class DeckLinesTests
    {
        private static ulong _nextId = 5000;

        private static Card NewCard(int health, CardLine line = CardLine.Auto, double crit = 0, ulong deck = 1, Dere dere = Dere.Kamidere) => new Card
        {
            Id = _nextId++,
            Name = $"karta {health}",
            Dere = dere,
            Rarity = Rarity.SSS,
            Health = health,
            Attack = 100,
            Defence = 50,
            CritChance = crit,
            Line = line,
            GameDeckId = deck,
            Image = "https://example.com/a.png",
            Tags = new List<Tag>(),
        };

        [Fact]
        public void Auto_PutsTheCardsWithTheMostHealthInFront_AndKeepsPrecisionBack()
        {
            var deck = new[] { 900, 800, 700, 600, 500, 400, 300, 200 }.Select(hp => NewCard(hp)).ToList();
            deck[0].CritChance = DeckLines.AutoBackPrecision;

            var front = DeckLines.GetFrontCards(deck);

            Assert.Equal(new[] { 800, 700, 600, 500 }, deck.Where(x => front.Contains(x.Id)).Select(x => x.Health));
        }

        [Fact]
        public void PlayerSettings_GoFirst_AndBackIsNeverInFront()
        {
            var deck = new[] { 900, 800, 700, 600, 500, 400, 300, 200 }.Select(hp => NewCard(hp)).ToList();
            deck[7].Line = CardLine.Front;
            deck[6].Line = CardLine.Front;
            deck[0].Line = CardLine.Back;

            var front = DeckLines.GetFrontCards(deck);

            Assert.Equal(new[] { 800, 700, 300, 200 }, deck.Where(x => front.Contains(x.Id)).Select(x => x.Health));
        }

        [Theory]
        [InlineData(1, 0)]
        [InlineData(3, 1)]
        [InlineData(5, 2)]
        [InlineData(8, 4)]
        public void Front_HoldsAtMostFourCardsAndHalfOfTheDeck(int cards, int expected)
        {
            var deck = Enumerable.Range(0, cards).Select(_ => NewCard(500, CardLine.Front)).ToList();

            Assert.Equal(expected, DeckLines.GetFrontCards(deck).Count);
        }

        [Fact]
        public void FrontCards_AreTargetedTwiceAsOften()
        {
            var enemies = new List<(string Name, bool Front)> { ("przód", true), ("tył", false) };
            var front = Enumerable.Range(0, 30000).Count(_ => DeckLines.PickTarget(enemies, x => x.Front).Front) / 30000d;

            Assert.InRange(front, 0.64, 0.69);
            var withoutFront = new List<(string Name, bool Front)> { ("a", false), ("b", false) };
            Assert.Equal(2, Enumerable.Range(0, 200).Select(_ => DeckLines.PickTarget(withoutFront, x => x.Front).Name).Distinct().Count());
        }

        [Fact]
        public void FrontCards_DoNotHitCritically()
        {
            var target = new CardWithHealth { Card = NewCard(100000, deck: 2), Health = 100000, MaxHealth = 100000 };
            var round = new RoundInfo();
            var front = new CardWithHealth { Card = NewCard(1000, crit: 100), Health = 1000, MaxHealth = 1000, Front = true };
            var back = new CardWithHealth { Card = NewCard(1000, crit: 100), Health = 1000, MaxHealth = 1000 };

            Waifu.Strike(round, front, target, false);
            Waifu.Strike(round, back, target, false);

            Assert.False(round.Fights[0].Critical);
            Assert.True(round.Fights[1].Critical);
        }

        [Fact]
        public void FullLog_ShowsWhereEachCardStands()
        {
            var mine = new[] { 900, 800 }.Select(hp => NewCard(hp)).ToList();
            mine[1].Line = CardLine.Back;
            var players = new List<PlayerInfo> { new PlayerInfo { Cards = mine }, new PlayerInfo { Cards = new List<Card> { NewCard(500, deck: 2) } } };

            var log = Waifu.GetFullFightLog(new FightHistory(null), players);

            Assert.Contains("karta 900 (WID", log);
            Assert.Contains("· przód (auto) ·", log);
            Assert.Contains("· tył ·", log);
        }

        [Fact]
        public async Task Command_SetsTheLineOfTheCallersCard()
        {
            using var harness = new WaifuCommandHarness();
            ulong own, other;
            using (var db = await harness.CreateAsync())
            {
                var user = new User().Default(95, harness.Time.Now());
                var card = NewCard(500, deck: 95);
                card.Active = true;
                user.GameDeck.Cards.Add(card);
                user.GameDeck.Cards.Add(NewCard(400, deck: 95));
                db.Users.Add(user);
                var stranger = new User().Default(96, harness.Time.Now());
                var theirs = NewCard(300, deck: 96);
                stranger.GameDeck.Cards.Add(theirs);
                db.Users.Add(stranger);
                await db.SaveChangesAsync();
                own = card.Id;
                other = theirs.Id;
            }

            var reply = await harness.RunAsync(95, m => m.SetCardLineAsync(own, "tył"));
            Assert.Contains("**tył**", reply);
            Assert.Contains("W walce stoi z tyłu.", reply);
            Assert.Contains("nie odnaleziono karty", await harness.RunAsync(95, m => m.SetCardLineAsync(other, "przód")));
            Assert.Contains("nie rozpoznano linii", await harness.RunAsync(95, m => m.SetCardLineAsync(own, "bok")));

            using var check = harness.Open();
            Assert.Equal(CardLine.Back, (await check.Cards.AsNoTracking().SingleAsync(x => x.Id == own)).Line);
            Assert.Equal(CardLine.Auto, (await check.Cards.AsNoTracking().SingleAsync(x => x.Id == other)).Line);
        }
    }
}
