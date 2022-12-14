#pragma warning disable 1591

using System.Collections.Generic;
using Newtonsoft.Json;

namespace Sanakan.Database.Models
{
    public class BoosterPack
    {
        public ulong Id { get; set; }
        public string Name { get; set; }
        public ulong Title { get; set; }
        public int CardCnt { get; set; }
        public Rarity MinRarity { get; set; }
        public bool IsCardFromPackTradable { get; set; }
        public CardSource CardSourceFromPack { get; set; }

        public virtual ICollection<BoosterPackCharacter> Characters { get; set; }
        public virtual ICollection<RarityExcluded> RarityExcludedFromPack { get; set; }

        public ulong GameDeckId { get; set; }
        [JsonIgnore]
        public virtual GameDeck GameDeck { get; set; }
    }
}
