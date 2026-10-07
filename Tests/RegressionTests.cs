#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.Linq;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Preconditions;
using Sanakan.Services;
using Sanakan.Services.PocketWaifu;
using Xunit;

namespace Artifacts
{
    // Regresja dla Batch 5 (5.1-5.4, 5.6, 5.7)
    public class RegressionTests
    {
        // 5.1 RandomizeHealth
        [Fact]
        public void RandomizeHealth_MinEqualsMax_DoesNotThrow_AndMeetsMinimum()
        {
            var card = new Card { Rarity = Rarity.SSS, Attack = 130, Defence = 71 };
            Assert.Equal(100, Waifu.RandomizeHealth(card));
        }

        [Fact]
        public void RandomizeHealth_NeverBelowRarityMinimum()
        {
            var card = new Card { Rarity = Rarity.SSS, Attack = 130, Defence = 80 };
            for (int i = 0; i < 50; i++)
                Assert.True(Waifu.RandomizeHealth(card) >= card.Rarity.GetHealthMin());
        }

        // 5.2 GetOneRandomFrom
        [Fact]
        public void GetOneRandomFrom_EmptyArray_ReturnsDefault()
            => Assert.Equal(0, Fun.GetOneRandomFrom(Array.Empty<int>()));

        [Fact]
        public void GetOneRandomFrom_EmptyList_ReturnsDefault()
            => Assert.Equal(0, Fun.GetOneRandomFrom(new List<int>()));

        // 5.3 DelayNextUseBy (Global vs PerUser)
        [Fact]
        public void DelayNextUseBy_Global_UsesSharedKey()
            => Assert.Equal(("c", 1UL), DelayNextUseBy.BuildKey("c", DelayNextUseBy.DelayMethod.Global, 999));

        [Fact]
        public void DelayNextUseBy_PerUser_UsesUserKey()
            => Assert.Equal(("c", 999UL), DelayNextUseBy.BuildKey("c", DelayNextUseBy.DelayMethod.PerUser, 999));

        // 5.4 Shinden.GetSearchResponse
        [Fact]
        public void GetSearchResponse_ManyPages_DoesNotThrow()
        {
            var shinden = new Sanakan.Services.Shinden(null, null, null, new ListLogger());
            var items = Enumerable.Range(0, 300).Select(_ => (object)new string('x', 200)).ToList();

            var pages = shinden.GetSearchResponse(items, "title");

            Assert.True(pages.Length > 10);
            Assert.Contains("```", pages.Last());
        }

        // 5.7 StringExtensions
        [Fact]
        public void TrimToLength_Zero_DoesNotThrow_ReturnsEmpty()
            => Assert.Equal("", "abcdef".TrimToLength(0));

        [Fact]
        public void TrimToLength_Negative_DoesNotThrow_ReturnsEmpty()
            => Assert.Equal("", "abcdef".TrimToLength(-1));

        [Fact]
        public void TrimToLength_Long_TruncatesToExactLength()
        {
            var result = new string('a', 50).TrimToLength(10);
            Assert.Equal(10, result.Length);
            Assert.EndsWith("...", result);
        }

        [Fact]
        public void IsCommand_MetacharPrefix_IsEscaped()
            => Assert.False("ssss".IsCommand("s*"));

        [Fact]
        public void IsCommand_InvalidRegexPrefix_DoesNotThrow()
            => Assert.False("x".IsCommand("s["));

        [Fact]
        public void IsCommand_NormalPrefix_Matches()
        {
            Assert.True("s.ping".IsCommand("s."));
            Assert.False("xp.ing".IsCommand("s."));
        }

        // 6.1 Spawn: dzienny reset liczony z czasu
        [Fact]
        public void SpawnReset_FirstCall_Resets()
            => Assert.True(Spawn.ShouldResetServerCounter(null, new DateTime(2026, 1, 1)));

        [Fact]
        public void SpawnReset_BeforeNextReset_DoesNotReset()
            => Assert.False(Spawn.ShouldResetServerCounter(new DateTime(2026, 1, 2), new DateTime(2026, 1, 1)));

        [Fact]
        public void SpawnReset_AtOrAfterReset_Resets()
            => Assert.True(Spawn.ShouldResetServerCounter(new DateTime(2026, 1, 1), new DateTime(2026, 1, 1)));
    }
}
