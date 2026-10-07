#pragma warning disable 1591

using Sanakan.Database.Models;
using Sanakan.Extensions;
using Xunit;

namespace Artifacts
{
    public class EnumExtensionTests
    {
        [Fact]
        public void Fake_BelowOne_ReturnsSameValue() => Assert.Equal(Quality.Lambda, Quality.Lambda.Fake(0));

        [Fact]
        public void Fake_AdvancesByOne()
            => Assert.Equal(Quality.Beta, Quality.Alpha.Fake(1));

        [Fact]
        public void Fake_UsesDeclarationOrder_AcrossValueGaps()
        {
            // Quality: ... Jota=9, Lambda=11, Sigma=18, Omega=24 (nieciagly)
            Assert.Equal(Quality.Lambda, Quality.Jota.Fake(1));
            Assert.Equal(Quality.Sigma, Quality.Jota.Fake(2));
            Assert.Equal(Quality.Sigma, Quality.Lambda.Fake(1));
            Assert.Equal(Quality.Omega, Quality.Lambda.Fake(2));
        }

        [Fact]
        public void Fake_TooLarge_SaturatesAtLastInsteadOfThrowing()
            => Assert.Equal(Quality.Omega, Quality.Alpha.Fake(1000));

        [Fact]
        public void Next_AdvancesByOne()
            => Assert.Equal(Quality.Lambda, Quality.Jota.Next());

        [Fact]
        public void Next_OnLast_SaturatesInsteadOfWrapping()
        {
            Assert.Equal(Quality.Omega, Quality.Omega.Next());
            Assert.NotEqual(Quality.Broken, Quality.Omega.Next());
        }
    }
}
