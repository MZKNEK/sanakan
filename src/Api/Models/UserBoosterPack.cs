#pragma warning disable 1591

using Sanakan.Database.Models;

namespace Sanakan.Api.Models
{
    /// <summary>
    /// Pakiet kart użytkownika
    /// </summary>
    public class UserBoosterPack
    {
        /// <summary>
        /// Numer pakietu, ten sam co w poleceniu bota i w api/waifu/boosterpack/open/{numer}
        /// </summary>
        public int Number { get; set; }
        /// <summary>
        /// Id pakietu
        /// </summary>
        public ulong Id { get; set; }
        /// <summary>
        /// Nazwa pakietu
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// Liczba kart w pakiecie
        /// </summary>
        public int CardCount { get; set; }
        /// <summary>
        /// Minimalna jakość ostatniej karty
        /// </summary>
        public Rarity MinRarity { get; set; }
        /// <summary>
        /// Czy karty z pakietu można wymieniać
        /// </summary>
        public bool Tradable { get; set; }
        /// <summary>
        /// Źródło kart z pakietu
        /// </summary>
        public CardSource Source { get; set; }
        /// <summary>
        /// Skąd losowane są postacie: random - dowolne, title - z jednego tytułu, list - z wybranych postaci
        /// </summary>
        public CardsPoolType Pool { get; set; }
        /// <summary>
        /// Liczba postaci, z których losowane są karty (0 - brak ograniczenia)
        /// </summary>
        public int CharacterCount { get; set; }

        public static UserBoosterPack From(BoosterPack pack, int number) => new UserBoosterPack
        {
            Number = number,
            Id = pack.Id,
            Name = pack.Name,
            CardCount = pack.CardCnt,
            MinRarity = pack.MinRarity,
            Tradable = pack.IsCardFromPackTradable,
            Source = pack.CardSourceFromPack,
            Pool = GetPoolType(pack),
            CharacterCount = pack.Characters?.Count ?? 0,
        };

        private static CardsPoolType GetPoolType(BoosterPack pack)
        {
            if (pack.Characters?.Count > 0) return CardsPoolType.List;
            if (pack.Title != 0) return CardsPoolType.Title;
            return CardsPoolType.Random;
        }
    }
}
