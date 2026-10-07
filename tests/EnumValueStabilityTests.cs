#pragma warning disable 1591

using System;
using System.Linq;
using Sanakan.Database.Models;
using Sanakan.Database.Models.Analytics;
using Sanakan.Database.Models.Management;
using Xunit;

namespace Artifacts
{
    // Strażnik: wartości enumów są zapisane w bazie jako int.
    // Każdy z tych enumów MUSI pozostać gęsty (0..n-1) - wstawienie członka go zepsuje.
    public class EnumValueStabilityTests
    {
        private static void AssertDense(Type type)
        {
            var values = Enum.GetValues(type).Cast<int>().ToArray();

            Assert.Equal(values.Length, values.Distinct().Count());
            Assert.Equal(0, values.Min());
            Assert.Equal(values.Length - 1, values.Max());
        }

        [Fact] public void Rarity_IsStable() => AssertDense(typeof(Rarity));
        [Fact] public void Dere_IsStable() => AssertDense(typeof(Dere));
        [Fact] public void CardSource_IsStable() => AssertDense(typeof(CardSource));
        [Fact] public void StarStyle_IsStable() => AssertDense(typeof(StarStyle));
        [Fact] public void PreAssembledFigure_IsStable() => AssertDense(typeof(PreAssembledFigure));
        [Fact] public void CardCurse_IsStable() => AssertDense(typeof(CardCurse));
        [Fact] public void CardExpedition_IsStable() => AssertDense(typeof(CardExpedition));
        [Fact] public void FightType_IsStable() => AssertDense(typeof(FightType));
        [Fact] public void FightResult_IsStable() => AssertDense(typeof(FightResult));
        [Fact] public void ExpContainerLevel_IsStable() => AssertDense(typeof(ExpContainerLevel));
        [Fact] public void ActionAfterExpedition_IsStable() => AssertDense(typeof(ActionAfterExpedition));
        [Fact] public void TagsOrder_IsStable() => AssertDense(typeof(TagsOrder));
        [Fact] public void FigurePart_IsStable() => AssertDense(typeof(FigurePart));
        [Fact] public void ItemType_IsStable() => AssertDense(typeof(ItemType));
        [Fact] public void PenaltyType_IsStable() => AssertDense(typeof(PenaltyType));
        [Fact] public void ModifierType_IsStable() => AssertDense(typeof(ModifierType));
        [Fact] public void ProfileType_IsStable() => AssertDense(typeof(ProfileType));
        [Fact] public void CharacterPoolType_IsStable() => AssertDense(typeof(CharacterPoolType));
        [Fact] public void AvatarBorder_IsStable() => AssertDense(typeof(AvatarBorder));
        [Fact] public void ActivityType_IsStable() => AssertDense(typeof(ActivityType));
        [Fact] public void WishlistObjectType_IsStable() => AssertDense(typeof(WishlistObjectType));
        [Fact] public void WishlistEntryType_IsStable() => AssertDense(typeof(WishlistEntryType));
        [Fact] public void SystemAnalyticsEventType_IsStable() => AssertDense(typeof(SystemAnalyticsEventType));
        [Fact] public void TransferSource_IsStable() => AssertDense(typeof(TransferSource));
        [Fact] public void UserAnalyticsEventType_IsStable() => AssertDense(typeof(UserAnalyticsEventType));
        [Fact] public void StatusType_IsStable() => AssertDense(typeof(StatusType));

        [Fact]
        public void Quality_HasNonContiguousPinnedValues()
        {
            Assert.Equal(13, Enum.GetValues(typeof(Quality)).Length);
            Assert.Equal(0, (int)Quality.Broken);
            Assert.Equal(11, (int)Quality.Lambda);
            Assert.Equal(18, (int)Quality.Sigma);
            Assert.Equal(24, (int)Quality.Omega);
        }
    }
}
