#pragma warning disable 1591

using System.Collections.Generic;
using Sanakan.Database.Models;

namespace Sanakan.Api.Models
{
    /// <summary>
    /// Oznaczenia użytkownika
    /// </summary>
    public class UserTags
    {
        /// <summary>
        /// Własne oznaczenia (w kolejności ustawionej przez użytkownika)
        /// </summary>
        public List<UserTag> Custom { get; set; }
        /// <summary>
        /// Domyślne oznaczenia bota (ulubione, galeria, rezerwacja, wymiana, kosz)
        /// </summary>
        public List<UserTag> Default { get; set; }
        /// <summary>
        /// Maksymalna liczba własnych oznaczeń
        /// </summary>
        public int Max { get; set; }
        /// <summary>
        /// Sortowanie własnych oznaczeń
        /// </summary>
        public TagsOrder Order { get; set; }
    }

    /// <summary>
    /// Oznaczenie
    /// </summary>
    public class UserTag
    {
        /// <summary>
        /// Id oznaczenia (używane w pozostałych operacjach na oznaczeniach)
        /// </summary>
        public ulong Id { get; set; }
        /// <summary>
        /// Nazwa
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// Liczba kart z tym oznaczeniem
        /// </summary>
        public long CardCount { get; set; }
    }
}
