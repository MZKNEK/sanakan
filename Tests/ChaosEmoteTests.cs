using System.Collections.Generic;
using Sanakan.Services;
using Xunit;

namespace Artifacts
{
    public class ChaosEmoteTests
    {
        private static readonly HashSet<ulong> BotRoles = new HashSet<ulong> { 5 };

        [Fact]
        public void PlainEmote_IsUsable()
            => Assert.True(Chaos.CanUse(true, new List<ulong>(), BotRoles));

        [Fact]
        public void UnknownAvailability_IsUsable()
            => Assert.True(Chaos.CanUse(null, null, BotRoles));

        [Fact]
        public void UnavailableEmote_IsSkipped()
            => Assert.False(Chaos.CanUse(false, new List<ulong>(), BotRoles));

        [Fact]
        public void RoleLockedEmote_WithoutBotRole_IsSkipped()
            => Assert.False(Chaos.CanUse(true, new List<ulong> { 7 }, BotRoles));

        [Fact]
        public void RoleLockedEmote_WithBotRole_IsUsable()
            => Assert.True(Chaos.CanUse(true, new List<ulong> { 7, 5 }, BotRoles));
    }
}
