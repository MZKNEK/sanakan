#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.Linq;

namespace Sanakan.Services
{
    // Aligned plain-text columns; the log channel shows reports in a monospace code block.
    public static class TextTable
    {
        public static List<string> Render(IReadOnlyList<string[]> rows, bool[] rightAlign, string separator = "  ")
        {
            if (rows.Count == 0)
                return new List<string>();

            var columns = rows.Max(x => x.Length);
            var widths = new int[columns];
            foreach (var row in rows)
                for (int i = 0; i < row.Length; i++)
                    widths[i] = Math.Max(widths[i], (row[i] ?? "").Length);

            return rows.Select(row => string.Join(separator, Enumerable.Range(0, columns).Select(i =>
            {
                var cell = i < row.Length ? row[i] ?? "" : "";
                var right = i < rightAlign.Length && rightAlign[i];
                return right ? cell.PadLeft(widths[i]) : cell.PadRight(widths[i]);
            })).TrimEnd()).ToList();
        }
    }
}
