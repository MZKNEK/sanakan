#pragma warning disable 1591

using System;
using System.Text.RegularExpressions;
using Discord;
using Sanakan.Database.Models.Management;

namespace Sanakan.Services
{
    public static class PenaltyFormat
    {
        public static readonly Color MuteColor = new Color(240, 162, 50);
        public static readonly Color BanColor = new Color(229, 72, 77);

        private static readonly Regex _autoReasonPrefix = new Regex("^Automatyczny (mute|ban): ", RegexOptions.Compiled);

        public static string Plural(long count, string one, string few, string many)
        {
            if (count == 1)
                return one;

            var lastDigit = count % 10;
            var lastTwoDigits = count % 100;
            return lastDigit >= 2 && lastDigit <= 4 && (lastTwoDigits < 12 || lastTwoDigits > 14) ? few : many;
        }

        public static string Duration(long hours)
        {
            var days = hours / 24;
            var rest = hours % 24;
            var daysText = $"{days} {(days == 1 ? "dzień" : "dni")}";
            var hoursText = $"{rest} {Plural(rest, "godzina", "godziny", "godzin")}";

            if (days == 0)
                return hoursText;

            return rest == 0 ? daysText : $"{daysText} {hoursText}";
        }

        public static string Title(PenaltyType type, bool automatic)
            => (type == PenaltyType.Ban ? "🔨 Ban" : "🔇 Wyciszenie") + (automatic ? " · automat" : "");

        public static string DmTitle(PenaltyType type)
            => type == PenaltyType.Ban ? "🔨 Elo! Dostałeś bana" : "🔇 Elo! Dostałeś wyciszenie";

        public static Color GetColor(PenaltyType type) => type == PenaltyType.Ban ? BanColor : MuteColor;

        // the stored reason keeps its prefix, because automatic bans are recognized by it
        public static string Reason(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                return "nie podano";

            var stripped = _autoReasonPrefix.Replace(reason, "", 1);
            if (stripped.Length == reason.Length || string.IsNullOrWhiteSpace(stripped))
                return reason;

            return char.ToUpperInvariant(stripped[0]) + stripped.Substring(1);
        }

        public static string Quote(string text) => "> " + text.Replace("\r", "").Replace("\n", "\n> ");

        public static string User(IUser user) => $"<@{user.Id}>\n`{user.Id}`";

        public static string Footer(string byWho, bool automatic)
            => automatic ? "🤖 Nałożone automatycznie" : $"Nałożył: {byWho}";

        public static string Timestamp(DateTime date, char style)
            => $"<t:{new DateTimeOffset(date).ToUnixTimeSeconds()}:{style}>";

        public static DateTime End(PenaltyInfo info) => info.StartDate.AddHours(info.DurationInHours);
    }
}
