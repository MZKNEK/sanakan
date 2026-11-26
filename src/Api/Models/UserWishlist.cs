#pragma warning disable 1591

using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Sanakan.Database.Models;
using Sanakan.ShindenCompat.Models;

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
        /// <summary>
        /// Tylko przy titleCharacters=true: id tytułów, których postaci nie pobrano z powodu limitu
        /// (najwyżej 50 tytułów spoza pamięci podręcznej na zapytanie); można je pobrać przez api/waifu/titles/{id}/characters
        /// albo ponawiając zapytanie
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<ulong> SkippedTitles { get; set; }

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
        /// <summary>
        /// Postacie z tytułu - tylko przy titleCharacters=true i tylko dla wpisów title;
        /// brak pola przy wpisie title spoza skippedTitles oznacza, że nie udało się pobrać postaci z shindena
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<UserTitleCharacter> Characters { get; set; }

        public static UserWishlistEntry From(WishlistObject wish) => new UserWishlistEntry
        {
            Type = wish.Type,
            ObjectId = wish.ObjectId,
            Name = wish.ObjectName,
            Persistent = wish.Entry == WishlistEntryType.Persistent,
        };
    }

    /// <summary>
    /// Postać z tytułu
    /// </summary>
    public class UserTitleCharacter
    {
        /// <summary>
        /// Id postaci na shinden
        /// </summary>
        public ulong Id { get; set; }
        /// <summary>
        /// Imię i nazwisko
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// Czy użytkownik ma już kartę tej postaci (bot pomija takie postacie, szukając kart z tytułów na liście życzeń)
        /// </summary>
        public bool Owned { get; set; }

        // staff rows of a title come without a character (id 0)
        public static List<UserTitleCharacter> From(IEnumerable<IRelation> relations, ICollection<ulong> owned)
            => relations.Where(x => x.CharacterId > 0).GroupBy(x => x.CharacterId.Value).Select(x => x.First())
                .Select(x => new UserTitleCharacter
                {
                    Id = x.CharacterId.Value,
                    Name = $"{x.CharacterFirstName} {x.CharacterLastName}".Trim(),
                    Owned = owned.Contains(x.CharacterId.Value),
                }).ToList();
    }
}
