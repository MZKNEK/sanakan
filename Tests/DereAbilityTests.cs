using System.Collections.Generic;
using System.Linq;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services.PocketWaifu;
using Sanakan.Services.PocketWaifu.Fight;
using Xunit;

namespace Artifacts
{
    public class DereAbilityTests
    {
        private static ulong _nextId = 1;

        private static CardWithHealth Fighter(Dere dere, double health = 1000, int attack = 100, int defence = 50, double maxHealth = 1000) => new CardWithHealth
        {
            Card = new Card
            {
                Id = _nextId++,
                Dere = dere,
                Rarity = Rarity.SSS,
                Health = 1000,
                Attack = attack,
                Defence = defence,
                Image = "https://example.com/a.png",
                Tags = new List<Tag>(),
            },
            Health = health,
            MaxHealth = maxHealth,
        };

        private static double Hit(CardWithHealth attacker, CardWithHealth target, bool counter = false)
        {
            var before = target.Health;
            Waifu.Strike(new RoundInfo(), attacker, target, counter);
            return before - target.Health;
        }

        [Theory]
        [InlineData(Dere.Deredere, Dere.Kamidere, 1)]
        [InlineData(Dere.Yami, Dere.Kamidere, 1)]
        [InlineData(Dere.Yato, Dere.Kamidere, 1)]
        [InlineData(Dere.Yandere, Dere.Kamidere, 0.5)]
        [InlineData(Dere.Mayadere, Dere.Dandere, 1)]
        [InlineData(Dere.Mayadere, Dere.Yato, 1)]
        [InlineData(Dere.Mayadere, Dere.Tsundere, 2)]
        [InlineData(Dere.Yato, Dere.Deredere, 1.75)]
        public void Kamidere_TakesNoExtra_AndMayadere_DealsNoLess(Dere attacker, Dere defender, double expected)
            => Assert.Equal(expected, Waifu.GetDereDmgMultiplier(attacker, defender));

        [Fact]
        public void Kuudere_HasExtraPrecision_CountedInPower()
        {
            var kuudere = Fighter(Dere.Kuudere).Card;
            kuudere.CritChance = 5;

            Assert.Equal(25, kuudere.GetCritChance());
            Assert.Equal(5, kuudere.CritChance);
            var without = kuudere.CalculateCardPower();
            kuudere.CritChance = 0;
            Assert.True(kuudere.CalculateCardPower() < without);
        }

        [Fact]
        public void Yandere_HitsHarderBelowAQuarterOfHealth()
        {
            var target = Fighter(Dere.Yami, 100000, maxHealth: 100000);

            Assert.Equal(50, Hit(Fighter(Dere.Yandere, 250), target), 3);
            Assert.Equal(75, Hit(Fighter(Dere.Yandere, 249), target), 3);
        }

        [Fact]
        public void Tsundere_TakesLessBelowHalfHealth()
        {
            var attacker = Fighter(Dere.Kamidere);

            Assert.Equal(100, Hit(attacker, Fighter(Dere.Tsundere, 500)), 3);
            Assert.Equal(75, Hit(attacker, Fighter(Dere.Tsundere, 499)), 3);
        }

        [Fact]
        public void Deredere_HealsOnceBelowThirtyPercent()
        {
            var attacker = Fighter(Dere.Bodere);
            var deredere = Fighter(Dere.Deredere, 340);

            Hit(attacker, deredere);
            Assert.Equal(290 + 250, deredere.Health, 3);
            Assert.True(deredere.Healed);

            deredere.Health = 280;
            Hit(attacker, deredere);
            Assert.Equal(230, deredere.Health, 3);
        }

        [Fact]
        public void Deredere_DoesNotRiseFromTheDead()
        {
            var deredere = Fighter(Dere.Deredere, 40);

            Hit(Fighter(Dere.Bodere), deredere);

            Assert.Equal(0, deredere.Health);
            Assert.False(deredere.Healed);
        }

        [Fact]
        public void Dandere_DodgesAboutOneInTen_ButNotCounters()
        {
            var attacker = Fighter(Dere.Bodere);
            var round = new RoundInfo();
            for (int i = 0; i < 20000; i++)
                Waifu.Strike(round, attacker, Fighter(Dere.Dandere), false);

            var rate = round.Fights.Count(x => x.Dodged) / 20000d;
            Assert.InRange(rate, 0.09, 0.11);

            var counters = new RoundInfo();
            for (int i = 0; i < 2000; i++)
                Waifu.Strike(counters, attacker, Fighter(Dere.Dandere), true);
            Assert.DoesNotContain(counters.Fights, x => x.Dodged);
        }

        [Fact]
        public void AbilitiesLine_CountsEventsPerPlayer()
        {
            var a = Fighter(Dere.Dandere).Card;
            var b = Fighter(Dere.Bodere).Card;
            var fight = new FightHistory(null);
            fight.Rounds.Add(new RoundInfo
            {
                Fights = new List<AttackInfo>
                {
                    new AttackInfo { AtkCardId = b.Id, DefCardId = a.Id, Dodged = true },
                    new AttackInfo { AtkCardId = b.Id, DefCardId = a.Id, Counter = true },
                    new AttackInfo { AtkCardId = a.Id, DefCardId = b.Id, Healed = true },
                }
            });
            var players = new List<PlayerInfo>
            {
                new PlayerInfo { Cards = new List<Card> { a } },
                new PlayerInfo { Cards = new List<Card> { b } },
            };

            Assert.Equal("✨ Zdolności: ???? uniki 1 · ???? kontry 1, leczenie 1\n", Waifu.AbilitiesLine(fight, players));
            Assert.Equal("", Waifu.AbilitiesLine(new FightHistory(null), players));
        }

        [Fact]
        public void EveryNormalDere_HasADescribedAbility()
        {
            foreach (var dere in new[] { Dere.Tsundere, Dere.Kamidere, Dere.Deredere, Dere.Yandere, Dere.Dandere, Dere.Kuudere, Dere.Mayadere, Dere.Bodere })
                Assert.False(string.IsNullOrEmpty(dere.GetAbilityDescription()));

            Assert.Null(Dere.Yato.GetAbilityDescription());
        }
    }
}
