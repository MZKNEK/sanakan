using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Sanakan.Config.Model;
using Sanakan.Database;
using Sanakan.Services;
using Sanakan.Services.PocketWaifu;
using Sanakan.Services.Profiles;
using Xunit;

namespace Artifacts
{
    // Converts every old profile of a MySQL copy of the database and checks each result.
    // Runs only when SANAKAN_PROFILE_COPY_MYSQL holds the connection string of a disposable copy; it changes that database.
    public class ProfileMigrationOnCopy
    {
        private sealed class NoNetwork : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        [Fact]
        public async Task ConvertsEveryOldProfileOfTheCopy()
        {
            var connection = Environment.GetEnvironmentVariable("SANAKAN_PROFILE_COPY_MYSQL");
            if (string.IsNullOrEmpty(connection))
                return;

            var config = new FakeConfig();
            config.Model.ConnectionString = connection;
            config.Model.DatabaseProvider = DatabaseProvider.MySql;
            config.Model.MigrateOnStart = true;
            DatabaseContext Open() => new DatabaseContext(config);

            using (var db = Open())
                db.EnsureReady();

            Directory.CreateDirectory(Dir.ProfilePictures);
            var store = new ProfileStore(new ImageProcessing(null, new TagIcon(7, "galeria", "x"), false, null, new NoNetwork()), new FixedTime { Value = new DateTime(2026, 11, 15) });
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var report = await store.MigrateAllAsync(Open);
            watch.Stop();

            var log = new StringBuilder($"candidates {report.Candidates}, converted {report.Converted}, failed {report.Failed}, {watch.Elapsed.TotalSeconds:0.0} s\n");
            using (var db = Open())
            {
                var profiles = await db.UserProfiles.AsNoTracking().ToListAsync();
                var unlocks = (await db.ProfileUnlocks.AsNoTracking().ToListAsync()).ToLookup(x => x.UserId, x => x.Feature);
                var pictures = (await db.ProfilePictures.AsNoTracking().ToListAsync()).ToLookup(x => x.UserId);
                var problems = 0;
                foreach (var row in profiles)
                {
                    var layout = ProfileLayout.FromJson(row.Layout);
                    var errors = ProfileRules.Validate(layout, ProfileStore.RulesContext(pictures[row.UserId], _ => true));
                    var missing = ProfileRules.MissingUnlocks(layout, unlocks[row.UserId]);
                    if (errors.Count > 0 || missing.Count > 0)
                    {
                        problems++;
                        log.AppendLine($"{row.UserId}: {string.Join("; ", errors)} {string.Join(", ", missing.Select(x => x.Key))}");
                    }
                }
                log.AppendLine($"profiles {profiles.Count}, with problems {problems}");
                foreach (var g in unlocks.SelectMany(x => x).GroupBy(x => x.Split(':')[0]).OrderByDescending(x => x.Count()))
                    log.AppendLine($"unlock {g.Key}: {g.Count()}");

                var output = Environment.GetEnvironmentVariable("SANAKAN_PROFILE_COPY_OUT");
                if (!string.IsNullOrEmpty(output))
                    File.WriteAllText(output, log.ToString());

                Assert.Equal(0, report.Failed);
                Assert.Equal(0, problems);
            }
        }
    }
}
