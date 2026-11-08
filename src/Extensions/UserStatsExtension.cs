#pragma warning disable 1591

using Discord;
using Sanakan.Database.Models;

namespace Sanakan.Extensions
{
    public static class UserStatsExtension
    {
        public static EmbedBuilder AddStatsFields(this EmbedBuilder embed, UserStats stats)
        {
            if (stats == null) return embed;

            return embed
                .AddField("Wydane TC", $"pakiety: {stats.WastedTcOnCards}\nprzedmioty: {stats.WastedTcOnCookies}\ninne: {stats.WastedPuzzlesOnCards}", true)
                .AddField("Wydane AC", $"pakiety: {stats.WastedActivityOnCards}\nprzedmioty: {stats.WastedActivityOnCookies}", true)
                .AddField("Wydane PC", $"przedmioty: {stats.WastedPuzzlesOnCookies}", true)
                .AddField("Hazard", $"automat: {stats.SlotMachineGames} gier\nrzuty: {stats.Tail + stats.Head} ({stats.Hit} trafień)\nSC: +{stats.IncomeInSc} / −{stats.ScLost}", true)
                .AddField("Pakiety otwarte", $"za aktywność: {stats.OpenedBoosterPacksActivity}\ninne: {stats.OpenedBoosterPacks}", true)
                .AddField("Ulepszenia", $"do SSS: {stats.UpgradedToSSS}\nYato {stats.YatoUpgrades} · Raito {stats.RaitoUpgrades} · Yami {stats.YamiUpgrades}", true)
                .AddField("Inne", $"bilety: {stats.LotteryTicketsUsed}\nodwrócona karma: {stats.ReversedKarmaCnt}\ndruciarstwo: {stats.CreatedCardsFromItems}", true);
        }
    }
}
