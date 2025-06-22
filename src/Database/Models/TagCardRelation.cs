#pragma warning disable 1591

using Newtonsoft.Json;

namespace Sanakan.Database.Models
{
    public class TagCardRelation
    {
        public ulong TagId { get; set; }
        public ulong CardId { get; set; }

        [JsonIgnore]
        public virtual Tag Tag { get; set; }
        [JsonIgnore]
        public virtual Card Card { get; set; }
    }
}
