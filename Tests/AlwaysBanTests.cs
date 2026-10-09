using System.Collections.Generic;
using Sanakan.Services.Supervisor;
using Xunit;

namespace Artifacts
{
    public class AlwaysBanTests
    {
        [Fact]
        public void AdminRole_IsStaff()
            => Assert.True(Supervisor.IsAlwaysBanStaff(10, 11, new List<ulong> { 10 }, null));

        [Fact]
        public void SemiAdminRole_IsStaff()
            => Assert.True(Supervisor.IsAlwaysBanStaff(10, 11, new List<ulong> { 11 }, null));

        [Fact]
        public void ModeratorRole_IsStaff()
            => Assert.True(Supervisor.IsAlwaysBanStaff(10, 11, new List<ulong> { 42 }, new List<ulong> { 42, 43 }));

        [Fact]
        public void NormalUser_IsNotStaff()
            => Assert.False(Supervisor.IsAlwaysBanStaff(10, 11, new List<ulong> { 7, 8, 9 }, new List<ulong> { 42 }));

        [Fact]
        public void ZeroConfiguredRoles_AreIgnored()
            => Assert.False(Supervisor.IsAlwaysBanStaff(0, 0, new List<ulong> { 0 }, null));

        [Fact]
        public void NullUserRoles_IsNotStaff()
            => Assert.False(Supervisor.IsAlwaysBanStaff(10, 11, null, new List<ulong> { 42 }));

        [Fact]
        public void Owner_IsProtected()
            => Assert.True(Supervisor.IsAlwaysBanProtected(true, false, 0, 0, new List<ulong>(), null));

        [Fact]
        public void AdministratorPermission_IsProtected()
            => Assert.True(Supervisor.IsAlwaysBanProtected(false, true, 0, 0, new List<ulong>(), null));

        [Fact]
        public void Protected_DelegatesToConfiguredAdminRole()
            => Assert.True(Supervisor.IsAlwaysBanProtected(false, false, 10, 11, new List<ulong> { 10 }, null));

        [Fact]
        public void NormalUser_IsNotProtected()
            => Assert.False(Supervisor.IsAlwaysBanProtected(false, false, 10, 11, new List<ulong> { 7 }, null));

        [Fact]
        public void NullModeratorRoles_OnlyAdminAndSemiAdminCount()
        {
            Assert.True(Supervisor.IsAlwaysBanStaff(10, 11, new List<ulong> { 10 }, null));
            Assert.False(Supervisor.IsAlwaysBanStaff(10, 11, new List<ulong> { 42 }, null));
        }
    }
}
