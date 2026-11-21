#pragma warning disable 1591

namespace Sanakan.Api.Models
{
    /// <summary>
    /// Liczniki postaci
    /// </summary>
    public class CharacterCardCounts
    {
        /// <summary>
        /// Id postaci z bazy shindena
        /// </summary>
        public ulong CharacterId { get; set; }
        /// <summary>
        /// Liczba kont Shindena z kartą tej postaci
        /// </summary>
        public int Owners { get; set; }
        /// <summary>
        /// Liczba publicznych list życzeń z tą postacią
        /// </summary>
        public int Wishlists { get; set; }
        /// <summary>
        /// Liczba publicznych list życzeń aktywnych graczy z tą postacią
        /// </summary>
        public int ActiveWishlists { get; set; }
    }
}
