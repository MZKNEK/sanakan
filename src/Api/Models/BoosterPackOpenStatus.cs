#pragma warning disable 1591

using System.Collections.Generic;
using Newtonsoft.Json;
using Sanakan.Database.Models;

namespace Sanakan.Api.Models
{
    /// <summary>
    /// Stan otwierania pakietów zleconego z nagłówkiem Idempotency-Key
    /// </summary>
    public class BoosterPackOpenStatus
    {
        public const string Pending = "pending";
        public const string Done = "done";
        public const string Failed = "failed";

        public const string NoSpace = "no_space";
        public const string ShindenUnavailable = "shinden_unavailable";
        public const string Error = "error";

        /// <summary>
        /// pending - trwa, done - karty zostały dodane, failed - nic nie zapisano, można ponowić z tym samym kluczem
        /// </summary>
        public string Status { get; set; }
        /// <summary>
        /// Powód niepowodzenia: no_space, shinden_unavailable, error
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string Reason { get; set; }
        /// <summary>
        /// Id wszystkich wylosowanych kart
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<ulong> CardIds { get; set; }
        /// <summary>
        /// Wylosowane karty w aktualnym stanie, tylko te które nadal istnieją
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<Card> Cards { get; set; }
    }
}
