using System;
using Sanakan.Api;
using Sanakan.Api.Models;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services;
using Sanakan.Services.PocketWaifu;
using Xunit;

namespace Artifacts
{
    public class FigureAndInfoTests
    {
        private static Figure Figure(bool focus = true) => new Figure
        {
            Id = 12,
            Name = "Rem",
            Character = 1,
            IsFocus = focus,
            SkeletonQuality = Quality.Delta,
            HeadQuality = Quality.Delta,
            BodyQuality = Quality.Epsilon,
            RightArmQuality = Quality.Delta,
            FocusedPart = FigurePart.LeftArm,
            PartExp = 80,
        };

        [Fact]
        public void ConstructionPoints_MatchTheInstallFormula()
        {
            Assert.Equal(680, Figure().ConstructionPointsToInstall(Quality.Delta));
            Assert.Equal(680, Figure().ConstructionPointsToInstall(new Item { Quality = Quality.Delta }));
        }

        [Fact]
        public void ListLine_ShowsProgressAndActiveMark()
        {
            Assert.StartsWith("▶️ **[12]** [Rem](", Figure().ToListLine());
            Assert.EndsWith(" · Δ · 3/7 części", Figure().ToListLine());
            Assert.Equal("[12] Rem · Δ · 3/7 części", Figure(false).ToListLine());
        }

        [Fact]
        public void View_ShowsPartsAndConstructionPoints()
        {
            var embed = Figure().GetView().Build();

            Assert.Equal("▶️ Figurka [12] · Rem", embed.Title);
            Assert.Contains("Δ Delta", embed.Description);
            Assert.Contains(embed.Fields, x => x.Name == "Części (3/7)" && x.Value.Contains("✅ Głowa Δ") && x.Value.Contains("⬜ Lewa ręka"));
            Assert.Contains(embed.Fields, x => x.Name == "Teraz budujesz: Lewa ręka" && x.Value == "Punkty konstrukcji: 80 / 680 (dla części Δ)");
        }

        [Fact]
        public void PartPoints_ShowProgressTowardsInstalling()
        {
            var figure = Figure();
            figure.PartExp = 110;

            Assert.Equal("Dodano 30 punktów konstrukcji do części Lewa ręka (110 / 680).", Waifu.PartPointsAdded(figure, 30));
        }

        [Theory]
        [InlineData(0, 10, "▱▱▱▱▱▱▱▱▱▱")]
        [InlineData(1200, 4000, "▰▰▰▱▱▱▱▱▱▱")]
        [InlineData(5000, 4000, "▰▰▰▰▰▰▰▰▰▰")]
        public void ProgressBar_IsClamped(double value, double max, string expected)
        {
            Assert.Equal(expected, FigureExtension.ProgressBar(value, max));
        }

        [Fact]
        public void BotInfo_ExplainsProblemsAndLinksTheVersion()
        {
            Assert.Equal("✅ Wszystko działa.", BotInfoFormat.Status(new HealthStatus { Status = HealthMonitor.Ok }));
            Assert.Equal("⚠️ Shinden nie odpowiada, bot jest przeciążony.", BotInfoFormat.Status(new HealthStatus
            {
                Status = HealthMonitor.Degraded,
                Shinden = new HealthShinden { Ok = false },
                Database = new HealthDatabase { Ok = true },
                Discord = new HealthDiscord { LatencyMs = 40 },
                Commands = new HealthCommands { Rejected5Min = 2 },
            }));
            Assert.Equal("https://sanakan.pl/state/versions/?v=1.4.11.10", BotInfoFormat.ChangesUrl("1.4.11.10+abc1234"));
        }

        [Fact]
        public void BotInfo_DoesNotClaimHealthWhenTheCheckFailed()
        {
            Assert.StartsWith("❔", BotInfoFormat.Status(null));
            Assert.StartsWith("Prefiks tego serwera: s.", BotInfoFormat.Build(null, "s.").Footer.Value.Text);
            Assert.StartsWith("Prefiks: s.", BotInfoFormat.Build(null, "s.", inGuild: false).Footer.Value.Text);
        }
    }
}
