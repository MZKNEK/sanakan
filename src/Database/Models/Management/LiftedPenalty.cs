#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.Linq;

namespace Sanakan.Database.Models.Management
{
    // a mute taken off after an accepted appeal, kept under the site's key so it can be put back for the time that was left
    public class LiftedPenalty
    {
        public ulong Id { get; set; }
        public string Key { get; set; }
        public ulong Guild { get; set; }
        public ulong User { get; set; }
        public string Reason { get; set; }
        public DateTime StartDate { get; set; }
        public long DurationInHours { get; set; }
        // roles taken away by the mute, comma separated
        public string Roles { get; set; }
        public DateTime LiftedAt { get; set; }
        public DateTime? RestoredAt { get; set; }

        public static string JoinRoles(IEnumerable<ulong> roles) => string.Join(",", roles.Distinct());

        public IEnumerable<ulong> RoleIds()
            => (Roles ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(ulong.Parse);
    }
}
