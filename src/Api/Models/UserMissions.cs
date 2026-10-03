#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.Linq;
using Sanakan.Database.Models;
using Sanakan.Extensions;

namespace Sanakan.Api.Models
{
    /// <summary>
    /// Misje użytkownika
    /// </summary>
    public class UserMissions
    {
        /// <summary>
        /// Misje dzienne (odświeżają się o północy)
        /// </summary>
        public List<UserMission> Daily { get; set; }
        /// <summary>
        /// Misje tygodniowe (odświeżają się w niedzielę)
        /// </summary>
        public List<UserMission> Weekly { get; set; }

        public static UserMissions From(User user, DateTime now) => new UserMissions
        {
            Daily = new TimeStatus().GetDailyQuestTypes().Select(x => UserMission.From(GetOrEmpty(user, x), now)).ToList(),
            Weekly = new TimeStatus().GetWeeklyQuestTypes().Select(x => UserMission.From(GetOrEmpty(user, x), now)).ToList(),
        };

        private static TimeStatus GetOrEmpty(User user, StatusType type)
            => user.TimeStatuses?.FirstOrDefault(x => x.Type == type) ?? type.NewTimeStatus();
    }

    /// <summary>
    /// Postęp misji
    /// </summary>
    public class UserMission
    {
        /// <summary>
        /// Typ misji
        /// </summary>
        public StatusType Type { get; set; }
        /// <summary>
        /// Nazwa misji
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// Obecny postęp
        /// </summary>
        public long Progress { get; set; }
        /// <summary>
        /// Wymagany postęp
        /// </summary>
        public long Required { get; set; }
        /// <summary>
        /// Czy misja jest wykonana
        /// </summary>
        public bool Completed { get; set; }
        /// <summary>
        /// Czy nagroda została odebrana
        /// </summary>
        public bool Claimed { get; set; }
        /// <summary>
        /// Nagroda
        /// </summary>
        public string Reward { get; set; }
        /// <summary>
        /// Kiedy postęp się wyzeruje (null - misja nie została jeszcze rozpoczęta)
        /// </summary>
        public DateTime? ResetsAt { get; set; }

        public static UserMission From(TimeStatus status, DateTime now)
        {
            var active = status.IsActive(now);
            var required = status.Type.ToComplete();
            var progress = active ? Math.Min(status.IValue, required) : 0;

            return new UserMission
            {
                Type = status.Type,
                Name = status.Type.Name(),
                Progress = progress,
                Required = required,
                Completed = progress >= required,
                Claimed = status.IsClaimed(now),
                Reward = status.Type.GetRewardString(),
                ResetsAt = active ? status.EndsAt : null,
            };
        }
    }
}
