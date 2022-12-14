#pragma warning disable 1591

using Newtonsoft.Json;

namespace Sanakan.Database.Models
{
    public enum FightType
    {
        Versus, BattleRoyale, NewVersus
    }

    public enum FightResult
    {
        Win, Lose, Draw
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
