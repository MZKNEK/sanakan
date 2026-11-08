using System;
using Sanakan.Database.Models.Management;
using Sanakan.Services;
using Xunit;

namespace Artifacts
{
    public class PenaltyFormatTests
    {
        [Theory]
        [InlineData(1, "godzina")]
        [InlineData(2, "godziny")]
        [InlineData(4, "godziny")]
        [InlineData(5, "godzin")]
        [InlineData(12, "godzin")]
        [InlineData(14, "godzin")]
        [InlineData(22, "godziny")]
        [InlineData(0, "godzin")]
        [InlineData(112, "godzin")]
        [InlineData(123, "godziny")]
        public void Plural_FollowsPolishRules(long count, string expected)
        {
            Assert.Equal(expected, PenaltyFormat.Plural(count, "godzina", "godziny", "godzin"));
        }

        [Theory]
        [InlineData(0, "0 godzin")]
        [InlineData(1, "1 godzina")]
        [InlineData(12, "12 godzin")]
        [InlineData(23, "23 godziny")]
        [InlineData(24, "1 dzień")]
        [InlineData(25, "1 dzień 1 godzina")]
        [InlineData(42, "1 dzień 18 godzin")]
        [InlineData(48, "2 dni")]
        [InlineData(54, "2 dni 6 godzin")]
        [InlineData(720, "30 dni")]
        public void Duration_SkipsZerosAndDeclines(long hours, string expected)
        {
            Assert.Equal(expected, PenaltyFormat.Duration(hours));
        }

        [Theory]
        [InlineData("Automatyczny mute: scamowe obrazki - rozpoznano 4 scamowe obrazki w ciągu 5 minut.",
            "Scamowe obrazki - rozpoznano 4 scamowe obrazki w ciągu 5 minut.")]
        [InlineData("Automatyczny ban: spam linkami - wykryto adres spoza listy zaufanych domen.",
            "Spam linkami - wykryto adres spoza listy zaufanych domen.")]
        [InlineData("obrażanie innych na czacie", "obrażanie innych na czacie")]
        [InlineData("przekroczono maksymalną liczbę ostrzeżeń (5)", "przekroczono maksymalną liczbę ostrzeżeń (5)")]
        [InlineData("Automatyczny mute: ", "Automatyczny mute: ")]
        [InlineData("", "nie podano")]
        [InlineData(null, "nie podano")]
        public void Reason_DropsOnlyTheAutomaticPrefix(string reason, string expected)
        {
            Assert.Equal(expected, PenaltyFormat.Reason(reason));
        }

        [Fact]
        public void Reason_KeepsTheStoredPrefixRecognizable()
        {
            var stored = "Automatyczny ban: scamowe obrazki - rozpoznano 2 scamowe obrazki w ciągu 5 minut.";

            Assert.True(Moderator.IsAutomaticReason(stored));
            Assert.False(Moderator.IsAutomaticReason(PenaltyFormat.Reason(stored)));
        }

        [Fact]
        public void Title_NamesTypeAndMarksAutomatic()
        {
            Assert.Equal("🔇 Wyciszenie", PenaltyFormat.Title(PenaltyType.Mute, false));
            Assert.Equal("🔇 Wyciszenie · automat", PenaltyFormat.Title(PenaltyType.Mute, true));
            Assert.Equal("🔨 Ban", PenaltyFormat.Title(PenaltyType.Ban, false));
            Assert.Equal("🔨 Ban · automat", PenaltyFormat.Title(PenaltyType.Ban, true));
        }

        [Fact]
        public void Footer_ShowsWhoGaveThePenalty()
        {
            Assert.Equal("🤖 Nałożone automatycznie", PenaltyFormat.Footer(Moderator.AutoAuthor, true));
            Assert.Equal("Nałożył: ModNick", PenaltyFormat.Footer("ModNick", false));
        }

        [Fact]
        public void Quote_PrefixesEveryLine()
        {
            Assert.Equal("> a\n> b", PenaltyFormat.Quote("a\r\nb"));
        }

        [Fact]
        public void Timestamp_UsesDiscordFormatForTheSameInstant()
        {
            var date = new DateTime(2026, 11, 8, 9, 47, 0, DateTimeKind.Local);
            var unix = new DateTimeOffset(date).ToUnixTimeSeconds();

            Assert.Equal($"<t:{unix}:R>", PenaltyFormat.Timestamp(date, 'R'));
            Assert.Equal(date.AddHours(54), PenaltyFormat.End(new PenaltyInfo { StartDate = date, DurationInHours = 54 }));
        }
    }
}
