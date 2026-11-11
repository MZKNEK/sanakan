using System;
using System.Collections.Generic;
using System.Linq;
using Sanakan.Api.Models;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Xunit;

namespace Artifacts
{
    // the card lists sort attack, defence and health by the numbers the card shows, not by an approximation
    public class StatSortTests
    {
        private static List<Card> RandomCards(int count)
        {
            var random = new Random(1234);
            return Enumerable.Range(1, count).Select(i => new Card
            {
                Id = (ulong)i,
                Attack = random.Next(0, 1000),
                Defence = random.Next(0, 100),
                Health = random.Next(1, 1000),
                AttackBonus = random.Next(0, 4) == 0 ? random.Next(0, 6000) : 0,
                DefenceBonus = random.Next(0, 4) == 0 ? random.Next(0, 6000) : 0,
                HealthBonus = random.Next(0, 4) == 0 ? random.Next(0, 60000) : 0,
                RestartCnt = random.Next(0, 3) switch { 0 => 0, 1 => random.Next(0, 120), _ => random.Next(0, 1500) },
                Affection = random.NextDouble() * 200 - 60,
                FromFigure = random.Next(0, 3) == 0,
                Curse = random.Next(0, 6) == 0 ? CardCurse.LoweredStats : CardCurse.None,
            }).ToList();
        }

        [Fact]
        public void SortValues_EqualTheShownStats()
        {
            var cards = RandomCards(5000);
            var attack = CardsQueryFilter.AttackShown.Compile();
            var defence = CardsQueryFilter.DefenceShown.Compile();
            var health = CardsQueryFilter.HealthShown.Compile();

            Assert.All(cards, x =>
            {
                Assert.Equal(x.GetAttackWithBonus(), attack(x));
                Assert.Equal(x.GetDefenceWithBonus(), defence(x));
                Assert.Equal(x.GetHealthWithPenalty(), health(x));
            });
        }

        [Theory]
        [InlineData(OrderType.Atack)]
        [InlineData(OrderType.AtackDes)]
        [InlineData(OrderType.Defence)]
        [InlineData(OrderType.DefenceDes)]
        [InlineData(OrderType.Health)]
        [InlineData(OrderType.HealthDes)]
        public void StatSorts_FollowTheShownStats(OrderType type)
        {
            var sorted = CardsQueryFilter.Use(type, RandomCards(2000).AsQueryable()).ToList();

            Func<Card, int> shown = type switch
            {
                OrderType.Atack or OrderType.AtackDes => x => x.GetAttackWithBonus(),
                OrderType.Defence or OrderType.DefenceDes => x => x.GetDefenceWithBonus(),
                _ => x => x.GetHealthWithPenalty(),
            };
            var values = sorted.Select(shown).ToList();
            var expected = type.ToString().EndsWith("Des") ? values.OrderByDescending(x => x) : values.OrderBy(x => x);
            Assert.Equal(expected, values);
        }
    }
}
