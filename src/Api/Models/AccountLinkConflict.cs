#pragma warning disable 1591

using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Sanakan.Api.Models
{
    /// <summary>
    /// Błąd powiązania kont z listą istniejących połączeń do wyświetlenia na stronie.
    /// </summary>
    [JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    public class AccountLinkConflict
    {
        public bool Success => false;
        public string Code { get; set; }
        public string Message { get; set; }
        public List<LinkedDiscordAccount> Accounts { get; set; }
    }
}
