using System.Collections.Generic;

namespace Sanakan.Api.Models
{
    /// <summary>
    /// Odpowiedź na filtrowanie kart
    /// </summary>
    public class FilteredCards
    {
        /// <summary>
        /// Wszystkie karty dosepne pod tym filtrem
        /// </summary>
        public int TotalCards { get; set; }
        /// <summary>
        /// Karty uwzględniające paginację
        /// </summary>
        public IEnumerable<CardFinalView> Cards { get; set; }
    }
}