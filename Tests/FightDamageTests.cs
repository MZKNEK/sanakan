using Sanakan.Database.Models;
using Sanakan.Services.PocketWaifu;
using Xunit;

namespace Artifacts
{
    public class FightDamageTests
    {
        // Kamidere and Dandere hit each other without a dere multiplier
        private static Card Card(bool ultimate, int attack, int defence, Dere dere) => new Card
        {
            FromFigure = ultimate,
            Rarity = Rarity.SSS,
            Attack = attack,
            Defence = defence,
            Health = 500,
            Dere = dere,
            Image = "https://example.com/a.png",
        };

        [Fact]
        public void Ultimate_DefenceAboveAttack_StillTakes15PercentOfTheAttack()
            => Assert.Equal(150, Waifu.GetFA(Card(true, 1000, 0, Dere.Kamidere), Card(true, 0, 3000, Dere.Dandere)), 3);

        [Fact]
        public void Ultimate_AttackAboveDefence_KeepsFlatDamage()
            => Assert.Equal(2000, Waifu.GetFA(Card(true, 3000, 0, Dere.Kamidere), Card(true, 0, 1000, Dere.Dandere)), 3);

        [Fact]
        public void NormalCardAgainstUltimate_AlsoGetsTheMinimum()
            => Assert.Equal(149.85, Waifu.GetFA(Card(false, 999, 0, Dere.Kamidere), Card(true, 0, 5000, Dere.Dandere)), 3);

        [Fact]
        public void NormalCards_KeepPercentDefenceWithoutTheMinimum()
            => Assert.Equal(10, Waifu.GetFA(Card(false, 100, 0, Dere.Kamidere), Card(false, 0, 90, Dere.Dandere)), 3);
    }
}
