#pragma warning disable 1591

using Sanakan.Database.Models;
using System.Collections.Generic;

namespace Sanakan.Api.Models
{
    /// <summary>
    /// Pakiet kart
    /// </summary>
    public class CardBoosterPack
    {
        /// <summary>
        /// Definiuje czy kartami otrzymanymi z pakietu będzie można się wymienić
        /// </summary>
        public bool Tradable { get; set; }
        /// <summary>
        /// Gwarantowana jakość jednej z kart, E - 100% losowanie
        /// </summary>
        public Rarity Rarity { get; set; } = Rarity.E;
        /// <summary>
        /// Wykluczone jakości z losowania, Gwarantowana ma wyższy priorytet
        /// </summary>
        public List<Rarity> RarityExcluded { get; set; }
        /// <summary>
        /// Nazwa pakietu
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// Liczba kart w pakiecie (min. 1)
        /// </summary>
        public int Count { get; set; }
        /// <summary>
        /// Definiuje jak będą losowane postacie do kart
        /// </summary>
        public BoosterPackPool Pool { get; set; }

        public BoosterPack ToRealPack()
        {
            var pack = new BoosterPack
            {
                ExcludedRarities = BoosterPack.RarityMask(RarityExcluded),
                CardSourceFromPack = CardSource.Api,
                IsCardFromPackTradable = Tradable,
                MinRarity = Rarity,
                CardCnt = Count,
                Name = Name
            };

            switch (Pool?.Type ?? CardsPoolType.Random)
            {
                case CardsPoolType.Title:
                    pack.Title = Pool?.TitleId ?? 0;
                break;

                case CardsPoolType.List:
                    pack.Title = 0;
                    foreach (var id in Pool?.Character ?? new List<ulong>())
                        pack.Characters.Add(id);
                break;

                // the season key is resolved by the controller, it needs the season store
                case CardsPoolType.Season:
                default:
                case CardsPoolType.Random:
                    pack.Title = 0;
                break;
            }

            return pack;
        }
    }
}