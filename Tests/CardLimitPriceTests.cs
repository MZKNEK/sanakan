using Sanakan.Database.Models;
using Sanakan.Extensions;
using Xunit;

namespace Artifacts
{
    public class CardLimitPriceTests
    {
        [Fact]
        public void BulkPurchaseAcrossTwoTiers_HasCorrectPrice()
        {
            var deck = new GameDeck { MaxNumberOfCards = 1900 };
            Assert.Equal(6624, deck.CalculatePriceOfIncMaxCardCount(12));
            Assert.Equal(1900, deck.MaxNumberOfCards);
        }

        [Fact]
        public void EveryAllowedBulkSize_MatchesSinglePurchasesAcrossTiers()
        {
            for (long limit = 1000; limit <= 50000; limit += 100)
            {
                var bulkDeck = new GameDeck { MaxNumberOfCards = limit };
                var singleDeck = new GameDeck { MaxNumberOfCards = limit };
                long price = 0;
                for (var count = 1; count <= 20; count++)
                {
                    price += singleDeck.CalculatePriceOfIncMaxCardCount(1);
                    singleDeck.MaxNumberOfCards += 100;
                    Assert.Equal(price, bulkDeck.CalculatePriceOfIncMaxCardCount(count));
                }
            }
        }
    }
}
