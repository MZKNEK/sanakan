#pragma warning disable 1591

using System.Linq;
using Discord;
using Sanakan.Database.Models.Configuration;

namespace Sanakan.Services
{
    /// <summary>
    /// Reguły bezpieczeństwa komend moderacyjnych wydzielone poza moduł,
    /// aby dały się pokryć testami jednostkowymi bez mockowania Discorda.
    /// </summary>
    public static class ModerationGuard
    {
        /// <summary>
        /// Rozpoznaje role, których nie wolno dodać do samodzielnego zarządzania (selfrole),
        /// bo prowadziłoby to do eskalacji uprawnień (np. użytkownik nadałby sobie rolę admina).
        /// </summary>
        public static bool IsProtectedSelfRole(ulong roleId, int rolePosition, GuildPermissions permissions,
            GuildOptions config, int? botTopPosition)
        {
            if (config == null) return true;

            if (roleId == config.AdminRole || roleId == config.SemiAdminRole || roleId == config.TesterRole
                || roleId == config.UserRole || roleId == config.MuteRole || roleId == config.ModMuteRole
                || roleId == config.WaifuRole || roleId == config.GlobalEmotesRole || roleId == config.NitroRole)
                return true;

            if (config.ModeratorRoles != null && config.ModeratorRoles.Any(x => x.Role == roleId))
                return true;

            if (config.Lands != null && config.Lands.Any(x => x.Manager == roleId || x.Underling == roleId))
                return true;

            if (permissions.Administrator || permissions.ManageGuild || permissions.ManageRoles
                || permissions.ManageChannels || permissions.KickMembers || permissions.BanMembers || permissions.ManageWebhooks)
                return true;

            // nie pozwalamy na role równe lub wyższe od najwyższej roli bota
            if (botTopPosition.HasValue && rolePosition >= botTopPosition.Value)
                return true;

            return false;
        }

        /// <summary>
        /// Czy wolno przeczyścić konfigurację wskazanego serwera. Na własnym serwerze -
        /// tak (o uprawnienia zadba precondition), na obcym - tylko z uprawnieniami administratora.
        /// </summary>
        public static bool CanCleanGuild(ulong targetGuildId, ulong currentGuildId, bool isAdminInTargetGuild)
            => targetGuildId == currentGuildId || isAdminInTargetGuild;
    }
}
