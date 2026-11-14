#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Sanakan.Database;
using Sanakan.Database.Models.Management;

namespace Sanakan.Services
{
    // the database side of lifting a mute after an appeal and putting it back; Discord roles are handled by Moderator
    public static class MuteAppeals
    {
        public enum Result { Ok, None, Already, KeyMismatch, UnknownKey, NoGuild }

        public sealed class Lift
        {
            public Result Result { get; init; }
            public PenaltyInfo Penalty { get; init; }
            public DateTime End { get; init; }
        }

        public sealed class Restore
        {
            public Result Result { get; init; }
            public PenaltyInfo Penalty { get; init; }
            public bool Extended { get; init; }
            public TimeSpan Remaining { get; init; }
            public DateTime End { get; init; }
        }

        public static bool IsActive(PenaltyInfo penalty, DateTime now)
            => penalty.Type == PenaltyType.Mute && PenaltyFormat.End(penalty) > now;

        public static TimeSpan Remaining(LiftedPenalty lifted)
        {
            var left = PenaltyFormat.End(new PenaltyInfo { StartDate = lifted.StartDate, DurationInHours = lifted.DurationInHours }) - lifted.LiftedAt;
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }

        private static async Task<PenaltyInfo> ActiveMuteAsync(DatabaseContext db, ulong guild, ulong user, DateTime now)
            => (await db.Penalties.Include(x => x.Roles)
                    .Where(x => x.Guild == guild && x.User == user && x.Type == PenaltyType.Mute).ToListAsync())
                .Where(x => IsActive(x, now))
                .OrderByDescending(PenaltyFormat.End)
                .FirstOrDefault();

        // finds what to lift; nothing is changed until SaveLiftAsync, which runs after the roles are given back
        public static async Task<Lift> PrepareLiftAsync(DatabaseContext db, ulong guild, ulong user, string key, DateTime now)
        {
            var done = await db.LiftedPenalties.AsQueryable().FirstOrDefaultAsync(x => x.Key == key);
            if (done != null)
                return new Lift { Result = done.Guild == guild && done.User == user ? Result.Already : Result.KeyMismatch };

            var penalty = await ActiveMuteAsync(db, guild, user, now);
            return penalty == null
                ? new Lift { Result = Result.None }
                : new Lift { Result = Result.Ok, Penalty = penalty, End = PenaltyFormat.End(penalty) };
        }

        public static async Task SaveLiftAsync(DatabaseContext db, PenaltyInfo penalty, string key, DateTime now)
        {
            db.LiftedPenalties.Add(new LiftedPenalty
            {
                Key = key,
                Guild = penalty.Guild,
                User = penalty.User,
                Reason = penalty.Reason,
                StartDate = penalty.StartDate,
                DurationInHours = penalty.DurationInHours,
                Roles = LiftedPenalty.JoinRoles(penalty.Roles.Select(x => x.Role)),
                LiftedAt = now,
            });
            db.OwnedRoles.RemoveRange(penalty.Roles);
            db.Penalties.Remove(penalty);
            await db.SaveChangesAsync();
        }

        // puts the time that was left back: as the original mute, or added to a mute the user got in the meantime
        public static async Task<Restore> RestoreAsync(DatabaseContext db, ulong guild, ulong user, string key, DateTime now)
        {
            var lifted = await db.LiftedPenalties.AsQueryable().FirstOrDefaultAsync(x => x.Key == key);
            if (lifted == null)
                return new Restore { Result = Result.UnknownKey };
            if (lifted.Guild != guild || lifted.User != user)
                return new Restore { Result = Result.KeyMismatch };
            if (lifted.RestoredAt != null)
                return new Restore { Result = Result.Already };

            var remaining = Remaining(lifted);
            if (remaining <= TimeSpan.Zero)
                return new Restore { Result = Result.None };

            var current = await ActiveMuteAsync(db, guild, user, now);
            var extended = current != null;
            if (extended)
            {
                // the end of a mute is StartDate + DurationInHours, so moving the start adds the exact time
                current.StartDate += remaining;
                foreach (var role in lifted.RoleIds().Where(x => current.Roles.All(r => r.Role != x)))
                    current.Roles.Add(new OwnedRole { Role = role });
            }
            else
            {
                current = new PenaltyInfo
                {
                    Guild = guild,
                    User = user,
                    Type = PenaltyType.Mute,
                    Reason = lifted.Reason,
                    // the same mute as before, with the time served until the lift already behind it
                    StartDate = now - (lifted.LiftedAt - lifted.StartDate),
                    DurationInHours = lifted.DurationInHours,
                    Roles = lifted.RoleIds().Select(x => new OwnedRole { Role = x }).ToList(),
                };
                db.Penalties.Add(current);
            }

            lifted.RestoredAt = now;
            await db.SaveChangesAsync();

            return new Restore { Result = Result.Ok, Penalty = current, Extended = extended, Remaining = remaining, End = PenaltyFormat.End(current) };
        }
    }
}
