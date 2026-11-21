#pragma warning disable 1591

using System;

namespace Sanakan.Api.Models
{
    /// <summary>
    /// Czas odnowienia karta+ gracza
    /// </summary>
    public class FreeCardStatus
    {
        /// <summary>
        /// Id użytkownika shindena
        /// </summary>
        public ulong ShindenId { get; set; }
        /// <summary>
        /// Id użytkownika Discord
        /// </summary>
        public ulong DiscordId { get; set; }
        /// <summary>
        /// Od kiedy gracz może wziąć następną kartę (już skrócone za karty ultimate); w przeszłości - może wziąć teraz
        /// </summary>
        public DateTime EndsAt { get; set; }
        /// <summary>
        /// Kiedy gracz ostatnio wziął kartę
        /// </summary>
        public DateTime TakenAt { get; set; }
        /// <summary>
        /// Czy gracz jest na głównym serwerze (null - bot nie ma jeszcze pełnej listy członków)
        /// </summary>
        public bool? OnServer { get; set; }
    }
}
