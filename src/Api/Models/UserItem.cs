#pragma warning disable 1591

using Sanakan.Database.Models;
using Sanakan.Extensions;

namespace Sanakan.Api.Models
{
    /// <summary>
    /// Przedmiot użytkownika
    /// </summary>
    public class UserItem
    {
        /// <summary>
        /// Numer przedmiotu, ten sam co w poleceniach bota (np. użyj)
        /// </summary>
        public int Number { get; set; }
        /// <summary>
        /// Id przedmiotu
        /// </summary>
        public ulong Id { get; set; }
        /// <summary>
        /// Nazwa przedmiotu
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// Typ przedmiotu
        /// </summary>
        public ItemType Type { get; set; }
        /// <summary>
        /// Jakość przedmiotu (null - przedmiot nie ma jakości)
        /// </summary>
        public Quality? Quality { get; set; }
        /// <summary>
        /// Liczba sztuk
        /// </summary>
        public long Count { get; set; }
        /// <summary>
        /// Opis przedmiotu
        /// </summary>
        public string Description { get; set; }

        public static UserItem From(Item item, int number) => new UserItem
        {
            Number = number,
            Id = item.Id,
            Name = item.Name,
            Type = item.Type,
            Quality = item.Type.HasDifferentQualities() ? item.Quality : null,
            Count = item.Count,
            Description = item.Type.Desc(),
        };
    }
}
