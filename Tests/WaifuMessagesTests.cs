using System.Net;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services;
using Sanakan.Services.PocketWaifu;
using Xunit;

namespace Artifacts
{
    public class WaifuMessagesTests
    {
        [Theory]
        [InlineData(150, 150, "150 punktów doświadczenia do skrzyni!")]
        [InlineData(150, 32, "32 punkty doświadczenia do skrzyni (z 150, skrzynia jest pełna)!")]
        [InlineData(150, 0, "150 punktów doświadczenia, ale skrzynia jest nieaktywna lub pełna i przepadły!")]
        public void LotteryExp_SaysWhatReachedTheChest(int won, int stored, string expected)
        {
            Assert.Equal(expected, Lottery.ExpRewardText(won, stored));
        }

        [Theory]
        [InlineData(DeckPowerStatus.TooManyCards, false, "masz za dużo kart w talii (maksymalnie 8).")]
        [InlineData(DeckPowerStatus.NotEnoughtCards, true, "twój przeciwnik nie ma żadnej aktywnej karty w talii.")]
        [InlineData(DeckPowerStatus.TooLow, false, "masz zbyt słabą talię (12.50).")]
        [InlineData(DeckPowerStatus.TooHigh, true, "twój przeciwnik ma zbyt silną talię (12.50).")]
        public void DeckStatus_NamesTheRealProblem(DeckPowerStatus status, bool opponent, string expected)
        {
            Assert.Equal(expected, Sanakan.Modules.PocketWaifu.DeckStatusMessage(status, 12.5, opponent).Replace(',', '.'));
        }

        [Fact]
        public void ShindenLookups_TellMissingFromOutage()
        {
            Assert.Equal("Nie odnaleziono serii o ID 42!", Sanakan.Modules.PocketWaifu.TitleNotFound(HttpStatusCode.NotFound, 42));
            Assert.Equal("Brak połączenia z Shindenem! Spróbuj ponownie za chwilę.", Sanakan.Modules.PocketWaifu.TitleNotFound(HttpStatusCode.BadGateway, 42));
            Assert.StartsWith("Nie odnaleziono postaci o ID 7!", Sanakan.Modules.PocketWaifu.CharacterNotFound(7));
        }

        [Fact]
        public void JoinWithinLimit_UsesTheGivenSeparator()
        {
            var lines = new[] { "a", "b", "c" };
            Assert.Equal("a\nb\nc", Moderator.JoinWithinLimit(lines, 1000, "\n"));
            Assert.Equal("a\nb\n…i jeszcze 1", Moderator.JoinWithinLimit(lines, 1000, "\n", maxEntries: 2));
        }
    }
}
