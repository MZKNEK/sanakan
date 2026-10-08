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
                RarityExcludedFromPack = new List<RarityExcluded>(),
                Characters = new List<BoosterPackCharacter>(),
                CardSourceFromPack = CardSource.Api,
                IsCardFromPackTradable = Tradable,
                MinRarity = Rarity,
                CardCnt = Count,
                Name = Name
            };

            if (RarityExcluded != null)
            {
                if (RarityExcluded.Count > 0)
                {
                    foreach (var exc in RarityExcluded)
                        pack.RarityExcludedFromPack.Add(new RarityExcluded() { Rarity = exc });
                }
            }

            switch (Pool?.Type ?? CardsPoolType.Random)
            {
                case CardsPoolType.Title:
                    pack.Title = Pool?.TitleId ?? 0;
                break;

                case CardsPoolType.List:
                    pack.Title = 0;
                    foreach (var id in Pool?.Character ?? new List<ulong>())
                        pack.Characters.Add(new BoosterPackCharacter() { Character = id });
                break;

                default:
                case CardsPoolType.Random:
                    pack.Title = 0;
                break;
            }

            return pack;
        }
    }
}