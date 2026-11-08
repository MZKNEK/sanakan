using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sanakan.Database.Models;
using Sanakan.Services;
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

        [Fact]
        public async Task CardList_PagesByRenderedElements_AndKeepsEveryCard()
        {
            var deck = NewDeck(43, Now, Now, 10, 0);
            var plain = Enumerable.Range(0, 60).Select(_ => NewCard(deck));
            var decorated = Enumerable.Range(0, 60).Select(_ =>
            {
                var card = NewCard(deck);
                card.Active = card.Unique = card.InCage = true;
                card.CustomImage = card.CustomBorder = "x";
                return card;
            });

            var plainPages = await NewWaifu().GetWaifuFromCharacterSearchResult("posiadają:", plain, mention: true);
            var decoratedPages = await NewWaifu().GetWaifuFromCharacterSearchResult("posiadają:", decorated, mention: true);

            Assert.True(plainPages[0].Description.Split('\n').Count(x => x.Contains("<@")) > 27);
            Assert.True(decoratedPages.Count > plainPages.Count);
            foreach (var pages in new[] { plainPages, decoratedPages })
            {
                Assert.All(pages, p => Assert.True(DiscordLimits.CountElements(p.Description) <= DiscordLimits.SafeMarkdownElements));
                Assert.Equal(60, pages.Sum(p => p.Description.Split('\n').Count(x => x.Contains("<@"))));
            }
        }

        [Fact]
        public void TextRows_FillPagesUpToTheLength()
        {
            var rows = Enumerable.Range(1, 300).Select(i => $"Imię Nazwisko: {100000 + i}");

            var pages = new Sanakan.Services.Helper(new FakeConfig(), new ListLogger()).BuildEmbedsFormTextRows(rows);

            Assert.True(pages.Count <= 3);
            Assert.All(pages, p => Assert.True(p.Description.Length <= DiscordLimits.SafeDescriptionLength));
            Assert.Equal(300, pages.Sum(p => p.Description.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length));
        }
    }
}
