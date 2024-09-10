#pragma warning disable 1591

using System;

namespace Sanakan.Extensions
{
    public static class DateTimeExtension
    {
        public static string ToShortDateTime(this DateTime date) =>
            $"<t:{(long)date.ToUniversalTime().Subtract(DateTime.UnixEpoch).TotalSeconds}:f>";
            // $"{date.ToShortDateString()} {date.ToShortTimeString()}";

        public static string ToRemTime(this DateTime date) =>
            $"<t:{(long)date.ToUniversalTime().Subtract(DateTime.UnixEpoch).TotalSeconds}:R>";
    }
}
