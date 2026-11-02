#pragma warning disable 1591

using System.Collections.Generic;
using System.Linq;
using Sanakan.Services.PocketWaifu;

namespace Sanakan.Api.Models
{
    /// <summary>Sklepik i jego aktualna oferta.</summary>
    public class Shop
    {
        public ShopType Type { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        /// <summary>Waluta wszystkich cen w tym sklepiku: TC, PC lub AC.</summary>
        public string Currency { get; set; }
        public List<ShopItem> Items { get; set; }

        public static Shop From(Waifu waifu, ShopType type) => new Shop
        {
            Type = type,
            Name = waifu.GetShopName(type),
            Description = type switch
            {
                ShopType.Normal => "Przedmioty i pakiety kart za TC.",
                ShopType.Pvp => "Przedmioty i pakiety kart za monety PvP (PC).",
                ShopType.Activity => "Przedmioty i pakiety kart za punkty aktywności (AC).",
                _ => throw new System.ArgumentOutOfRangeException(nameof(type))
            },
            Currency = waifu.GetShopCurrencyName(type),
            Items = waifu.GetItemsWithCostForShop(type).Select((item, index) => ShopItem.From(item, index + 1)).ToList()
        };
    }
}
