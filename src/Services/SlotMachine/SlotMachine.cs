#pragma warning disable 1591

using System.Collections.Generic;
using Sanakan.Database.Models;
using Sanakan.Extensions;

namespace Sanakan.Services.SlotMachine
{
    public class SlotMachine
    {
        private const string ok = "\\✔";
        private const string nok = "\\✖";

        private const int Rows = 3;
        private const int Slots = 5;

        private User User;

        public SlotMachineSlots[,] Row { get; private set; }

        public SlotMachine(User user)
        {
            Row = new SlotMachineSlots[Rows, Slots];
            User = user;
        }

        public long ToPay() => User.SMConfig.Beat.Value() * User.SMConfig.Multiplier.Value() * User.SMConfig.Rows.Value();

        private string RowIsSelected(int index) => User.SMConfig.Rows switch
        {
            SlotMachineSelectedRows.r3 => ok,
            SlotMachineSelectedRows.r2 => (index == 0 || index == 1) ? ok : nok,
            SlotMachineSelectedRows.r1 => index == 1 ? ok : nok,
            _ => nok
        };

        public string Draw()
        {
            string[] m = new string[Rows];
            for (int i = 0; i < Rows; i++)
            {
                m[i] += RowIsSelected(i) + " ";
                for (int j = 0; j < Slots; j++)
                    m[i] += Row[i, j].Icon(User.SMConfig.PsayMode > 0);
                m[i] += " ";
            }
            return string.Join("\n", m);
        }

        public List<List<string>> DrawUCS()
        {
            var ucs = new List<List<string>>();
            for (int i = 0; i < Rows; i++)
            {
                var tl = new List<string>();
                tl.Add(RowIsSelected(i) + " ");
                for (int j = 0; j < Slots; j++)
                    tl.Add(Row[i, j].Icon(User.SMConfig.PsayMode > 0));
                tl.Add(" ");
                ucs.Add(tl);
            }
            return ucs;
        }

        public long Play(ISlotRandom rng)
        {
            var lst = new List<SlotMachineWinSlots>();

            Randomize(rng);
            var win = GetWin(ref lst);
            UpdateStats(win, lst);

            if (User.SMConfig.PsayMode > 0)
                --User.SMConfig.PsayMode;

            return win;
        }

        private void UpdateStats(long win, List<SlotMachineWinSlots> slots)
        {
            ++User.Stats.SlotMachineGames;
            var scLost = ToPay() - win;
            if (scLost > 0)
            {
                User.Stats.ScLost += scLost;
                User.Stats.IncomeInSc -= scLost;
            }
            else
                User.Stats.IncomeInSc += win;
        }

        private long GetWin(ref List<SlotMachineWinSlots> list)
        {
            long rBeat = User.SMConfig.Beat.Value() * User.SMConfig.Multiplier.Value();
            long win = 0;

            switch(User.SMConfig.Rows)
            {
                case SlotMachineSelectedRows.r3:
                    win += CheckColumns(rBeat, ref list);
                    win += CheckRow(2, rBeat, ref list);
                    goto case SlotMachineSelectedRows.r2;
                case SlotMachineSelectedRows.r2:
                    win += CheckRow(0, rBeat, ref list);
                    goto case SlotMachineSelectedRows.r1;
                case SlotMachineSelectedRows.r1:
                default:
                    return win + CheckRow(1, rBeat, ref list);
            }
        }

        private void Randomize(ISlotRandom rng)
        {
            for (int i = 0; i < Rows; i++)
            {
                for (int j = 0; j < Slots; j++)
                    Row[i, j] = rng.Next(0, SlotMachineSlots.max.Value()).ToSMS();
            }
        }

        private long CheckColumns(long beat, ref List<SlotMachineWinSlots> list)
        {
            long total = 0;
            for (int i = 0; i < Slots; i++)
            {
                SlotMachineSlots ft = Row[0, i];
                if (ft == Row[1, i] && ft == Row[2, i])
                {
                    list.Add(ft.WinType(3));
                    if (ft == SlotMachineSlots.q)
                    {
                        User.SMConfig.PsayMode += ft.WinValue(3);
                        continue;
                    }
                    total += ft.WinValue(3, User.SMConfig.PsayMode > 0) * beat;
                }
            }
            return total;
        }

        private long CheckRow(int index, long beat, ref List<SlotMachineWinSlots> list)
        {
            int rt = 0;
            bool broken = false;
            SlotMachineSlots ft = Row[index, 0];
            for (int i = 0; i < Slots; i++)
            {
                if(ft == Row[index, i])
                {
                    if (!broken)
                        ++rt;
                }
                else
                {
                    broken = true;
                    if (rt < 3)
                    {
                        ft = Row[index, i];
                        broken = false;
                        rt = 1;
                    }
                }
            }
            list.Add(ft.WinType(rt));
            if (ft == SlotMachineSlots.q)
            {
                User.SMConfig.PsayMode += ft.WinValue(rt);
                return 0;
            }
            return ft.WinValue(rt, User.SMConfig.PsayMode > 0) * beat;
        }
    }
}