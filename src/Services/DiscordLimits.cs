#pragma warning disable 1591

using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Sanakan.Services
{
    // Discord accepts up to 4096 characters in a description, but the client stops rendering
    // markdown after about 100 elements (links, bold, mentions, emoji, timestamps) in one message
    public static class DiscordLimits
    {
        public const int SafeMarkdownElements = 90;
        public const int SafeDescriptionLength = 3000;

        private static readonly string[] _openers = { "<@", "<#", "<t:", "<:", "<a:", "](" };

        public static int CountElements(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;

            var count = 0;
            foreach (var opener in _openers)
                count += Occurrences(text, opener);

            count += Occurrences(text, "**") / 2;
            count += Occurrences(text, "__") / 2;
            count += Occurrences(text, "`") / 2;

            foreach (var rune in text.EnumerateRunes())
            {
                if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.OtherSymbol)
                    ++count;
            }

            return count;
        }

        public static List<List<string>> Paginate(IEnumerable<string> lines, int firstPageReserve = 0,
            int maxElements = SafeMarkdownElements, int maxLength = SafeDescriptionLength)
        {
            var pages = new List<List<string>>();
            var page = new List<string>();
            var elements = firstPageReserve;
            var length = 0;

            foreach (var line in lines)
            {
                var lineElements = CountElements(line);
                if (page.Count > 0 && (elements + lineElements > maxElements || length + line.Length + 1 > maxLength))
                {
                    pages.Add(page);
                    page = new List<string>();
                    elements = 0;
                    length = 0;
                }

                page.Add(line);
                elements += lineElements;
                length += line.Length + 1;
            }

            if (page.Count > 0)
                pages.Add(page);

            return pages;
        }

        private static int Occurrences(string text, string value)
        {
            var count = 0;
            for (var i = text.IndexOf(value, System.StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + value.Length, System.StringComparison.Ordinal))
                ++count;
            return count;
        }
    }
}
