using System.Collections.Generic;
using System.Linq;
using Sanakan.Api.Models;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services.PocketWaifu;
using Xunit;

namespace Artifacts
{
    public class PvpParamsTests
    {
        private static GameDeck Deck() => new GameDeck
        {
            MatachMakingRatio = 1000,
            GlobalPVPRank = 500,
            SeasonalPVPRank = 500,
        };

        [Fact]
        public void Draw_IsZeroSumForMmrAndRank()
        {
            var d1 = Deck();
            var d2 = Deck();

            d1.CalculatePVPParams(d2, FightResult.Draw);

            Assert.Equal(2000, d1.MatachMakingRatio + d2.MatachMakingRatio, 6);
            Assert.Equal(1000, d1.GlobalPVPRank + d2.GlobalPVPRank);
            Assert.Equal(1000, d1.SeasonalPVPRank + d2.SeasonalPVPRank);
        }

        [Fact]
        public void Win_IsZeroSumForMmrAndRank()
        {
            var d1 = Deck();
            var d2 = Deck();

            d1.CalculatePVPParams(d2, FightResult.Win);

            Assert.Equal(2000, d1.MatachMakingRatio + d2.MatachMakingRatio, 6);
            Assert.Equal(1000, d1.GlobalPVPRank + d2.GlobalPVPRank);
            Assert.Equal(1000, d1.SeasonalPVPRank + d2.SeasonalPVPRank);
        }
    }

    public class BoosterPackDefaultTests
    {
        [Fact]
        public void Rarity_DefaultsToE()
        {
            Assert.Equal(Rarity.E, new CardBoosterPack().Rarity);
            Assert.Equal(Rarity.E, new CardBoosterPack { Count = 1 }.ToRealPack().MinRarity);
        }
    }

    public class ExpeditionAffectionGateTests
    {
        private static Expedition Exp() => new Expedition(new FixedTime());

        private static User User() => new User { GameDeck = new GameDeck { Karma = 100 } };

        [Fact]
        public void TargetExpedition_IsCounted_NotOnlyCurrentOne()
        {
            // bez poprawki koszt liczony z card.Expedition (None) = 0 -> wynik zawsze 10080
            var time = Exp().GetMaxPossibleLengthOfExpedition(User(), new Card { Affection = 50 }, CardExpedition.LightExp);

            Assert.True(time < 10080, $"oczekiwano skończonego czasu, było {time}");
            Assert.True(time > 0);
        }

        [Fact]
        public void LowAffection_IsBlockedByGate()
        {
            var time = Exp().GetMaxPossibleLengthOfExpedition(User(), new Card { Affection = -5.5 }, CardExpedition.LightExp);

            Assert.True(time <= 2, $"niska relacja powinna blokować, było {time}");
        }
    }
}
