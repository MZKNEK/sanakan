#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.Linq;
using Discord;
using Discord.Commands;

namespace Sanakan.Services.Commands
{
    public static class CommandErrors
    {
        public static readonly Color ErrorColor = new Color(229, 72, 77);
        public static readonly Color BusyColor = new Color(232, 197, 71);
        public static readonly Color HelpColor = new Color(91, 141, 239);

        public const string Busy = "⏳ {0} bot ma teraz dużo pracy i nie przyjął polecenia. Spróbuj ponownie za chwilę.";

        private const string ParseFailedPrefix = "Failed to parse ";

        private static readonly Dictionary<string, string> _notFound = new Dictionary<string, string>
        {
            ["User not found."] = "nie znaleziono takiego użytkownika. Oznacz go albo podaj jego ID.",
            ["Channel not found."] = "nie znaleziono takiego kanału.",
            ["Role not found."] = "nie znaleziono takiej roli.",
            ["Message not found."] = "nie znaleziono takiej wiadomości.",
        };

        private static readonly HashSet<Type> _signed = new HashSet<Type> { typeof(sbyte), typeof(short), typeof(int), typeof(long) };
        private static readonly HashSet<Type> _unsigned = new HashSet<Type> { typeof(byte), typeof(ushort), typeof(uint), typeof(ulong) };
        private static readonly HashSet<Type> _fractional = new HashSet<Type> { typeof(float), typeof(double), typeof(decimal) };

        // Discord.NET reports these in English; messages from the bot's own type readers are already Polish
        public static string Translate(string reason, Type parameterType, string parameterDescription)
        {
            if (reason != null && _notFound.TryGetValue(reason, out var notFound))
                return notFound;

            if (reason == null || !reason.StartsWith(ParseFailedPrefix, StringComparison.Ordinal))
                return reason;

            var expected = ExpectedValue(parameterType);
            if (expected == null || string.IsNullOrWhiteSpace(parameterDescription))
                return "podano nieprawidłową wartość.";

            return $"w miejscu „{parameterDescription}” musi być {expected}.";
        }

        public static string ExpectedValue(Type type)
        {
            if (type == null) return null;
            type = Nullable.GetUnderlyingType(type) ?? type;

            if (_signed.Contains(type)) return "liczba całkowita";
            if (_unsigned.Contains(type)) return "liczba całkowita nie mniejsza od zera";
            if (_fractional.Contains(type)) return "liczba";
            return null;
        }

        public static string FormatArguments(IEnumerable<(string Description, bool Optional)> arguments)
            => string.Join("\n", arguments.Select((x, i) => $"**{i + 1}.** {x.Description}{(x.Optional ? " *(opcjonalnie)*" : "")}"));
    }
}
