using System;
using Sanakan.Database.Models.Management;
using Sanakan.Extensions;
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
        [InlineData(-1, "godzina")]
        [InlineData(-2, "godziny")]
        [InlineData(-5, "godzin")]
        public void Plural_FollowsPolishRules(long count, string expected)
        {
            Assert.Equal(expected, Polish.Plural(count, "godzina", "godziny", "godzin"));
        }

        [Theory]
        [InlineData(1, "1 kartę")]
        [InlineData(3, "3 karty")]
        [InlineData(5, "5 kart")]
        [InlineData(13, "13 kart")]
        [InlineData(24, "24 karty")]
        public void Count_PutsTheNumberBeforeTheDeclinedNoun(long count, string expected)
        {
            Assert.Equal(expected, Polish.Count(count, "kartę", "karty", "kart"));
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

        [Theory]
        [InlineData(PenaltyType.Mute, 12, 0, "wyciszony na **12 godzin**")]
        [InlineData(PenaltyType.Mute, 54, 42, "wyciszony na **2 dni 6 godzin** (w tym 1 dzień 18 godzin dodatku)")]
        [InlineData(PenaltyType.Ban, 720, 0, "zbanowany na **30 dni**")]
        public void Applied_ShowsTotalAndBonus(PenaltyType type, long total, long bonus, string expected)
        {
            var exInfo = new ExtendedPenaltyInfo
            {
                Info = new PenaltyInfo { Type = type, DurationInHours = total },
                BaseDuration = total - bonus,
                BonusDuration = bonus,
            };

            Assert.Equal(expected, PenaltyFormat.Applied(exInfo));
        }

        [Theory]
        [InlineData(1, "To Twoje **1. ostrzeżenie**, przy 3. dostaniesz wyciszenie.")]
        [InlineData(2, "To Twoje **2. ostrzeżenie**, przy 3. dostaniesz wyciszenie.")]
        [InlineData(4, "To Twoje **4. ostrzeżenie**, kolejne oznacza wyciszenie.")]
        public void WarningCount_AnnouncesTheMute(long warnings, string expected)
        {
            Assert.Equal(expected, PenaltyFormat.WarningCount(warnings));
        }

        [Fact]
        public void MutedEntry_ShowsCountdownAndMarksAutomatic()
        {
            var start = new DateTime(2026, 11, 8, 9, 47, 0, DateTimeKind.Local);
            var end = new DateTimeOffset(start.AddHours(24)).ToUnixTimeSeconds();
            var auto = new PenaltyInfo { User = 42, StartDate = start, DurationInHours = 24,
                Reason = "Automatyczny mute: scamowe obrazki - rozpoznano 4 scamowe obrazki w ciągu 5 minut." };
            var manual = new PenaltyInfo { User = 42, StartDate = start, DurationInHours = 24, Reason = "obrażanie innych" };

            Assert.Equal($"<@42> · koniec <t:{end}:R>\n> 🤖 Scamowe obrazki - rozpoznano 4 scamowe obrazki w ciągu 5 minut.",
                PenaltyFormat.MutedEntry(auto));
            Assert.Equal($"<@42> · koniec <t:{end}:R>\n> obrażanie innych", PenaltyFormat.MutedEntry(manual));
        }

        [Fact]
        public void JoinWithinLimit_CutsWholeEntriesAndSaysHowManyAreLeft()
        {
            var entries = new[] { new string('a', 40), new string('b', 40), new string('c', 40) };

            Assert.Equal(string.Join("\n\n", entries), Moderator.JoinWithinLimit(entries, 1000));
            Assert.Equal($"{entries[0]}\n\n…i jeszcze 2", Moderator.JoinWithinLimit(entries, 100));
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

        [Fact]
        public void End_HandlesHugeDurationsUsedAsPermanent()
        {
            var info = new PenaltyInfo { StartDate = new DateTime(2026, 11, 8), DurationInHours = 99999999, User = 1, Reason = "test" };

            var end = PenaltyFormat.End(info);

            Assert.True(end.Year > 3000);
            Assert.Contains("<t:", PenaltyFormat.Timestamp(end, 'R'));
            Assert.Contains("<@1>", PenaltyFormat.MutedEntry(info));
        }
    }
}
