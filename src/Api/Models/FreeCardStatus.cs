#pragma warning disable 1591

using System;

namespace Sanakan.Api.Models
{
    /// <summary>
    /// Trwający czas odnowienia karta+
    /// </summary>
    public class FreeCardStatus
    {
        /// <summary>
        /// Id użytkownika shindena
        /// </summary>
        public ulong ShindenId { get; set; }
        /// <summary>
        /// Od kiedy gracz może wziąć następną kartę (już skrócone za karty ultimate)
        /// </summary>
        public DateTime EndsAt { get; set; }
    }
}
