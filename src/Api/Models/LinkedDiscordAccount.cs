#pragma warning disable 1591

using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Sanakan.Api.Models
{
    /// <summary>
    /// Konto Discord powiązane z Shinden, zwracane przy konflikcie połączenia.
    /// </summary>
    [JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    public class LinkedDiscordAccount
    {
        /// <summary>Id konta Discord jako tekst, bez utraty precyzji w przeglądarce.</summary>
        public string DiscordUserId { get; set; }
        /// <summary>Id powiązanego konta Shinden jako tekst.</summary>
        public string ShindenUserId { get; set; }
        /// <summary>Nazwa użytkownika Discord; null, jeśli nie udało się pobrać konta.</summary>
        public string Username { get; set; }
        /// <summary>Pseudonim na głównym serwerze, a poza nim nazwa wyświetlana lub nazwa użytkownika; null, jeśli konto jest niedostępne.</summary>
        public string Nickname { get; set; }
        /// <summary>Adres awatara, w tym domyślnego; null, jeśli konto jest niedostępne.</summary>
        public string AvatarUrl { get; set; }
    }
}
