#pragma warning disable 1591

using System;

namespace Sanakan.Extensions
{
    public static class Polish
    {
        public static string Plural(long count, string one, string few, string many)
        {
            if (count == 1 || count == -1)
                return one;

            var lastDigit = Math.Abs(count % 10);
            var lastTwoDigits = Math.Abs(count % 100);
            return lastDigit >= 2 && lastDigit <= 4 && (lastTwoDigits < 12 || lastTwoDigits > 14) ? few : many;
        }

        public static string Count(long count, string one, string few, string many) => $"{count} {Plural(count, one, few, many)}";
    }
}
