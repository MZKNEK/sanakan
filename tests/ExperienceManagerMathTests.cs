#pragma warning disable 1591

using Sanakan.Services;
using Xunit;

namespace Artifacts
{
    public class ExperienceManagerMathTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(-1000)]
        public void ExpForLevel_NonPositiveLevel_IsZero(long level)
            => Assert.Equal(0, ExperienceManager.CalculateExpForLevel(level));

        [Theory]
        [InlineData(1, 9)]
        [InlineData(2, 33)]
        [InlineData(10, 817)]
        public void ExpForLevel_KnownValues(long level, long expected)
            => Assert.Equal(expected, ExperienceManager.CalculateExpForLevel(level));

        [Theory]
        [InlineData(0, 0)]
        [InlineData(9, 1)]
        [InlineData(33, 2)]
        [InlineData(817, 10)]
        public void LevelForExp_KnownValues(long exp, long expected)
            => Assert.Equal(expected, ExperienceManager.CalculateLevel(exp));

        [Fact]
        public void ExpForLevel_IsStrictlyIncreasing()
        {
            for (long level = 1; level <= 200; level++)
                Assert.True(ExperienceManager.CalculateExpForLevel(level + 1) > ExperienceManager.CalculateExpForLevel(level),
                    $"level {level}");
        }

        [Fact]
        public void LevelForExp_IsNonDecreasing()
        {
            long previous = ExperienceManager.CalculateLevel(0);
            for (long exp = 1; exp <= 50000; exp++)
            {
                var current = ExperienceManager.CalculateLevel(exp);
                Assert.True(current >= previous, $"exp {exp}");
                previous = current;
            }
        }

        [Fact]
        public void LevelForExp_RoundTripsForCalculatedExp()
        {
            for (long level = 1; level <= 100; level++)
            {
                var exp = ExperienceManager.CalculateExpForLevel(level);
                Assert.Equal(level, ExperienceManager.CalculateLevel(exp));
            }
        }
    }
}
