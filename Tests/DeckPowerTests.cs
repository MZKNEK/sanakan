using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services.PocketWaifu;
using Xunit;
using Xunit.Abstractions;

namespace Artifacts
{
    public class DeckPowerTests
    {
        private readonly ITestOutputHelper _output;
        private readonly Random _random = new Random(2027);
        private readonly Waifu _waifu = (Waifu)FormatterServices.GetUninitializedObject(typeof(Waifu));
        private ulong _nextId = 1;

        public DeckPowerTests(ITestOutputHelper output) => _output = output;

        private static readonly Dere[] _normalDeres = { Dere.Tsundere, Dere.Kamidere, Dere.Deredere, Dere.Yandere, Dere.Dandere, Dere.Kuudere, Dere.Mayadere, Dere.Bodere };

        private Card Ultimate(int health, int attack, int defence, Dere dere, double crit = 0) => new Card
        {
            Id = _nextId++,
            FromFigure = true,
            Rarity = Rarity.SSS,
            Quality = Quality.Omega,
            Health = health,
            Attack = attack,
            Defence = defence,
            Dere = dere,
            CritChance = crit,
            Image = "https://example.com/a.png",
            Tags = new List<Tag>(),
        };

        private Card Maxed(Dere dere, double crit = 0) => Ultimate(99999, 9999, 9999, dere, crit);

        // maxed attack, the power is reached with health
        private Card UltimateOfPower(Dere dere, double power)
        {
            int low = 300, high = 99999;
            while (high - low > 1)
            {
                var mid = (low + high) / 2;
                if (Ultimate(mid, 9999, 9000, dere).CalculateCardPower() < power) low = mid;
                else high = mid;
            }
            return Ultimate(high, 9999, 9000, dere);
        }

        private Dere RandomNormalDere() => _normalDeres[_random.Next(_normalDeres.Length)];

        [Fact]
        public void OneCard_KeepsItsPower_AndMoreCardsCountSlightlyLessThanTheirSum()
        {
            Assert.Equal(0, UserExtension.CombineCardPowers(new double[0]));
            Assert.Equal(1234.5, UserExtension.CombineCardPowers(new[] { 1234.5 }), 6);

            var eight = UserExtension.CombineCardPowers(Enumerable.Repeat(1000d, 8));
            Assert.InRange(eight / 8000, 0.94, 0.96);
        }

        [Fact]
        public void TheLimit_FitsTheFourStrongestCards_ButNotFive()
        {
            var strongest = Maxed(Dere.Yato, 40).CalculateCardPower();
            var normal = new Card { Rarity = Rarity.SSS, Health = 999, Attack = 999, Defence = 99, Dere = Dere.Yato, CritChance = 25, Image = "x", Tags = new List<Tag>() };

            var four = UserExtension.CombineCardPowers(Enumerable.Repeat(strongest, 4).Concat(Enumerable.Repeat(normal.CalculateCardPower(), 4)));
            var five = UserExtension.CombineCardPowers(Enumerable.Repeat(strongest, 5));

            Assert.True(four < UserExtension.MAX_DECK_POWER);
            Assert.True(five > UserExtension.MAX_DECK_POWER);
        }

        private double FewAgainstMany(int few, int many, int fights)
        {
            double wins = 0;
            for (int i = 0; i < fights; i++)
            {
                var strong = Enumerable.Range(0, few).Select(_ => Maxed(RandomNormalDere())).ToList();
                var each = UserExtension.CombineCardPowers(strong.Select(x => x.CalculateCardPower())) / Math.Pow(many, 1 / UserExtension.DeckPowerExponent);
                var weak = Enumerable.Range(0, many).Select(_ => UltimateOfPower(RandomNormalDere(), each)).ToList();
                strong.ForEach(x => x.GameDeckId = 1);
                weak.ForEach(x => x.GameDeckId = 2);

                var first = new PlayerInfo { Cards = strong };
                var fight = _waifu.MakeFightAsync(new List<PlayerInfo> { first, new PlayerInfo { Cards = weak } });
                wins += fight.Winner == null ? 0.5 : fight.Winner == first ? 1 : 0;
            }
            return wins / fights;
        }

        [Fact]
        public void FewStrongCards_AndMoreWeakerOnes_OfTheSameDeckPower_WinAboutEqually()
        {
            var fourVsEight = FewAgainstMany(4, 8, 300);
            var twoVsSix = FewAgainstMany(2, 6, 300);

            _output.WriteLine($"4 vs 8: {fourVsEight:P0}, 2 vs 6: {twoVsSix:P0}");
            Assert.InRange(fourVsEight, 0.35, 0.65);
            Assert.InRange(twoVsSix, 0.35, 0.65);
        }
    }
}
