#pragma warning disable 1591

using Newtonsoft.Json;

namespace Sanakan.Database.Models
{
    public enum WishlistObjectType
    {
        Card = 0,
        Title = 1,
        Character = 2
    }

    public enum WishlistEntryType
    {
        Normal = 0,
        Persistent = 1
    }

    public class WishlistObject
    {
        public ulong Id { get; set; }
        public ulong ObjectId { get; set; }
        public string ObjectName { get; set; }
        public WishlistEntryType Entry { get; set; }
        public WishlistObjectType Type { get; set; }

        public ulong GameDeckId { get; set; }
        [JsonIgnore]
        public virtual GameDeck GameDeck { get; set; }
    }
}
