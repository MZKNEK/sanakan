using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord.Commands;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services.Commands;
using Xunit;

namespace Artifacts
{
    public class HiddenCommandModule : ModuleBase<ICommandContext>
    {
        [Command("visible")]
        public Task VisibleAsync() => Task.CompletedTask;

        [Command("secret"), Hidden]
        public Task SecretAsync() => Task.CompletedTask;
    }

    public class CardViewTests
    {
        private static readonly FixedTime Time = new FixedTime();

        private static Card Card(int healthPenalty = 0) => new Card
        {
            Id = 123456,
            Character = 2135,
            Name = "Rem",
            Title = "Re:Zero",
            Rarity = Rarity.SS,
            Dere = Dere.Tsundere,
            Health = 500,
            Attack = 210,
            Defence = 80,
            Affection = healthPenalty == 0 ? 0 : 4,
            UpgradesCnt = 1,
            WhoWantsCount = 57,
            AWhoWantsCount = 12,
            Tags = new List<Tag>(),
        };

        [Fact]
        public void HealthLine_ShowsBaseOnlyWhenItDiffers()
        {
            Assert.Equal("500", Card().GetHealthLine());
            var hurt = Card(1);
            Assert.Equal("600 (bazowo 500)", hurt.GetHealthLine());
        }

        [Fact]
        public void SmallView_GroupsInfoInThreeColumns()
        {
            var embed = Card().GetSmallView(null, Time).Build();

            Assert.StartsWith("**[[123456](", embed.Description);
            Assert.Equal(new[] { "Walka", "Postać", "Rozwój", "Kolekcja" }, embed.Fields.Select(x => x.Name));
            Assert.All(embed.Fields.Take(3), x => Assert.True(x.Inline));
            Assert.Equal("❤ 500\n🔥 210\n🛡 80", embed.Fields[0].Value);
            Assert.Equal("Tsundere\nObojętność\nZmęczenie: brak", embed.Fields[1].Value);
            Assert.EndsWith("\nUlepszenia: 1", embed.Fields[2].Value);
            Assert.StartsWith("KC: 57 (12 aktywnych) · postać 2135\nTagi: ", embed.Fields[3].Value);
        }

        [Fact]
        public void FullView_HasAThreeByThreeGridAndOneLinePerFact()
        {
            var embed = Card().GetView(true, null, Time).Build();

            Assert.Equal("Rem · SS", embed.Title);
            Assert.Equal(9, embed.Fields.Count(x => x.Inline));
            Assert.Equal("❤ Życie", embed.Fields[0].Name);
            var more = Assert.Single(embed.Fields, x => x.Name == "Więcej").Value.Split('\n');
            Assert.StartsWith("Nieaktywna · poza klatką · ", more[0]);
            Assert.StartsWith("Numer: ", more[1]);
            Assert.Equal("KC: 57 (12 aktywnych)", more[2]);
            Assert.StartsWith("Tagi: ", more.Last());
        }

        [Fact]
        public void LegacyViews_KeepTheOldLayout()
        {
            Assert.Contains("PWR:", Card().GetLegacyDescSmall(null, Time));
            Assert.Contains("**Możliwość wymiany:** ", Card().GetLegacyDesc(true, null, Time));
        }

        [Fact]
        public void CardGameProfile_ColumnsKeepTheirRowsWithoutUltimate()
        {
            var user = new User().Default(5, Time.Now());
            user.GameDeck.Cards.Add(Card());
            user.GameDeck.CTCnt = 3200;

            var embed = Sanakan.Modules.PocketWaifu.BuildCardGameProfile(user, Time.Now()).Build();

            Assert.Equal(new[] { "Kolekcja (1)", "​", "Ultimate (0)", "PvP", "Zasoby", "Historia kart" }, embed.Fields.Select(x => x.Name));
            Assert.All(embed.Fields, x => Assert.True(x.Inline));
            Assert.Equal("SS 1\nS 0\nA 0\nB 0", embed.Fields[0].Value);
            Assert.Equal("C 0\nD 0\nE 0", embed.Fields[1].Value);
            Assert.Equal("brak", embed.Fields[2].Value);
            Assert.StartsWith("0 / 0 wygranych\n", embed.Fields[3].Value);
            Assert.StartsWith("CT: 3200\n", embed.Fields[4].Value);
            Assert.Contains("**Posiadane karty**: 1", Sanakan.Modules.PocketWaifu.BuildLegacyCardGameProfile(user, Time.Now()).Description);
        }

        [Fact]
        public async Task HiddenCommands_AreMarked()
        {
            var module = await new CommandService().AddModuleAsync<HiddenCommandModule>(null);

            Assert.False(module.Commands.Single(x => x.Name == "visible").IsHidden());
            Assert.True(module.Commands.Single(x => x.Name == "secret").IsHidden());
        }
    }
}
