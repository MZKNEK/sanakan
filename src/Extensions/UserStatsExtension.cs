#pragma warning disable 1591

using Sanakan.Database.Models;

namespace Sanakan.Extensions
{
    public static class UserStatsExtension
    {
        public static string ToView(this UserStats stats)
        {
            if (stats == null) return "";

            return $"**Wydane TC**:\n"
                + $"**-Na pakiety**: {stats.WastedTcOnCards}\n"
                + $"**-Na przedmioty**: {stats.WastedTcOnCookies}\n"
                + $"**-Zbugowanie(może być AC)**: {stats.WastedPuzzlesOnCards}\n\n"
                + $"**Wydane PC na przedmioty**: {stats.WastedPuzzlesOnCookies}\n\n"
                + $"**Wydane AC**:\n"
                + $"**-Na pakiety**: {stats.WastedActivityOnCards}\n"
                + $"**-Na przedmioty**: {stats.WastedActivityOnCookies}\n\n"
                + $"**Stracone SC**: {stats.ScLost}\n"
                + $"**Dochód SC**: {stats.IncomeInSc}\n"
                + $"**Gier na automacie**: { stats.SlotMachineGames}\n"
                + $"**Rzutów monetą**: {stats.Tail + stats.Head}\n"
                + $"**-Trafień**: {stats.Hit}\n"
                + $"**-Pudeł**: {stats.Misd}\n\n"
                + $"**Pakiety otwarte**:\n"
                + $"**-Aktywność**: {stats.OpenedBoosterPacksActivity}\n"
                + $"**-Inne**: {stats.OpenedBoosterPacks}\n\n"
                + $"**Ulepszenia do SSS**: {stats.UpgradedToSSS}\n"
                + $"**Ustawienie Yato**: {stats.YatoUpgrades}\n"
                + $"**Ustawienie Raito**: {stats.RaitoUpgrades}\n"
                + $"**Ustawienie Yami**: {stats.YamiUpgrades}\n\n"
                + $"**Użyte bilety**: {stats.LotteryTicketsUsed}\n"
                + $"**Odwrocona karma**: {stats.ReversedKarmaCnt}\n"
                + $"**Druciarstwo**: {stats.CreatedCardsFromItems}";
        }
    }
}
