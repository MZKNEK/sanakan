#pragma warning disable 1591

using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services;
using Sanakan.Services.PocketWaifu;
using Xunit;

namespace Artifacts
{
    public class ExpeditionChancesTests
    {
        private static Expedition New() => new Expedition(new FixedTime());

        [Theory]
        [InlineData(CardExpedition.DarkExp)]
        [InlineData(CardExpedition.LightExp)]
        public void ExpOnlyExpedition_HasNoItemChances_AndDoesNotThrow(CardExpedition expedition)
            => Assert.Empty(New().GetChancesFromExpedition(expedition));

        [Fact]
        public void ItemExpedition_ReturnsChances()
            => Assert.NotEmpty(New().GetChancesFromExpedition(CardExpedition.NormalItemWithExp));
    }

    public class TopViewTests
    {
        [Fact]
        public void PostsMonthlyCharacter_NoMessagesThisMonth_ReturnsZero()
        {
            var user = new User { MessagesCnt = 5, MessagesCntAtDate = 5, CharacterCntFromDate = 10 };
            Assert.Equal("0", user.GetViewValueForTop(TopType.PostsMonthlyCharacter));
        }

        [Fact]
        public void PostsMonthlyCharacter_WithMessages_DividesCharactersByMessages()
        {
            var user = new User { MessagesCnt = 10, MessagesCntAtDate = 4, CharacterCntFromDate = 12 };
            Assert.Equal("2", user.GetViewValueForTop(TopType.PostsMonthlyCharacter));
        }
    }

    public class PayTests
    {
        [Fact]
        public void Pay_Pc_RequiresCostTimesCount()
        {
            var user = new User { GameDeck = new GameDeck { PVPCoins = 30 } };
            var cost = new CurrencyCost(20, CurrencyType.PC);

            // 20 * 2 = 40 > 30 -> odmowa i brak zmiany salda
            Assert.False(user.Pay(cost, 2));
            Assert.Equal(30, user.GameDeck.PVPCoins);

            // 20 <= 30 -> pobiera 20
            Assert.True(user.Pay(cost, 1));
            Assert.Equal(10, user.GameDeck.PVPCoins);
        }

        [Fact]
        public void Pay_Pc_ExactBalance_Passes()
        {
            var user = new User { GameDeck = new GameDeck { PVPCoins = 40 } };
            var cost = new CurrencyCost(20, CurrencyType.PC);

            Assert.True(user.Pay(cost, 2));
            Assert.Equal(0, user.GameDeck.PVPCoins);
        }
    }
}
