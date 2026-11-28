#pragma warning disable 1591

using System.Collections.Generic;

namespace Sanakan.Api.Models
{
    /// <summary>
    /// Gracze, którzy dziś pokonali użytkownika w PvP, czyli lista polecenia rewanże
    /// </summary>
    public class UserRevenges
    {
        /// <summary>
        /// Czy dzisiejszy rewanż został już wykorzystany
        /// </summary>
        public bool Used { get; set; }
        /// <summary>
        /// Gracze w kolejności pierwszej dzisiejszej wygranej
        /// </summary>
        public List<UserRevengeTarget> Players { get; set; }
    }

    /// <summary>
    /// Gracz, na którym można wziąć rewanż
    /// </summary>
    public class UserRevengeTarget
    {
        /// <summary>
        /// Numer z listy, ten sam co w poleceniu rewanż
        /// </summary>
        public int Number { get; set; }
        /// <summary>
        /// Id na discordzie
        /// </summary>
        public string DiscordId { get; set; }
        /// <summary>
        /// Nick na discordzie (null, gdy nieznany)
        /// </summary>
        public string Nick { get; set; }
        /// <summary>
        /// Id konta na Shindenie (null, gdy konto nie jest połączone)
        /// </summary>
        public string ShindenId { get; set; }
        /// <summary>
        /// Liczba dzisiejszych wygranych z użytkownikiem
        /// </summary>
        public int Wins { get; set; }
        /// <summary>
        /// Czy rewanż na tym graczu został już wzięty
        /// </summary>
        public bool Avenged { get; set; }
    }
}
