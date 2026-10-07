using System.Collections.Generic;
using System.Linq;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Xunit;

namespace Artifacts
{
    public class DereTransformGuardTests
    {
        [Theory]
        [InlineData(Dere.Yami, true)]
        [InlineData(Dere.Yato, true)]
        [InlineData(Dere.Raito, false)]
        [InlineData(Dere.Tsundere, false)]
        [InlineData(Dere.Kamidere, false)]
        [InlineData(Dere.Deredere, false)]
        public void BlocksDemonTransform_OnlyBlocksYamiAndYato(Dere dere, bool expected)
        {
            Assert.Equal(expected, dere.BlocksDemonTransform());
        }

        [Theory]
        [InlineData(Dere.Raito, true)]
        [InlineData(Dere.Yato, true)]
        [InlineData(Dere.Yami, false)]
        [InlineData(Dere.Tsundere, false)]
        [InlineData(Dere.Kamidere, false)]
        [InlineData(Dere.Deredere, false)]
        public void BlocksAngelTransform_OnlyBlocksRaitoAndYato(Dere dere, bool expected)
        {
            Assert.Equal(expected, dere.BlocksAngelTransform());
        }

        [Fact]
        public void BothTransforms_AllowNormalCards()
        {
            Assert.False(Dere.Deredere.BlocksDemonTransform());
            Assert.False(Dere.Deredere.BlocksAngelTransform());
        }
    }

    public class RecalculateDeckTests
    {
        private static Card Card(bool active) => new Card
        {
            Health = 100,
            Attack = 100,
            Defence = 100,
            Active = active,
        };

        [Fact]
        public void RecalculateDeck_CountsOnlyActiveCards()
        {
            var deck = new GameDeck
            {
                Cards = new List<Card> { Card(true), Card(true), Card(false) },
            };

            deck.RecalculateDeck();

            Assert.Equal(2, deck.CardsInDeck);
            Assert.True(deck.DeckPower > 0);
        }

        [Fact]
        public void RecalculateDeck_PowerMatchesActiveCards()
        {
            var deck = new GameDeck
            {
                Cards = new List<Card> { Card(true), Card(false), Card(true) },
            };

            deck.RecalculateDeck();

            var expected = deck.Cards.Where(x => x.Active).Sum(x => x.CalculateCardPower());
            Assert.Equal(expected, deck.DeckPower, 3);
        }

        [Fact]
        public void RecalculateDeck_EmptyDeck_IsZero()
        {
            var deck = new GameDeck { Cards = new List<Card>() };

            deck.RecalculateDeck();

            Assert.Equal(0, deck.CardsInDeck);
            Assert.Equal(0, deck.DeckPower);
        }

        [Fact]
        public void RecalculateDeck_NullCards_DoesNotThrow()
        {
            var deck = new GameDeck { Cards = null };
            deck.RecalculateDeck();
            Assert.Equal(0, deck.CardsInDeck);
        }
    }
}
