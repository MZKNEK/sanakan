using Sanakan.Database.Models.Management;
using Sanakan.Services;
using Xunit;

namespace Artifacts
{
    public class ConfigFormatTests
    {
        [Fact]
        public void TooLong_ShowsLengthAndLimit()
        {
            Assert.Equal("Wiadomość jest za długa: 2347 znaków, a limit to 2000.", ConfigFormat.TooLong(2347));
        }

        [Fact]
        public void GreetingSet_QuotesTheMessageOrSaysItIsOff()
        {
            Assert.Equal("Ustawiono wiadomość powitalną:\n> No elo ^mention!\n> Miłej zabawy",
                ConfigFormat.GreetingSet("wiadomość powitalną", "No elo ^mention!\nMiłej zabawy"));
            Assert.Equal("Wyłączono wiadomość pożegnalną.", ConfigFormat.GreetingSet("wiadomość pożegnalną", "off"));
        }

        [Fact]
        public void Toggle_AndPrefix_ReadPlainly()
        {
            Assert.Equal("Tryb nadzoru: **wyłączony**.", ConfigFormat.Toggle("Tryb nadzoru", false));
            Assert.Equal("Tryb siania chaosu: **włączony**.", ConfigFormat.Toggle("Tryb siania chaosu", true));
            Assert.Equal("Przywrócono domyślny prefiks `s.`.", ConfigFormat.PrefixSet(null, "s."));
            Assert.Equal("Ustawiono prefiks `!`.", ConfigFormat.PrefixSet("!", "s."));
        }

        [Fact]
        public void MuteModifier_DescribesWhatWasSet()
        {
            Assert.Equal("Ustawiono dodatek do wyciszeń dla <@1>: **+1 dzień** do każdego wyciszenia.",
                ConfigFormat.MuteModifierSet("<@1>", 24, ModifierType.Constant));
            Assert.Equal("Ustawiono dodatek do wyciszeń dla <@1>: **+12 godzin, rosnący** z każdym kolejnym wyciszeniem.",
                ConfigFormat.MuteModifierSet("<@1>", 12, ModifierType.Growing));
        }

        [Fact]
        public void ChannelInfo_ShowsParentOnlyForThreads()
        {
            Assert.Equal("Ignorowany: nie\nBez doświadczenia: tak", ConfigFormat.ChannelInfo(false, true, false, true, true));
            Assert.Equal("Ignorowany: nie\nBez doświadczenia: nie\nKanał nadrzędny ignorowany: nie\nKanał nadrzędny bez doświadczenia: tak",
                ConfigFormat.ChannelInfo(false, false, true, false, true));
        }

        [Theory]
        [InlineData(ConfigFormat.CheckState.Ok, "✅ **Kolor:** w porządku")]
        [InlineData(ConfigFormat.CheckState.Unpaid, "❗ **Kolor:** rola bez wykupienia, zdjęto ją")]
        [InlineData(ConfigFormat.CheckState.Expired, "⚠ **Kolor:** wykupienie wygasło, zdjęto rolę")]
        public void CheckLine_SaysWhatTheBotDid(ConfigFormat.CheckState state, string expected)
        {
            Assert.Equal(expected, ConfigFormat.CheckLine("Kolor", state));
        }
    }
}
