#pragma warning disable 1591

namespace Sanakan.Api.Models
{
    /// <summary>
    /// Uprawnienia użytkownika na serwerze wynikające z configa bota i ról
    /// </summary>
    public class UserPermissions
    {
        /// <summary>
        /// Id użytkownika na Discordzie
        /// </summary>
        public string DiscordId { get; set; }
        /// <summary>
        /// Id serwera, dla którego sprawdzono uprawnienia
        /// </summary>
        public string GuildId { get; set; }
        /// <summary>
        /// Czy użytkownik jest na serwerze, jeśli nie to poza Dev wszystkie flagi są false
        /// </summary>
        public bool OnGuild { get; set; }
        /// <summary>
        /// Czy użytkownik jest na liście Dev w configu bota
        /// </summary>
        public bool Dev { get; set; }
        /// <summary>
        /// Czy użytkownik ma rolę testera
        /// </summary>
        public bool Tester { get; set; }
        /// <summary>
        /// Czy użytkownik ma rolę admina lub uprawnienie Administrator na serwerze
        /// </summary>
        public bool Admin { get; set; }
        /// <summary>
        /// Czy użytkownik ma rolę pół-admina
        /// </summary>
        public bool SemiAdmin { get; set; }
        /// <summary>
        /// Czy użytkownik ma którąś z ról moderatora
        /// </summary>
        public bool Moderator { get; set; }
        /// <summary>
        /// Czy użytkownik może korzystać z poleceń wymagających roli użytkownika
        /// </summary>
        public bool User { get; set; }
    }
}
