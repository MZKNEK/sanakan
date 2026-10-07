using System;
using System.Collections.Generic;
using System.Linq;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Xunit;

namespace Artifacts
{
    public class ItemListFilterTests
    {
        private static readonly ItemType[] FigureCreationTypes =
        {
            ItemType.PreAssembledMegumin,
            ItemType.PreAssembledGintoki,
            ItemType.PreAssembledAsuna,
            ItemType.FigureSkeleton,
            ItemType.FigureUniversalPart,
            ItemType.FigureHeadPart,
            ItemType.FigureBodyPart,
            ItemType.FigureLeftArmPart,
            ItemType.FigureRightArmPart,
            ItemType.FigureLeftLegPart,
            ItemType.FigureRightLegPart,
            ItemType.FigureClothesPart,
        };

        // kolejność taka jak w s.item: GetAllItems sortuje po typie, potem po jakości
        private static List<Item> UserItems(params Item[] items)
        {
            var user = new User { GameDeck = new GameDeck { Items = items.ToList() } };
            return user.GetAllItems().ToList();
        }

        private static List<Item> MixedItems() => UserItems(
            ItemType.BloodOfYourWaifu.ToItem(3),
            ItemType.FigureHeadPart.ToItem(1, Quality.Alpha),
            ItemType.AffectionRecoverySmall.ToItem(10),
            ItemType.FigureSkeleton.ToItem(2, Quality.Beta),
            ItemType.PreAssembledAsuna.ToItem(),
            ItemType.CardFragment.ToItem(7),
            ItemType.FigureHeadPart.ToItem(1, Quality.Gamma));

        private static int NumberOf(Item item, List<Item> all) => all.IndexOf(item) + 1;

        [Fact]
        public void IsFigureCreationItem_CoversExactlyFigurePartsAndPreAssembled()
        {
            var flagged = Enum.GetValues(typeof(ItemType)).Cast<ItemType>().Where(x => x.IsFigureCreationItem());

            Assert.Equal(FigureCreationTypes.OrderBy(x => x), flagged.OrderBy(x => x));
        }

        [Fact]
        public void ToItemList_WithoutHiding_ListsEverything()
        {
            var items = MixedItems();

            Assert.Equal(items.Count, items.ToItemList("").Count);
            Assert.Equal(items.ToItemList(""), items.ToItemList("", false));
        }

        [Fact]
        public void ToItemList_Hiding_SkipsAllFigureItems()
        {
            var items = MixedItems();

            var list = items.ToItemList("", true);

            Assert.Equal(items.Count(x => !x.Type.IsFigureCreationItem()), list.Count);
            foreach (var figureItem in items.Where(x => x.Type.IsFigureCreationItem()))
                Assert.DoesNotContain(list, x => x.Contains(figureItem.Name));
        }

        [Fact]
        public void ToItemList_Hiding_KeepsOriginalNumbers()
        {
            var items = MixedItems();
            var blood = items.Single(x => x.Type == ItemType.BloodOfYourWaifu);
            var fragment = items.Single(x => x.Type == ItemType.CardFragment);

            var list = items.ToItemList("", true);

            // numer z s.item- musi wskazywać ten sam przedmiot co w s.item / s.użyj
            Assert.Contains($"**[{NumberOf(blood, items)}]** {blood.Name} x3", list);
            Assert.Contains($"**[{NumberOf(fragment, items)}]** {fragment.Name} x7", list);
            Assert.True(NumberOf(blood, items) > list.Count);
        }

        [Fact]
        public void ToItemList_Hiding_CombinesWithNameFilter()
        {
            var items = MixedItems();
            var blood = items.Single(x => x.Type == ItemType.BloodOfYourWaifu);

            var list = items.ToItemList(blood.Name, true);

            Assert.Equal(new[] { $"**[{NumberOf(blood, items)}]** {blood.Name} x3" }, list);
        }

        [Fact]
        public void ToItemList_Hiding_NameFilterMatchingOnlyFigureItems_IsEmpty()
        {
            var items = MixedItems();
            var head = items.First(x => x.Type == ItemType.FigureHeadPart);

            Assert.NotEmpty(items.ToItemList(head.Name, false));
            Assert.Empty(items.ToItemList(head.Name, true));
        }

        [Fact]
        public void CountFigureCreationItems_CountsHiddenEntries()
        {
            var items = MixedItems();
            var blood = items.Single(x => x.Type == ItemType.BloodOfYourWaifu);

            Assert.Equal(4, items.CountFigureCreationItems(""));
            Assert.Equal(2, items.CountFigureCreationItems("głowa figurki"));
            Assert.Equal(0, items.CountFigureCreationItems(blood.Name));
        }

        [Theory]
        [InlineData(1, "1 przedmiot ")]
        [InlineData(2, "2 przedmioty ")]
        [InlineData(4, "4 przedmioty ")]
        [InlineData(5, "5 przedmiotów ")]
        [InlineData(12, "12 przedmiotów ")]
        [InlineData(14, "14 przedmiotów ")]
        [InlineData(21, "21 przedmiotów ")]
        [InlineData(22, "22 przedmioty ")]
        [InlineData(112, "112 przedmiotów ")]
        [InlineData(124, "124 przedmioty ")]
        public void GetHiddenFigureItemsInfo_UsesPolishPluralForms(int count, string expected)
        {
            Assert.StartsWith($"Pominięto {expected}", ItemExtension.GetHiddenFigureItemsInfo(count));
        }

        [Fact]
        public void ToItemList_Hiding_OnlyFigureItems_IsEmpty()
        {
            var items = UserItems(
                ItemType.FigureSkeleton.ToItem(1, Quality.Alpha),
                ItemType.FigureClothesPart.ToItem(1, Quality.Beta),
                ItemType.PreAssembledMegumin.ToItem());

            Assert.Empty(items.ToItemList("", true));
        }
    }
}
