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
    // runs the real fight engine against the real power formula, both with every dere ability in place
    public class DereFairnessTests
    {
        private readonly ITestOutputHelper _output;
        private readonly Random _random = new Random(2026);
        private readonly Waifu _waifu = (Waifu)FormatterServices.GetUninitializedObject(typeof(Waifu));
        private ulong _nextId = 1;

        public DereFairnessTests(ITestOutputHelper output) => _output = output;

        private static readonly (Rarity Rarity, int Weight, int AtkMin, int AtkMax, int DefMin, int DefMax, int HpMin)[] _rarities =
        {
            (Rarity.SSS, 50, 100, 130, 88, 96, 100), (Rarity.SS, 20, 90, 100, 77, 91, 90), (Rarity.S, 12, 80, 96, 68, 79, 80),
            (Rarity.A, 6, 65, 87, 60, 75, 70), (Rarity.B, 4, 50, 84, 50, 70, 60), (Rarity.C, 3, 32, 68, 32, 65, 50),
            (Rarity.D, 3, 20, 50, 15, 53, 40), (Rarity.E, 2, 1, 35, 1, 38, 30),
        };

        private Dere RandomDere()
        {
            // Yami, Raito and Yato come from items, so they are met four times less often
            var roll = _random.NextDouble() * 8.75;
            return roll < 8 ? (Dere)(int)roll : (Dere)(8 + (int)((roll - 8) / 0.25));
        }

        private int Between(int min, int max) => _random.Next(min, max + 1);

        private (int Attack, int Defence) Stars(int restarts)
        {
            var stars = Math.Min(restarts / 3, 6 * 61);
            return (Math.Min(restarts, 100) * 4 + Math.Max(0, restarts - 100) + Math.Min(stars, 50) * 20 + Math.Max(0, stars - 50) * 4,
                Math.Min(restarts, 100) * 2 + Math.Max(0, restarts - 100) / 2 + Math.Min(stars, 50) * 5 + Math.Max(0, stars - 50));
        }

        private Card NewCard(bool fromFigure, int health, int attack, int defence, double crit) => new Card
        {
            Id = _nextId++,
            FromFigure = fromFigure,
            Rarity = Rarity.SSS,
            Health = health,
            Attack = attack,
            Defence = defence,
            Dere = RandomDere(),
            CritChance = crit,
            Image = "https://example.com/a.png",
            Tags = new List<Tag>(),
        };

        private Card Normal()
        {
            var roll = _random.Next(100);
            var r = _rarities.First(x => (roll -= x.Weight) < 0);
            int atk = Between(r.AtkMin, r.AtkMax), def = Between(r.DefMin, r.DefMax);
            var hp = Between(r.HpMin, Math.Max(r.HpMin, 300 - atk - def));
            var restarts = r.Rarity != Rarity.SSS ? 0 : _random.Next(4) switch { 2 => Between(1, 30), 3 => Between(30, 150), _ => 0 };
            var (sa, sd) = Stars(restarts);
            var affection = _random.Next(3) switch { 0 => 0, 1 => _random.NextDouble() * 20, _ => 20 + _random.NextDouble() * 180 };
            hp = Math.Max(10, Math.Min(hp + Math.Min((int)(hp * affection * 0.05), 2000), 999));
            var crit = _random.NextDouble() < 0.85 ? 0 : _random.NextDouble() * 25;
            return NewCard(false, hp, Math.Min(atk + sa, 999), Math.Min(def + sd, 99), crit);
        }

        private Card Ultimate()
        {
            var level = _random.NextDouble();
            double Spread() => level * (0.3 + _random.NextDouble());
            var (sa, sd) = Stars(Between(0, 150));
            var atk = 130 + sa + (int)(Spread() * 5000);
            var def = 96 + sd + (int)(Spread() * 3000);
            var hp = 300 + (int)(Spread() * 25000) + Math.Min((int)(300 * _random.NextDouble() * 100 * 0.05), 2000);
            var crit = _random.NextDouble() < 0.85 ? 0 : _random.NextDouble() * 40;
            return NewCard(true, Math.Min(hp, 99999), Math.Min(atk, 9999), Math.Min(def, 9999), crit);
        }

        private double WinRate(Dere dere, Func<Card> generate, int duels)
        {
            var pool = Enumerable.Range(0, 4000).Select(_ => generate()).Select(x => (Card: x, Power: x.CalculateCardPower())).OrderBy(x => x.Power).ToList();
            double wins = 0;
            var fought = 0;
            while (fought < duels)
            {
                var card = generate();
                card.Dere = dere;
                var power = card.CalculateCardPower();
                var near = pool.Where(x => x.Power >= power * 0.95 && x.Power <= power * 1.05).ToList();
                if (near.Count == 0) continue;

                var enemy = near[_random.Next(near.Count)].Card;
                card.GameDeckId = 1;
                enemy.GameDeckId = 2;
                var mine = new PlayerInfo { Cards = new List<Card> { card } };
                var fight = _waifu.MakeFightAsync(new List<PlayerInfo> { mine, new PlayerInfo { Cards = new List<Card> { enemy } } });
                wins += fight.Winner == null ? 0.5 : fight.Winner == mine ? 1 : 0;
                fought++;
            }
            return wins / duels;
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void EveryDere_WinsAboutHalfAgainstEqualPower(bool ultimate)
        {
            var rates = Enum.GetValues(typeof(Dere)).Cast<Dere>()
                .ToDictionary(d => d, d => WinRate(d, ultimate ? Ultimate : Normal, 400));

            _output.WriteLine(string.Join(", ", rates.Select(x => $"{x.Key} {x.Value:P0}")));
            Assert.All(rates, x => Assert.InRange(x.Value, 0.38, 0.62));
        }

        [Fact]
        public void Bodere_StrikesAgainInAboutOneTurnInSeven()
        {
            int turns = 0, again = 0;
            for (int i = 0; i < 5; i++)
            {
                var bodere = NewCard(false, 999, 10, 99, 0);
                bodere.Dere = Dere.Bodere;
                bodere.GameDeckId = 1;
                var enemy = NewCard(false, 999, 10, 99, 0);
                enemy.Dere = Dere.Raito;
                enemy.GameDeckId = 2;
                var fight = _waifu.MakeFightAsync(new List<PlayerInfo>
                {
                    new PlayerInfo { Cards = new List<Card> { bodere } },
                    new PlayerInfo { Cards = new List<Card> { enemy } },
                });

                var attacks = fight.Rounds.SelectMany(x => x.Fights).Where(x => x.AtkCardId == bodere.Id).ToList();
                turns += attacks.Count(x => !x.SecondStrike);
                again += attacks.Count(x => x.SecondStrike);
            }

            Assert.InRange(again / (double)turns, 0.12, 0.18);
        }
    }
}
