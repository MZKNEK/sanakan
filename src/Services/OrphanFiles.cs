#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Sanakan.Database;

namespace Sanakan.Services
{
    // Counts the pictures in GOut whose card or user is no longer in the database. It only counts, nothing is removed.
    public static class OrphanFiles
    {
        public enum Owner { Card, User }

        // a folder, the file names it holds (the owner's id in the "id" group) and whose id that is
        public sealed record Location(string Name, string Folder, Regex Pattern, Owner Owner);

        public static IReadOnlyList<Location> Default() => new[]
        {
            new Location("karty", Dir.Cards, CardFile, Owner.Card),
            new Location("miniatury", Dir.CardsMiniatures, CardFile, Owner.Card),
            new Location("karty w profilu", Dir.CardsInProfiles, CardFile, Owner.Card),
            new Location("własne obrazki i ramki kart", Dir.LocalCardData, new Regex(@"^C[IB](?<id>\d+)\.\w+$", RegexOptions.Compiled), Owner.Card),
            new Location("profile (Prof)", Dir.FakeCardsAsProfiles, IdFile, Owner.User),
            new Location("obrazki profili graczy", Dir.SavedData, new Regex(@"^(BG|SR|OV|UOV)(?<id>\d+)\.\w+$", RegexOptions.Compiled), Owner.User),
            new Location("statystyki strony", Dir.SiteStatistics, IdFile, Owner.User),
        };

        private static readonly Regex CardFile = new Regex(@"^(?<id>\d+)\.(webp|gif|png)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex IdFile = new Regex(@"^(?<id>\d+)\.\w+$", RegexOptions.Compiled);

        public sealed class Result
        {
            public string Name { get; init; }
            public long Files { get; set; }
            public long Bytes { get; set; }
            public long Orphans { get; set; }
            public long OrphanBytes { get; set; }
            // younger than the age limit: the card or user may be saved a moment later
            public long Recent { get; set; }
            // names the bot does not make, never counted as orphans
            public long Unknown { get; set; }
            public bool Missing { get; set; }
        }

        public const int ProgressEvery = 10000;

        // progress: the folder being counted and how many files of it so far
        public static async Task<List<Result>> ScanAsync(Func<DatabaseContext> createDb, DateTime olderThan, IReadOnlyList<Location> locations = null,
            Action<string, long> progress = null)
        {
            HashSet<ulong> cards, users;
            using (var db = createDb())
            {
                cards = (await db.Cards.AsNoTracking().Select(x => x.Id).ToListAsync()).ToHashSet();
                users = (await db.Users.AsNoTracking().Select(x => x.Id).ToListAsync()).ToHashSet();
            }

            // listing a million files is plain blocking IO
            return await Task.Run(() => (locations ?? Default()).Select(x => Scan(x, x.Owner == Owner.Card ? cards : users, olderThan, progress)).ToList());
        }

        private static Result Scan(Location location, HashSet<ulong> known, DateTime olderThan, Action<string, long> progress)
        {
            progress?.Invoke(location.Name, 0);
            var result = new Result { Name = location.Name };
            if (!Directory.Exists(location.Folder))
            {
                result.Missing = true;
                return result;
            }

            foreach (var file in new DirectoryInfo(location.Folder).EnumerateFiles())
            {
                if (file.LinkTarget != null)
                    continue;

                result.Files++;
                result.Bytes += file.Length;
                if (result.Files % ProgressEvery == 0)
                    progress?.Invoke(location.Name, result.Files);

                var match = location.Pattern.Match(file.Name);
                if (!match.Success || !ulong.TryParse(match.Groups["id"].Value, out var id))
                {
                    result.Unknown++;
                    continue;
                }

                if (known.Contains(id))
                    continue;

                if (file.LastWriteTimeUtc >= olderThan)
                {
                    result.Recent++;
                    continue;
                }

                result.Orphans++;
                result.OrphanBytes += file.Length;
            }
            return result;
        }

        public static string Describe(IEnumerable<Result> results, int days)
        {
            static string Size(long bytes) => bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.00} GB" : $"{bytes / (double)(1L << 20):0.0} MB";

            var lines = results.Select(x => x.Missing
                ? $"**{x.Name}**: brak katalogu"
                : $"**{x.Name}**: {x.Orphans} z {x.Files} plików bez właściciela ({Size(x.OrphanBytes)} z {Size(x.Bytes)})"
                    + (x.Recent > 0 ? $", {x.Recent} młodszych niż {days} dni pominięto" : "")
                    + (x.Unknown > 0 ? $", {x.Unknown} o nieznanej nazwie" : "")).ToList();
            var all = results.Where(x => !x.Missing).ToList();
            lines.Add($"\nRazem: {all.Sum(x => x.Orphans)} plików, {Size(all.Sum(x => x.OrphanBytes))}. Nic nie zostało usunięte.");
            return string.Join("\n", lines);
        }
    }
}
