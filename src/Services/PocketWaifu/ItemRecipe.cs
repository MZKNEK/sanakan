#pragma warning disable 1591

using System.Collections.Generic;
using System.Linq;
using Sanakan.Database.Models;
using Sanakan.Extensions;

namespace Sanakan.Services.PocketWaifu
{
    public enum RecipeType
    {
        None, CrystalBall, BloodyMarry, YourBlood, WaifuBlood, DereChange, StatsChange, CheckCurse
    }

    public class ItemRecipe
    {
        public ItemRecipe(Item item, List<Item> items, List<CurrencyCost> pay = null) :
            this(item, $"{item.Type.Name()} x{item.Count}", items, pay)
        {
        }

        public ItemRecipe(Item item, string name, List<Item> items, List<CurrencyCost> pay = null)
        {
            Name = name;
            Item = item;
            RequiredItems = items;
            RequiredPayments = pay ?? new List<CurrencyCost>();
        }

        public string Name { get; }
        public Item Item { get; }
        public List<Item> RequiredItems { get; }
        public List<CurrencyCost> RequiredPayments { get; }

        public override string ToString()
        {
            return $"**Przepis na: {Name}**\n{string.Join(" | ", RequiredPayments)}\n**Wymagane przedmioty**:\n{string.Join("\n", RequiredItems.Select(x => $"{x.Name} x{x.Count}"))}";
        }
    }
}