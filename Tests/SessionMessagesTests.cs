using System.Collections.Generic;
using Discord;
using Sanakan.Database.Models;
using Sanakan.Services.PocketWaifu;
using Sanakan.Services.Session.Models;
using Xunit;

namespace Artifacts
{
    public class SessionMessagesTests
    {
        private static PlayerInfo Player(bool accepted, int cards)
        {
            var list = new List<Card>();
            for (var i = 0; i < cards; i++)
                list.Add(new Card { Id = (ulong)i + 1, Name = $"Karta{i}" });

            return new PlayerInfo { User = SocketTestObjects.User(1), Accepted = accepted, Cards = list };
        }

        [Fact]
        public void Proposition_ShowsNothingAndFinishedMark()
        {
            Assert.Equal("<@1> oferuje:\n*nic*", ExchangeSession.BuildProposition(Player(false, 0), true));
            Assert.Equal("✅ <@1> oferuje:\n*nic*", ExchangeSession.BuildProposition(Player(true, 0), true));
            Assert.Equal("<@1> oferuje:\n*nic*", ExchangeSession.BuildProposition(Player(true, 0), false));
        }

        [Theory]
        [InlineData(13, "**13 kart**")]
        [InlineData(22, "**22 karty**")]
        public void Proposition_CountsManyCards(int cards, string expected)
        {
            Assert.EndsWith(expected, ExchangeSession.BuildProposition(Player(false, cards), true));
        }

        [Theory]
        [InlineData(ExchangeSession.ExchangeOutcome.None, true, "🔄 Wymiana")]
        [InlineData(ExchangeSession.ExchangeOutcome.None, false, "🔄 Wymiana: potwierdzenie")]
        [InlineData(ExchangeSession.ExchangeOutcome.Done, false, "✅ Wymiana zakończona")]
        [InlineData(ExchangeSession.ExchangeOutcome.Declined, false, "❌ Wymiana odrzucona")]
        [InlineData(ExchangeSession.ExchangeOutcome.Cancelled, false, "⚠️ Wymiana anulowana")]
        [InlineData(ExchangeSession.ExchangeOutcome.Expired, true, "⌛ Wymiana wygasła")]
        public void Title_FollowsTheOutcome(ExchangeSession.ExchangeOutcome outcome, bool adding, string expected)
        {
            Assert.Equal(expected, ExchangeSession.Title(outcome, adding));
        }

        [Fact]
        public void CancelReason_DropsTheRepeatedPrefix()
        {
            Assert.Equal("Jeden z graczy nie ma wystarczająco miejsca na otrzymywane karty.",
                ExchangeSession.CancelReason("Wymiana anulowana: jeden z graczy nie ma wystarczająco miejsca na otrzymywane karty."));
        }

        [Theory]
        [InlineData(0, 5, "Strona 1 z 5 · ⬅ ➡ zmieniają stronę")]
        [InlineData(4, 5, "Strona 5 z 5 · ⬅ ➡ zmieniają stronę")]
        [InlineData(0, 1, "Strona 1 z 1")]
        public void PageFooter_ExplainsTheArrows(int page, int pages, string expected)
        {
            Assert.Equal(expected, ListSession<string>.PageFooter(page, pages));
        }

        [Fact]
        public void ExpiredQuestion_KeepsTheTextAndAddsAFooter()
        {
            var question = new EmbedBuilder().WithDescription("Na pewno chcesz muta?").WithColor(new Color(255, 0, 0)).Build();

            var expired = AcceptSession.Expired(question);

            Assert.Equal("Na pewno chcesz muta?", expired.Description);
            Assert.Equal("⌛ Pytanie wygasło", expired.Footer?.Text);
            Assert.Equal(new Color(128, 132, 142), expired.Color);
        }
    }
}
