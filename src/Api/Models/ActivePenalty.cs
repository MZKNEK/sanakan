#pragma warning disable 1591

using System;
using Sanakan.Database.Models.Management;

namespace Sanakan.Api.Models
{
    /// <summary>
    /// Trwająca kara
    /// </summary>
    public class ActivePenalty
    {
        /// <summary>
        /// Id użytkownika Discord
        /// </summary>
        public ulong User { get; set; }
        /// <summary>
        /// mute albo ban
        /// </summary>
        public PenaltyType Type { get; set; }
        /// <summary>
        /// Powód
        /// </summary>
        public string Reason { get; set; }
        /// <summary>
        /// Początek kary
        /// </summary>
        public DateTime StartDate { get; set; }
        /// <summary>
        /// Czas trwania w godzinach
        /// </summary>
        public long DurationInHours { get; set; }
        /// <summary>
        /// Koniec kary
        /// </summary>
        public DateTime End { get; set; }
    }
}
