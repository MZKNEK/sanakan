#pragma warning disable 1591

using System.Collections.Generic;
using Sanakan.Database.Models.Management;
using Sanakan.Extensions;

namespace Sanakan.Services
{
    public static class ConfigFormat
    {
        public const int MaxGreetingLength = 2000;

        public static string TooLong(int length) => $"Wiadomość jest za długa: {length} znaków, a limit to {MaxGreetingLength}.";

        public static string GreetingSet(string what, string message)
            => message == "off" ? $"Wyłączono {what}." : $"Ustawiono {what}:\n{PenaltyFormat.Quote(message).TrimToLength(3500)}";

        public static string Toggle(string name, bool enabled) => $"{name}: **{(enabled ? "włączony" : "wyłączony")}**.";

        public static string PrefixSet(string prefix, string defaultPrefix)
            => prefix == null ? $"Przywrócono domyślny prefiks `{defaultPrefix}`." : $"Ustawiono prefiks `{prefix}`.";

        public static string MuteModifierSet(string mention, long hours, ModifierType type)
            => $"Ustawiono dodatek do wyciszeń dla {mention}: **+{PenaltyFormat.Duration(hours)}"
                + (type == ModifierType.Growing ? ", rosnący** z każdym kolejnym wyciszeniem." : "** do każdego wyciszenia.");

        public static string MuteModifierRemoved(string mention) => $"Usunięto dodatek do wyciszeń dla {mention}.";

        public static string YesNo(bool value) => value ? "tak" : "nie";

        public static string ChannelInfo(bool ignored, bool noExp, bool isThread, bool parentIgnored, bool parentNoExp)
        {
            var lines = new List<string> { $"Ignorowany: {YesNo(ignored)}", $"Bez doświadczenia: {YesNo(noExp)}" };
            if (isThread)
            {
                lines.Add($"Kanał nadrzędny ignorowany: {YesNo(parentIgnored)}");
                lines.Add($"Kanał nadrzędny bez doświadczenia: {YesNo(parentNoExp)}");
            }
            return string.Join("\n", lines);
        }

        public enum CheckState { Ok, Unpaid, Expired }

        public static string CheckLine(string name, CheckState state) => state switch
        {
            CheckState.Unpaid => $"❗ **{name}:** rola bez wykupienia, zdjęto ją",
            CheckState.Expired => $"⚠ **{name}:** wykupienie wygasło, zdjęto rolę",
            _ => $"✅ **{name}:** w porządku",
        };
    }
}
