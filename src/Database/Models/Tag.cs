#pragma warning disable 1591

using System.Collections.Generic;
using Newtonsoft.Json;

namespace Sanakan.Database.Models
{
    public class Tag
    {
        public ulong Id { get; set; }
        public string Name { get; set; }

        public ulong GameDeckId { get; set; }
        [JsonIgnore]
        public virtual GameDeck GameDeck { get; set; }

        [JsonIgnore]
        public virtual ICollection<Card> Cards { get; set; }

        [JsonIgnore]
        public virtual ICollection<TagCardRelation> Relation { get; set; }
    }
}
