#pragma warning disable 1591

using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services;
using Sanakan.Services.SlotMachine;
using Xunit;

namespace Artifacts
{
    public class SlotMachineTests
    {
        private class FixedSlotRandom : ISlotRandom
        {
            private readonly int _value;

            public FixedSlotRandom(int value) => _value = value;

            public int Next(int min, int max) => _value;
        }

        private static User NewUser(SlotMachineBeat beat, SlotMachineBeatMultiplier multi, SlotMachineSelectedRows rows, long psay = 0)
            => new User
            {
                Id = 1,
                Stats = new UserStats(),
                SMConfig = new SlotMachineConfig
                {
                    Beat = beat,
                    Multiplier = multi,
                    Rows = rows,
                    PsayMode = psay,
                }
            };

        [Fact]
        public void ToPay_IsBeatTimesMultiplierTimesRows()
        {
            Assert.Equal(1L, new SlotMachine(NewUser(SlotMachineBeat.b1, SlotMachineBeatMultiplier.x1, SlotMachineSelectedRows.r1)).ToPay());
            Assert.Equal(60L, new SlotMachine(NewUser(SlotMachineBeat.b10, SlotMachineBeatMultiplier.x2, SlotMachineSelectedRows.r3)).ToPay());
            Assert.Equal(900L, new SlotMachine(NewUser(SlotMachineBeat.b100, SlotMachineBeatMultiplier.x3, SlotMachineSelectedRows.r3)).ToPay());
        }

        [Fact]
        public void WinTypeAndValue_BelowThree_AreNothing()
        {
            Assert.Equal(SlotMachineWinSlots.nothing, SlotMachineSlots.f.WinType(0));
            Assert.Equal(SlotMachineWinSlots.nothing, SlotMachineSlots.f.WinType(2));
            Assert.Equal(0, SlotMachineSlots.f.WinValue(2));
        }

        [Fact]
        public void WinValue_UsesPsayMultiplier()
        {
            Assert.Equal(SlotMachineWinSlots.f3, SlotMachineSlots.f.WinType(3));
            Assert.Equal(5, SlotMachineSlots.f.WinValue(3));
            Assert.Equal(10, SlotMachineSlots.f.WinValue(3, true));
        }

        [Fact]
        public void FullGrid_OneRow_PaysTheRowWin()
        {
            var user = NewUser(SlotMachineBeat.b1, SlotMachineBeatMultiplier.x1, SlotMachineSelectedRows.r1);
            var sm = new SlotMachine(user);

            var win = sm.Play(new FixedSlotRandom((int)SlotMachineSlots.f));

            Assert.Equal(50L, win); // f5 = 50 * beat(1)
            Assert.Equal(50L, user.Stats.IncomeInSc);
            Assert.Equal(1L, user.Stats.SlotMachineGames);
            Assert.Equal(0L, user.Stats.ScLost);
        }

        [Fact]
        public void FullGrid_ThreeRows_PaysColumnsAndRows()
        {
            var user = NewUser(SlotMachineBeat.b1, SlotMachineBeatMultiplier.x1, SlotMachineSelectedRows.r3);
            var sm = new SlotMachine(user);

            var win = sm.Play(new FixedSlotRandom((int)SlotMachineSlots.f));

            // kolumny: 5 * f3(5) = 25, trzy rzędy: 3 * f5(50) = 150, razem 175 * beat(1)
            Assert.Equal(175L, win);
            Assert.Equal(175L, user.Stats.IncomeInSc);
            Assert.Equal(3L, sm.ToPay());
        }

        [Fact]
        public void FullGrid_HigherBet_ScalesTheWin()
        {
            var user = NewUser(SlotMachineBeat.b10, SlotMachineBeatMultiplier.x1, SlotMachineSelectedRows.r1);
            var sm = new SlotMachine(user);

            var win = sm.Play(new FixedSlotRandom((int)SlotMachineSlots.p));

            Assert.Equal(200L, win); // p5 = 20 * beat(10)
            Assert.Equal(200L, user.Stats.IncomeInSc);
        }
    }
}
