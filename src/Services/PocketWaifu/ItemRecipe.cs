#pragma warning disable 1591

using System.Collections.Generic;
using System.Linq;
using Sanakan.Database.Models;
using Sanakan.Extensions;

namespace Sanakan.Services.PocketWaifu
{
    public enum RecipeType
    {
        None, CrystalBall, BloodyMarry, YourBlood, WaifuBlood
    }

    public class ItemRecipe
    {
        public ItemRecipe(ItemType type, List<Item> items, List<CurrencyCost> pay = null) :
            this(type, type.Name(), items, pay)
        {
        }

        public ItemRecipe(ItemType type, string name, List<Item> items, List<CurrencyCost> pay = null)
        {
            Name = name;
            Type = type;
            RequiredItems = items;
            RequiredPayments = pay ?? new List<CurrencyCost>();
        }

        public string Name { get; }
        public ItemType Type { get; }
        public List<Item> RequiredItems { get; }
        public List<CurrencyCost> RequiredPayments { get; }

        public override string ToString()
        {
            return $"**Przepis na: {Name}**\n{string.Join(" | ", RequiredPayments)}\n**Wymagane przedmioty**:\n{string.Join("\n", RequiredItems.Select(x => $"{x.Name} x{x.Count}"))}";
        }
    }
}