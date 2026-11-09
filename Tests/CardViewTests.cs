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
        public void FullView_HasAGridWithPrecision()
        {
            var embed = Card().GetView(true, null, Time).Build();

            Assert.Equal("Rem · SS", embed.Title);
            Assert.Equal(9, embed.Fields.Count(x => x.Inline));
            Assert.Equal("❤ Życie", embed.Fields[0].Name);
            Assert.Contains(embed.Fields, x => x.Name == "🎯 Precyzja");
            var more = Assert.Single(embed.Fields, x => x.Name == "Więcej").Value.Split('\n');
            Assert.StartsWith("Numer: ", more[1]);
            Assert.Equal("KC: 57 (12 aktywnych)", more[2]);
        }

        [Fact]
        public void ClassicViews_KeepTheOldLayoutWithPrecision()
        {
            var card = Card();
            card.CritChance = 12.5;

            var small = card.GetDescSmall(null, Time);
            var full = card.GetDesc(true, null, Time);

            Assert.Contains("PWR:", small);
            Assert.Contains("% precyzji", small);
            Assert.Contains("**Możliwość wymiany:** ", full);
            Assert.Contains("**Dostępne ulepszenia:** 1\n**Precyzja:** ", full);
        }

        [Fact]
        public void CardGameProfile_HasTheClassicLookWithoutZeros()
        {
            var user = new User().Default(5, Time.Now());
            user.GameDeck.Cards.Add(Card());
            user.GameDeck.Cards.Add(new Card { FromFigure = true, Rarity = Rarity.SSS, Quality = Quality.Gamma, Tags = new List<Tag>() });
            user.GameDeck.CTCnt = 3200;
            user.Stats.ReleasedCards = 4;

            var text = Sanakan.Modules.PocketWaifu.BuildCardGameProfile(user, Time.Now()).Build().Description;

            Assert.Contains("**Posiadane karty**: 2\n**SS**: 1\n**[1]** **γ**: 1", text);
            Assert.Contains("**Uwolnione**: 4", text);
            Assert.Contains("**CT**: 3200", text);
            Assert.DoesNotContain("**S**: 0", text);
            Assert.DoesNotContain("**E**: 0", text);
            Assert.DoesNotContain("**α**", text);
            Assert.DoesNotContain("Zniszczone", text);
            Assert.DoesNotContain("Restartów", text);
            Assert.DoesNotContain("**PVP**", text);
            Assert.Contains("**GR**: ", text);
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
