using System.Linq;
using Discord;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services;
using Xunit;

namespace Artifacts
{
    public class StatsViewTests
    {
        [Fact]
        public void Stats_AreGroupedIntoSections()
        {
            var embed = new EmbedBuilder().AddStatsFields(new UserStats
            {
                WastedTcOnCards = 500, WastedTcOnCookies = 200, WastedPuzzlesOnCards = 30,
                SlotMachineGames = 120, Tail = 30, Head = 30, Hit = 35, IncomeInSc = 5000, ScLost = 3200,
            }).Build();

            Assert.Equal(new[] { "Wydane TC", "Wydane AC", "Wydane PC", "Hazard", "Pakiety otwarte", "Ulepszenia", "Inne" }, embed.Fields.Select(x => x.Name));
            Assert.Equal("pakiety: 500\nprzedmioty: 200\ninne: 30", embed.Fields[0].Value);
            Assert.Equal("automat: 120 gier\nrzuty: 60 (35 trafień)\nSC: +5000 / −3200", embed.Fields[3].Value);
        }

        [Fact]
        public void ManyRoles_FitInOneField()
        {
            var roles = Enumerable.Range(0, 80).Select(i => $"<@&{100000000000000000 + i}>").ToList();

            var text = Moderator.JoinWithinLimit(roles, EmbedFieldBuilder.MaxFieldValueLength, " ");

            Assert.True(text.Length <= EmbedFieldBuilder.MaxFieldValueLength);
            Assert.EndsWith(" …i jeszcze " + (80 - text.Split(' ').Count(x => x.StartsWith("<@&"))), text);
        }
    }
}
