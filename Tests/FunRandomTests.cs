#pragma warning disable 1591

using System.Collections.Generic;
using Sanakan.Services;
using Xunit;

namespace Artifacts
{
    public class FunRandomTests
    {
        [Fact]
        public void GetRandomValue_StaysWithinBounds()
        {
            for (int i = 0; i < 2000; i++)
                Assert.InRange(Fun.GetRandomValue(5, 10), 5, 9);
        }

        [Fact]
        public void GetRandomValue_MaxOnly_StartsAtZero()
        {
            for (int i = 0; i < 2000; i++)
                Assert.InRange(Fun.GetRandomValue(7), 0, 6);
        }

        [Fact]
        public void TakeATry_Zero_IsAlwaysFalse()
        {
            for (int i = 0; i < 200; i++)
                Assert.False(Fun.TakeATry(0));
        }

        [Fact]
        public void TakeATry_Hundred_IsAlwaysTrue()
        {
            for (int i = 0; i < 200; i++)
                Assert.True(Fun.TakeATry(100));
        }

        [Fact]
        public void GetOneRandomFrom_SingleElement_ReturnsThatElement()
        {
            Assert.Equal(42, Fun.GetOneRandomFrom(new List<int> { 42 }));
            Assert.Equal("only", Fun.GetOneRandomFrom(new[] { "only" }));
        }

        [Fact]
        public void GetAFKC_IsWithinRangeAndStablePerCharacter()
        {
            const ulong id = 987654321012345678;
            var first = Fun.GetAFKC(id);

            Assert.InRange(first, 0, 155);
            Assert.Equal(first, Fun.GetAFKC(id));
        }
    }
}
