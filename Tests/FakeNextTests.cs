using Sanakan.Database.Models;
using Sanakan.Extensions;
using Xunit;

namespace Artifacts
{
    // Blokuje "poprawione" zachowanie Fake/Next (saturacja do ostatniej wartości, bez wrap/throw).
    public class FakeNextTests
    {
        [Fact]
        public void Fake_SaturatesAtLastValue()
        {
            Assert.Equal(Quality.Omega, Quality.Jota.Fake(14));
            Assert.Equal(Quality.Omega, Quality.Omega.Fake(100));
            Assert.Equal(Quality.Omega, Quality.Sigma.Fake(1));
        }

        [Fact]
        public void Fake_StepsThroughEnumEntries()
        {
            Assert.Equal(Quality.Broken, Quality.Broken.Fake(0));
            Assert.Equal(Quality.Jota, Quality.Jota.Fake(0));
            Assert.Equal(Quality.Lambda, Quality.Jota.Fake(1));
            Assert.Equal(Quality.Sigma, Quality.Jota.Fake(2));
            Assert.Equal(Quality.Sigma, Quality.Lambda.Fake(1));
        }

        [Fact]
        public void Next_SaturatesAtLastValue()
        {
            Assert.Equal(Quality.Omega, Quality.Omega.Next());
            Assert.Equal(Quality.Lambda, Quality.Jota.Next());
        }

        [Fact]
        public void Fake_NegativeOverflow_ReturnsInput()
        {
            Assert.Equal(Quality.Lambda, Quality.Lambda.Fake(-5));
        }
    }
}
