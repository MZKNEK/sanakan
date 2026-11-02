#pragma warning disable 1591

using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services.PocketWaifu;

namespace Sanakan.Api.Models
{
    /// <summary>Pozycja w ofercie sklepiku.</summary>
    public class ShopItem
    {
        /// <summary>Numer pozycji od 1, zgodny z numeracją w komendzie bota dla tego sklepiku.</summary>
        public int Number { get; set; }
        public ItemType Type { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        /// <summary>Cena jednej sztuki w walucie podanej w polu currency sklepiku.</summary>
        public long Price { get; set; }

        public static ShopItem From(ItemWithCost item, int number) => new ShopItem
        {
            Number = number,
            Type = item.Item.Type,
            Name = item.Item.Name,
            Description = item.Item.Type.Desc(),
            Price = item.Cost,
        };
    }
}
