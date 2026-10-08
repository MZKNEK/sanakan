using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Sanakan.Database.Models;
using Sanakan.Services.PocketWaifu;
using Xunit;

namespace Artifacts
{
    public class CardListActivityMarkTests
    {
        private static readonly DateTime Now = new DateTime(2026, 1, 1, 12, 0, 0);

        private static Waifu NewWaifu()
            => new Waifu(null, null, null, new ListLogger(), null, null,
                new Sanakan.Services.Helper(new FakeConfig(), new ListLogger()),
                new FixedTime { Value = Now }, null, null, new FakeConfig());

        private static Card NewCard(GameDeck deck)
            => new Card { Id = 1000, Character = 5, Name = "Test", GameDeckId = deck.Id, GameDeck = deck, Tags = new List<Tag>() };

        private static GameDeck NewDeck(ulong id, DateTime measureDate, DateTime lastActivity, ulong messagesCnt, ulong messagesAtDate)
            => new GameDeck
            {
                Id = id,
                UserId = id,
                LastSignificantActivity = lastActivity,
                User = new User
                {
                    Id = id,
                    MeasureDate = measureDate,
                    MessagesCnt = messagesCnt,
                    MessagesCntAtDate = messagesAtDate,
                },
            };

        [Fact]
        public async Task CardList_ShowsInactiveMark_ForInactiveOwner()
        {
            var deck = NewDeck(42, new DateTime(2025, 1, 1), new DateTime(2025, 1, 1), 0, 0);

            var embeds = await NewWaifu().GetWaifuFromCharacterSearchResult("posiadają:", new[] { NewCard(deck) }, mention: true);

            Assert.Contains("⚠️", embeds[0].Description);
        }

        [Fact]
        public async Task CardList_HasNoMark_ForActiveOwner()
        {
            var deck = NewDeck(43, Now, Now, 10, 0);

            var embeds = await NewWaifu().GetWaifuFromCharacterSearchResult("posiadają:", new[] { NewCard(deck) }, mention: true);

            Assert.DoesNotContain("⚠️", embeds[0].Description);
        }
    }
}
