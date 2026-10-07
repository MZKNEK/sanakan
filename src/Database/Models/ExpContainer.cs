#pragma warning disable 1591

using Newtonsoft.Json;

namespace Sanakan.Database.Models
{
    public enum ExpContainerLevel
    {
        Disabled = 0,
        Level1 = 1,
        Level2 = 2,
        Level3 = 3,
        Level4 = 4
    }

    public class ExpContainer
    {
        public ulong Id { get; set; }
        public double ExpCount { get; set; }
        public ExpContainerLevel Level { get; set; }

        public ulong GameDeckId { get; set; }
        [JsonIgnore]
        public virtual GameDeck GameDeck { get; set; }
    }
}
