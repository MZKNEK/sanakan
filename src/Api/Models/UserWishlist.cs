#pragma warning disable 1591

using System.Collections.Generic;
using System.Linq;
using Sanakan.Database.Models;

namespace Sanakan.Api.Models
{
    /// <summary>
    /// Lista życzeń użytkownika (również prywatna)
    /// </summary>
    public class UserWishlist
    {
        /// <summary>
        /// Czy lista jest ukryta przed innymi
        /// </summary>
        public bool IsPrivate { get; set; }
        /// <summary>
        /// Wpisy
        /// </summary>
        public List<UserWishlistEntry> Entries { get; set; }

        public static UserWishlist From(GameDeck deck) => new UserWishlist
        {
            IsPrivate = deck.WishlistIsPrivate,
            Entries = (deck.Wishes ?? new List<WishlistObject>()).Select(UserWishlistEntry.From).ToList(),
        };
    }

    /// <summary>
    /// Wpis na liście życzeń
    /// </summary>
    public class UserWishlistEntry
    {
        /// <summary>
        /// Rodzaj wpisu: card - karta (WID), title - tytuł (id shinden), character - postać (id shinden)
        /// </summary>
        public WishlistObjectType Type { get; set; }
        /// <summary>
        /// WID karty lub id tytułu/postaci na shinden
        /// </summary>
        public ulong ObjectId { get; set; }
        /// <summary>
        /// Nazwa
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// Czy wpis zostaje po zdobyciu karty
        /// </summary>
        public bool Persistent { get; set; }

        public static UserWishlistEntry From(WishlistObject wish) => new UserWishlistEntry
        {
            Type = wish.Type,
            ObjectId = wish.ObjectId,
            Name = wish.ObjectName,
            Persistent = wish.Entry == WishlistEntryType.Persistent,
        };
    }
}
