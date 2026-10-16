#pragma warning disable 1591

using System.Collections.Generic;
using System.Linq;
using Sanakan.Config.Model;

namespace Sanakan.Api
{
    public static class LegacySiteKeys
    {
        // moves old ApiKeys entries into UserKeyApps with Site permission, keeping the same keys
        public static List<string> Migrate(ConfigModel config)
        {
            if (config.ApiKeys == null)
                return null;

            var migrated = new List<string>();
            config.UserKeyApps ??= new List<ApiApp>();

            foreach (var entry in config.ApiKeys.Where(x => !string.IsNullOrEmpty(x?.Key)))
            {
                var existing = config.UserKeyApps.FirstOrDefault(x => x.Key == entry.Key);
                if (existing != null)
                {
                    existing.Permissions |= ApiAppPermission.Site;
                    migrated.Add(existing.Bearer);
                    continue;
                }

                var name = UniqueName(config.UserKeyApps, string.IsNullOrWhiteSpace(entry.Bearer) ? "site" : entry.Bearer);
                config.UserKeyApps.Add(new ApiApp { Key = entry.Key, Bearer = name, Permissions = ApiAppPermission.Site });
                migrated.Add(name);
            }

            config.ApiKeys = null;
            return migrated;
        }

        private static string UniqueName(List<ApiApp> apps, string name)
        {
            var candidate = name;
            for (int i = 2; apps.Any(x => x.Bearer == candidate); i++)
                candidate = $"{name}-site{(i > 2 ? i - 1 : "")}";

            return candidate;
        }
    }
}
