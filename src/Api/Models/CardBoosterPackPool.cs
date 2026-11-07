#pragma warning disable 1591

using System.Collections.Generic;

namespace Sanakan.Api.Models
{
    public enum CardsPoolType
    {
        Random, Title, List, Season
    }

    public class BoosterPackPool
    {
        /// <summary>
        /// Typ wybierania postaci:
        /// Radnom - losowo, 
        /// Title - postacie z tytułu (wymagane TitleId),
        /// List - postacie z listy id (wymagane Character),
        /// Season - postacie z sezonu anime (opcjonalnie Season, domyślnie bieżący sezon)
        /// </summary>
        public CardsPoolType Type { get; set; }
        /// <summary>
        /// Id tytułu z którego będą losowane postacie
        /// </summary>
        public ulong TitleId { get; set; }
        /// <summary>
        /// Id tytułu z którego będą losowane postacie
        /// </summary>
        public List<ulong> Character { get; set; }
        /// <summary>
        /// Sezon, z którego będą losowane postacie, np. 2026-autumn (puste - bieżący sezon)
        /// </summary>
        public string Season { get; set; }
    }
}
