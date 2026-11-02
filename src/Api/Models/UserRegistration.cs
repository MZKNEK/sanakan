#pragma warning disable 1591

namespace Sanakan.Api.Models
{
    /// <summary>
    /// Struktura rejestracji użytkownika
    /// </summary>
    public class UserRegistration
    {
        /// <summary>
        /// Id użytkownika discord
        /// </summary>
        public ulong DiscordUserId { get; set; }
        /// <summary>
        /// Id użytkownika Shinden przekazane przez uwierzytelnioną stronę
        /// </summary>
        public ulong ShindenUserId { get; set; }
    }
}
