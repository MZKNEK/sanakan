#pragma warning disable 1591

using Newtonsoft.Json;

namespace Sanakan.Database.Models
{
    public enum FightType
    {
        Versus = 0,
        BattleRoyale = 1,
        NewVersus = 2
    }

    public enum FightResult
    {
        Win = 0,
        Lose = 1,
        Draw = 2
    }

    public class CardPvPStats
    {
        public ulong Id { get; set; }
        public FightType Type { get; set; }
        public FightResult Result { get; set; }

        public ulong GameDeckId { get; set; }
        [JsonIgnore]
        public virtual GameDeck GameDeck { get; set; }
    }
}
