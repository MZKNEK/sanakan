using System;
using System.Collections.Generic;
using System.Linq;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services.PocketWaifu;
using Sanakan.Services.PocketWaifu.Fight;
using Xunit;

namespace Artifacts
{
    public class CriticalHitTests
    {
        // Kamidere and Dandere hit each other without a dere multiplier
        private static Card Card(bool ultimate, int attack, int defence, double crit = 0, Dere dere = Dere.Kamidere) => new Card
        {
            FromFigure = ultimate,
            Rarity = Rarity.SSS,
            Attack = attack,
            Defence = defence,
            Health = 500,
            Dere = dere,
            CritChance = crit,
            Image = "https://example.com/a.png",
            Tags = new List<Tag>(),
        };

        [Fact]
        public void Critical_OnNormalCards_OnlyMultipliesTheDamage()
            => Assert.Equal(15, Waifu.GetFA(Card(false, 100, 0), Card(false, 0, 90, dere: Dere.Dandere), true), 3);

        [Fact]
        public void Critical_InUltimateFights_AlsoHalvesTheDefence()
            => Assert.Equal((3000 - 1000) * 1.5, Waifu.GetFA(Card(true, 3000, 0), Card(true, 0, 2000, dere: Dere.Dandere), true), 3);

        [Fact]
        public void ExpectedDamage_MixesNormalAndCriticalHits()
        {
            var attacker = Card(false, 100, 0, crit: 20);
            var target = Card(false, 0, 90, dere: Dere.Dandere);

            Assert.Equal(0.8 * 10 + 0.2 * 15, Waifu.GetExpectedFA(attacker, target), 3);
        }

        [Fact]
        public void Power_DoesNotChangeWithoutPrecision_AndGrowsWithIt()
        {
            var plain = Card(true, 3000, 3000);
            plain.Health = 10000;
            var precise = Card(true, 3000, 3000, crit: 40);
            precise.Health = 10000;

            Assert.Equal(7933.24, plain.CalculateCardPower(), 1);
            Assert.True(precise.CalculateCardPower() > plain.CalculateCardPower() * 1.1);
        }

        [Theory]
        [InlineData(false, 0, 250)]
        [InlineData(false, 24.9, 1)]
        [InlineData(false, 25, 0)]
        [InlineData(true, 25, 150)]
        [InlineData(true, 40, 0)]
        public void Glasses_StopAtTheLimit(bool ultimate, double crit, long expected)
            => Assert.Equal(expected, Card(ultimate, 100, 50, crit).UsefulPrecisionItems());

        [Fact]
        public void Glasses_AddATenthWithoutRoundingErrors()
        {
            var card = Card(false, 100, 50);
            for (int i = 0; i < 3; i++)
                card.AddCritChance(1);

            Assert.Equal(0.3, card.CritChance);
            card.AddCritChance(1000);
            Assert.Equal(CardExtension.MaxCritChance, card.CritChance);
        }

        [Fact]
        public void Precision_TravelsFromTheCardThroughTheFigure()
        {
            var card = Card(false, 100, 50, crit: 12.5);
            var figure = ItemType.FigureSkeleton.ToItem(1, Quality.Alpha).ToFigure(card, DateTime.Now);

            Assert.Equal(12.5, figure.CritChance);
            Assert.Equal(12.5, figure.ToCard(DateTime.Now).CritChance);
        }

        [Fact]
        public void FightLog_CountsCriticalHitsPerPlayer()
        {
            var a = Card(false, 100, 50); a.Id = 1;
            var b = Card(false, 100, 50); b.Id = 2;
            var players = new List<PlayerInfo> { new PlayerInfo { Cards = new List<Card> { a } }, new PlayerInfo { Cards = new List<Card> { b } } };
            var round = new RoundInfo();
            round.Fights.Add(new AttackInfo { AtkCardId = 1, DefCardId = 2, Critical = true });
            round.Fights.Add(new AttackInfo { AtkCardId = 1, DefCardId = 2, Critical = true });
            round.Fights.Add(new AttackInfo { AtkCardId = 2, DefCardId = 1, Critical = false });
            var fight = new FightHistory(null) { Rounds = new List<RoundInfo> { round } };

            Assert.Equal("💥 Trafienia krytyczne: ???? 2\n", Waifu.CriticalHitsLine(fight, players));
            Assert.Equal("", Waifu.CriticalHitsLine(new FightHistory(null) { Rounds = new List<RoundInfo>() }, players));
        }
    }
}
