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

        [Theory]
        [InlineData(FightResult.Draw)]
        [InlineData(FightResult.Win)]
        [InlineData(FightResult.Lose)]
        public void Mmr_IsZeroSum_AndTheOpponentRankMovesHalf(FightResult result)
        {
            var d1 = Deck();
            var d2 = Deck();

            d1.CalculatePVPParams(d2, result);

            Assert.Equal(2000, d1.MatachMakingRatio + d2.MatachMakingRatio, 6);
            Assert.Equal((d1.GlobalPVPRank - 500) / 2, 500 - d2.GlobalPVPRank);
            Assert.Equal((d1.SeasonalPVPRank - 500) / 2, 500 - d2.SeasonalPVPRank);
        }

        private static GameDeck Powered(double power) => new GameDeck { DeckPower = power };

        [Theory]
        [InlineData(1000, 1000, FightResult.Win, 100)]
        [InlineData(1000, 1000, FightResult.Lose, 50)]
        [InlineData(1000, 1000, FightResult.Draw, 50)]
        [InlineData(1000, 5000, FightResult.Win, 140)]
        [InlineData(5000, 1000, FightResult.Win, 60)]
        public void Coins_GrowWithTheOpponentsStrength(double mine, double enemy, FightResult result, long expected)
            => Assert.Equal(expected, Powered(mine).GetPVPCoinsFromDuel(Powered(enemy), result));

        [Theory]
        [InlineData(0, "Miles gregarius")]
        [InlineData(52, "Signifer")]
        [InlineData(3323, "Primus pilus")]
        [InlineData(14999, "Praetor")]
        [InlineData(15001, "Konsul")]
        public void SeasonalRanks_FitAMonth(long points, string expected)
            => Assert.Equal(expected, new GameDeck { SeasonalPVPRank = points }.GetRankName());

        [Fact]
        public void GlobalRanks_KeepTheLongThresholds()
        {
            Assert.Equal("Miles gregarius", new GameDeck { GlobalPVPRank = 493 }.GetGlobalRankName());
            Assert.Equal("Konsul", new GameDeck { GlobalPVPRank = 142717 }.GetGlobalRankName());
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
