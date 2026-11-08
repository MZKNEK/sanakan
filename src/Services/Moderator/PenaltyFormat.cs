#pragma warning disable 1591

using System;
using System.Text.RegularExpressions;
using Discord;
using Sanakan.Database.Models.Management;
using Sanakan.Extensions;

namespace Sanakan.Services
{
    public static class PenaltyFormat
    {
        public static readonly Color MuteColor = new Color(240, 162, 50);
        public static readonly Color BanColor = new Color(229, 72, 77);
        public static readonly Color WarningColor = new Color(232, 197, 71);

        private static readonly Regex _autoReasonPrefix = new Regex("^Automatyczny (mute|ban): ", RegexOptions.Compiled);

        public static string Duration(long hours)
        {
            var days = hours / 24;
            var rest = hours % 24;
            var daysText = $"{days} {(days == 1 ? "dzień" : "dni")}";
            var hoursText = Polish.Count(rest, "godzina", "godziny", "godzin");

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

        public static string Applied(ExtendedPenaltyInfo exInfo)
        {
            var text = $"{(exInfo.Info.Type == PenaltyType.Ban ? "zbanowany" : "wyciszony")} na **{Duration(exInfo.Info.DurationInHours)}**";
            return exInfo.IsBonusDuration() ? $"{text} (w tym {Duration(exInfo.BonusDuration)} dodatku)" : text;
        }

        public static string WarningCount(long warnings)
            => $"To Twoje **{warnings}. ostrzeżenie**, " + (warnings < 3 ? "przy 3. dostaniesz wyciszenie." : "kolejne oznacza wyciszenie.");

        public static string MutedEntry(PenaltyInfo info)
        {
            var reason = (Moderator.IsAutomaticReason(info.Reason) ? "🤖 " : "") + Reason(info.Reason);
            return $"<@{info.User}> · koniec {Timestamp(End(info), 'R')}\n{Quote(reason.TrimToLength(500))}";
        }
    }
}
