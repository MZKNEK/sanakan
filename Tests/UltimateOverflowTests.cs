using System.Collections.Generic;
using System.Linq;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Xunit;

namespace Artifacts
{
    public class UltimateOverflowTests
    {
        private static Card Ultimate(Quality quality, int overflow = 0) => new Card
        {
            FromFigure = true,
            Rarity = Rarity.SSS,
            Quality = quality,
            BorderOverflow = overflow,
            BorderVariant = 3,
            Tags = new List<Tag>(),
        };

        [Theory]
        [InlineData(Quality.Alpha, 1, 2, Quality.Beta)]
        [InlineData(Quality.Jota, 1, 10, Quality.Jota)]
        [InlineData(Quality.Jota, 2, 11, Quality.Lambda)]
        [InlineData(Quality.Lambda, 6, 17, Quality.Lambda)]
        [InlineData(Quality.Lambda, 7, 18, Quality.Sigma)]
        [InlineData(Quality.Sigma, 6, 24, Quality.Omega)]
        public void Overflow_AddsOneToTheValue_AndPlaysAsTheReachedQuality(Quality quality, int overflow, int value, Quality plays)
        {
            var card = Ultimate(quality, overflow);

            Assert.Equal(value, card.GetUltimateValue());
            Assert.Equal(plays, card.GetEffectiveQuality());
        }

        [Theory]
        [InlineData(Quality.Alpha, 7, Quality.Theta)]
        [InlineData(Quality.Beta, 1, Quality.Gamma)]
        [InlineData(Quality.Delta, 1, Quality.Epsilon)]
        [InlineData(Quality.Delta, 2, Quality.Zeta)]
        public void CardsWithOverflowInProduction_KeepTheirQuality(Quality quality, int overflow, Quality plays)
            => Assert.Equal(plays, Ultimate(quality, overflow).GetEffectiveQuality());

        [Fact]
        public void Upgrade_NeedsTheQualityTheCardPlaysAs()
        {
            var card = Ultimate(Quality.Alpha, 1);

            Assert.Contains("potrzebna: β", card.GetUltimateUpgradeBlockReason(Quality.Alpha, true));
            Assert.Null(card.GetUltimateUpgradeBlockReason(Quality.Beta, true));
            Assert.NotNull(card.GetUltimateUpgradeBlockReason(Quality.Gamma, false));
        }

        [Fact]
        public void InsideAGap_TheLowerQualityIsStillRequired()
        {
            var card = Ultimate(Quality.Jota, 1);

            Assert.Null(card.GetUltimateUpgradeBlockReason(Quality.Jota, true));
            Assert.NotNull(card.GetUltimateUpgradeBlockReason(Quality.Lambda, true));
        }

        [Fact]
        public void Omega_CannotBeUpgraded()
        {
            Assert.NotNull(Ultimate(Quality.Omega).GetUltimateUpgradeBlockReason(Quality.Omega, true));
            Assert.NotNull(Ultimate(Quality.Sigma, 6).GetUltimateUpgradeBlockReason(Quality.Omega, true));
            Assert.Null(Ultimate(Quality.Sigma, 5).GetUltimateUpgradeBlockReason(Quality.Sigma, true));
        }

        [Fact]
        public void Overflow_KeepsTheBorder()
        {
            var card = Ultimate(Quality.Alpha);

            card.ApplyUltimateUpgrade(true);

            Assert.Equal(Quality.Alpha, card.Quality);
            Assert.Equal(1, card.BorderOverflow);
            Assert.Equal(3, card.BorderVariant);
        }

        [Theory]
        [InlineData(Quality.Alpha, 1, Quality.Gamma)]
        [InlineData(Quality.Jota, 1, Quality.Lambda)]
        [InlineData(Quality.Lambda, 3, Quality.Sigma)]
        [InlineData(Quality.Sigma, 5, Quality.Omega)]
        public void NormalUpgrade_GoesToTheNextQualityAndResetsTheOverflow(Quality quality, int overflow, Quality after)
        {
            var card = Ultimate(quality, overflow);

            card.ApplyUltimateUpgrade(false);

            Assert.Equal(after, card.Quality);
            Assert.Equal(0, card.BorderOverflow);
            Assert.Equal(0, card.BorderVariant);
        }

        [Fact]
        public void OverflowFromAlphaToOmega_TakesAFigureForEveryPoint()
        {
            var card = Ultimate(Quality.Alpha);
            var used = new List<Quality>();
            while (card.GetEffectiveQuality() != Quality.Omega)
            {
                var needed = card.GetEffectiveQuality();
                Assert.Null(card.GetUltimateUpgradeBlockReason(needed, true));
                used.Add(needed);
                card.ApplyUltimateUpgrade(true);
            }

            Assert.Equal(23, used.Count);
            Assert.Equal(2, used.Count(x => x == Quality.Jota));
            Assert.Equal(7, used.Count(x => x == Quality.Lambda));
            Assert.Equal(6, used.Count(x => x == Quality.Sigma));
        }

        [Fact]
        public void NumericLimits_FollowTheValue()
        {
            var card = Ultimate(Quality.Lambda, 3);

            Assert.Equal(1000 + 50 * 14, card.ExpToUpgrade());
            Assert.True(card.CanUpgradePower(4900 * 14).IsOk());
            Assert.False(card.CanUpgradePower(4900 * 14 + 1).IsOk());
        }

        [Fact]
        public void QualityTables_FollowThePlayedQuality()
        {
            Assert.Equal(6, Ultimate(Quality.Jota, 1).GetMarketBonusFromUltimate());
            Assert.Equal(7, Ultimate(Quality.Lambda, 7).GetMarketBonusFromUltimate());
            Assert.Equal(8, Ultimate(Quality.Sigma, 6).GetMarketBonusFromUltimate());
        }

        [Fact]
        public void FreeCardCooldown_CountsThePlayedQuality()
        {
            var deck = new GameDeck { Cards = new List<Card> { Ultimate(Quality.Alpha, 2) } };

            Assert.Equal(1 + 1, deck.GetFreeCardCooldownReductionHours());
        }
    }
}
