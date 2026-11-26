#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Sanakan.Database.Models
{
    public class BoosterPack
    {
        public ulong Id { get; set; }
        public string Name { get; set; }
        public ulong Title { get; set; }
        public string Season { get; set; }
        public int CardCnt { get; set; }
        public Rarity MinRarity { get; set; }
        public bool IsCardFromPackTradable { get; set; }
        public CardSource CardSourceFromPack { get; set; }
        // EF overwrites the default when loading; packs granted before the column existed have the migration date
        public DateTime CreationDate { get; set; } = DateTime.Now;
        // one bit per rarity, 1 << (int)rarity
        public int ExcludedRarities { get; set; }

        // character ids to draw from, a JSON array in the database
        public List<ulong> Characters { get; set; } = new List<ulong>();

        public ulong GameDeckId { get; set; }
        [JsonIgnore]
        public virtual GameDeck GameDeck { get; set; }

        public static int RarityMask(IEnumerable<Rarity> rarities)
            => rarities?.Aggregate(0, (mask, rarity) => mask | (1 << (int)rarity)) ?? 0;

        public List<Rarity> GetExcludedRarities()
            => Enum.GetValues<Rarity>().Where(x => (ExcludedRarities & (1 << (int)x)) != 0).ToList();
    }
}
