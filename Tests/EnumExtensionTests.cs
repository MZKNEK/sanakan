#pragma warning disable 1591

using Sanakan.Database.Models;
using Sanakan.Extensions;
using Xunit;

namespace Artifacts
{
    public class EnumExtensionTests
    {
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
