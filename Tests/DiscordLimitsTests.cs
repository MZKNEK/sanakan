using System.Linq;
using Sanakan.Services;
using Xunit;

namespace Artifacts
{
    public class DiscordLimitsTests
    {
        [Theory]
        [InlineData("zwykły tekst", 0)]
        [InlineData("1: <@123>", 1)]
        [InlineData("2: [Nick](https://shinden.pl/user/5)", 1)]
        [InlineData("⚠️ **Nieaktywni:**", 2)]
        [InlineData("**P[5]** [Rem](https://shinden.pl/character/5)", 2)]
        [InlineData("<t:1700000000:R> <:emote:123> `kod`", 3)]
        public void CountElements_CountsLinksMentionsBoldAndEmoji(string text, int expected)
        {
            Assert.Equal(expected, DiscordLimits.CountElements(text));
        }

        [Fact]
        public void Paginate_KeepsEachPageUnderTheElementBudget()
        {
            var lines = Enumerable.Range(1, 200).Select(i => $"{i}: <@{100000 + i}>").ToList();

            var pages = DiscordLimits.Paginate(lines, firstPageReserve: 5);

            Assert.Equal(85, pages[0].Count);
            Assert.All(pages, p => Assert.True(p.Sum(DiscordLimits.CountElements) <= DiscordLimits.SafeMarkdownElements));
            Assert.Equal(200, pages.Sum(p => p.Count));
        }

        [Fact]
        public void Paginate_AlsoRespectsLength()
        {
            var lines = Enumerable.Range(1, 10).Select(i => new string('x', 1000)).ToList();

            var pages = DiscordLimits.Paginate(lines);

            Assert.All(pages, p => Assert.True(p.Sum(x => x.Length + 1) <= DiscordLimits.SafeDescriptionLength));
            Assert.Equal(10, pages.Sum(p => p.Count));
        }
    }
}
