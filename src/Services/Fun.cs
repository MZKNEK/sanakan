﻿#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Discord.WebSocket;
using Sanakan.Database.Models;
using Sanakan.Extensions;

namespace Sanakan.Services
{
    public enum CoinSide
    {
        Head, Tail
    }

    public enum SlotMachineSetting
    {
        Info, Multiplier, Beat, Rows
    }

    public enum SlotMachineSlots : int
    {
        p = 0, c = 1, q = 2, f = 3, g = 4, z = 5, j = 6, max = 7
    }

    public enum SlotMachineWinSlots : int
    {
        nothing = 0,
        q3 =   4, q4 =   8, q5 =   18,
        p3 =   2, p4 =  10, p5 =   20,
        c3 =   3, c4 =  15, c5 =   30,
        f3 =   5, f4 =  25, f5 =   50,
        g3 =  10, g4 =  50, g5 =  100,
        z3 =  30, z4 = 150, z5 =  300,
        j3 = 100, j4 = 300, j5 =  500,
    }

    public class Fun
    {
        private static Dictionary<ulong, int> _fakeKc = new Dictionary<ulong, int>();
        private static List<string> _botReactions = new List<string>()
        {
            "https://sanakan.pl/i/gif/mute_1.gif",
            "https://sanakan.pl/i/gif/mute_2.gif",
            "https://sanakan.pl/i/gif/mute_3.gif",
            "https://sanakan.pl/i/gif/mute_4.gif",
            "https://sanakan.pl/i/gif/mute_5.gif",
            "https://sanakan.pl/i/gif/mute_6.gif",
            "https://sanakan.pl/i/gif/mute_7.gif",
            "https://sanakan.pl/i/gif/mute_8.gif",
            "https://sanakan.pl/i/gif/mute_9.gif",
            "https://sanakan.pl/i/gif/mute_10.gif",
            "https://sanakan.pl/i/gif/mute_11.gif",
            "https://sanakan.pl/i/gif/mute_12.gif"
        };

        public static int GetAFKC(ulong charId)
        {
            if (_fakeKc.ContainsKey(charId))
                return _fakeKc[charId];

            var fkc = GetRandomValue(0, 156);
            _fakeKc.Add(charId, fkc);
            return fkc;
        }

        public static bool IsAF() => DateTime.Now.Month == 4 && DateTime.Now.Day == 1;

        public static int GetRandomValue(int max) => GetRandomValue(0, max);

        public static string GetRandomMuteReactionGif() => GetOneRandomFrom(_botReactions);

        public static int GetRandomValue(int min, int max) => RandomNumberGenerator.GetInt32(min, max);

        public static bool TakeATry(double percent) => ((double) GetRandomValue(10000) / 100) < percent;

        public static T GetOneRandomFrom<T>(IEnumerable<T> enumerable)
            => GetOneRandomFrom(enumerable.ToArray());

        public static T GetOneRandomFrom<T>(List<T> list)
            => list[GetRandomValue(list.Count)];

        public static T GetOneRandomFrom<T>(T[] arr)
            => arr[GetRandomValue(arr.Length)];

        public CoinSide RandomizeSide()
            => (CoinSide) GetRandomValue(2);

        public string GetSlotMachineInfo()
        {
            return $"**Nastawa** / **Wartośći** \n"
                    + $"Info `(wypisywanie informacji)`\n"
                    + $"Mnożnik / `1`, `2`, `3`\n"
                    + $"Stawka / `1`, `10`, `100`\n"
                    + $"Rzędy / `1`, `2`, `3`";
        }

        public string GetSlotMachineResult(string slots, SocketUser user, User botUser, long win)
        {
            string psay = (botUser.SMConfig.PsayMode > 0) ? "<:klasycznypsaj:482136878120828938> " : " ";

            return $"{psay}**Gra:** {user.Mention}\n\n ➖➖➖➖➖➖ \n{slots}\n ➖➖➖➖➖➖ \n"
                + $"**Stawka:** `{botUser.SMConfig.Beat.Value()} SC`\n"
                + $"**Mnożnik:** `x{botUser.SMConfig.Multiplier.Value()}`\n\n**Wygrana:** `{win} SC`";
        }

        public string GetSlotMachineGameInfo()
        {
            string info = $"**Info:**\n\n✖ - nieaktywny rząd\n✔ - aktywny rząd\n\n**Wygrane:**\n\n"
                + $"3-5x<:klasycznypsaj:482136878120828938> - tryb psaja (podwójne wygrane)\n\n";

            foreach (SlotMachineSlots em in Enum.GetValues(typeof(SlotMachineSlots)))
            {
                if (em != SlotMachineSlots.max && em != SlotMachineSlots.q)
                {
                    for (int i = 3; i < 6; i++)
                    {
                        string val = $"x{em.WinValue(i)}";
                        info += $"{i}x{em.Icon()} - {val.PadRight(5, ' ')} ";
                    }
                    info += "\n";
                }
            }

            return info;
        }
    }
}