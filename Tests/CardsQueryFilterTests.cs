using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Sanakan.Api.Models;
using Sanakan.Database.Models;
using Xunit;

namespace Artifacts
{
    // the id tie-breaker follows the direction of the last sort key, so MySQL reads one index forwards or backwards
    public class CardsQueryFilterTests
    {
        public static IEnumerable<object[]> OrderTypes() => Enum.GetValues<OrderType>().Select(x => new object[] { x });

        [Theory]
        [MemberData(nameof(OrderTypes))]
        public void TieBreaker_HasTheDirectionOfTheLastKey(OrderType type)
        {
            var ordered = CardsQueryFilter.Use(type, new List<Card>().AsQueryable());

            var tieBreaker = Assert.IsAssignableFrom<MethodCallExpression>(ordered.Expression);
            var lastKey = Assert.IsAssignableFrom<MethodCallExpression>(tieBreaker.Arguments[0]);
            Assert.Equal(lastKey.Method.Name.EndsWith("Descending"), tieBreaker.Method.Name.EndsWith("Descending"));
        }

        [Fact]
        public void DescendingSort_KeepsEqualValuesInDescendingIdOrder()
        {
            var cards = new[] { 1ul, 2ul, 3ul }.Select(id => new Card { Id = id, CardPower = 10 })
                .Append(new Card { Id = 4, CardPower = 20 }).AsQueryable();

            Assert.Equal(new ulong[] { 4, 3, 2, 1 }, CardsQueryFilter.Use(OrderType.CardPowerDes, cards).Select(x => x.Id));
            Assert.Equal(new ulong[] { 1, 2, 3, 4 }, CardsQueryFilter.Use(OrderType.CardPower, cards).Select(x => x.Id));
        }
    }
}
