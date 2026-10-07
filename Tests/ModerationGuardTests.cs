using System.Collections.Generic;
using Discord;
using Sanakan.Database.Models.Configuration;
using Sanakan.Services;
using Xunit;

namespace Artifacts
{
    public class ModerationGuardTests
    {
        private static GuildOptions Config() => new GuildOptions
        {
            Id = 1,
            AdminRole = 10,
            SemiAdminRole = 11,
            TesterRole = 12,
            UserRole = 13,
            MuteRole = 14,
            ModMuteRole = 15,
            WaifuRole = 16,
            GlobalEmotesRole = 17,
            NitroRole = 18,
            ModeratorRoles = new List<ModeratorRoles>(),
            Lands = new List<MyLand>(),
        };

        [Theory]
        [InlineData(10)]
        [InlineData(11)]
        [InlineData(12)]
        [InlineData(13)]
        [InlineData(14)]
        [InlineData(15)]
        [InlineData(16)]
        [InlineData(17)]
        [InlineData(18)]
        public void ConfiguredPrivilegedRoles_AreProtected(ulong roleId)
        {
            Assert.True(ModerationGuard.IsProtectedSelfRole(roleId, 1, GuildPermissions.None, Config(), 100));
        }

        [Fact]
        public void ModeratorRole_IsProtected()
        {
            var config = Config();
            config.ModeratorRoles.Add(new ModeratorRoles { Role = 42 });

            Assert.True(ModerationGuard.IsProtectedSelfRole(42, 1, GuildPermissions.None, config, 100));
        }

        [Fact]
        public void LandManagerAndUnderling_AreProtected()
        {
            var config = Config();
            config.Lands.Add(new MyLand { Manager = 50, Underling = 51 });

            Assert.True(ModerationGuard.IsProtectedSelfRole(50, 1, GuildPermissions.None, config, 100));
            Assert.True(ModerationGuard.IsProtectedSelfRole(51, 1, GuildPermissions.None, config, 100));
        }

        [Theory]
        [InlineData(true, false, false)]
        [InlineData(false, true, false)]
        [InlineData(false, false, true)]
        public void DangerousRolePermissions_AreProtected(bool administrator, bool manageRoles, bool manageChannels)
        {
            var perms = new GuildPermissions(administrator: administrator, manageRoles: manageRoles,
                manageChannels: manageChannels);

            Assert.True(ModerationGuard.IsProtectedSelfRole(999, 1, perms, Config(), 100));
        }

        [Fact]
        public void NormalRole_IsAllowed()
        {
            Assert.False(ModerationGuard.IsProtectedSelfRole(999, 1, GuildPermissions.None, Config(), 100));
        }

        [Fact]
        public void RoleAtOrAboveBot_IsProtected()
        {
            Assert.True(ModerationGuard.IsProtectedSelfRole(999, 50, GuildPermissions.None, Config(), 50));
            Assert.True(ModerationGuard.IsProtectedSelfRole(999, 60, GuildPermissions.None, Config(), 50));
            Assert.False(ModerationGuard.IsProtectedSelfRole(999, 49, GuildPermissions.None, Config(), 50));
        }

        [Fact]
        public void WithoutKnownBotPosition_PositionIsNotChecked()
        {
            Assert.False(ModerationGuard.IsProtectedSelfRole(999, 50, GuildPermissions.None, Config(), null));
        }

        [Fact]
        public void MissingConfig_IsTreatedAsProtected()
        {
            Assert.True(ModerationGuard.IsProtectedSelfRole(999, 1, GuildPermissions.None, null, null));
        }

        [Fact]
        public void CanCleanGuild_SameGuild_IsAllowed()
        {
            Assert.True(ModerationGuard.CanCleanGuild(5, 5, false));
        }

        [Fact]
        public void CanCleanGuild_ForeignGuild_RequiresAdmin()
        {
            Assert.True(ModerationGuard.CanCleanGuild(5, 7, true));
            Assert.False(ModerationGuard.CanCleanGuild(5, 7, false));
        }
    }
}
