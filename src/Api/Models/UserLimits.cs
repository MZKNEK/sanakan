#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.Linq;
using Sanakan.Database.Models;
using Sanakan.Extensions;

namespace Sanakan.Api.Models
{
    /// <summary>
    /// Dzienne limity i czasy odnowienia użytkownika
    /// </summary>
    public class UserLimits
    {
        /// <summary>
        /// Akcje z czasem odnowienia
        /// </summary>
        public List<UserCooldown> Cooldowns { get; set; }
        /// <summary>
        /// Dzienne liczniki
        /// </summary>
        public List<UserDailyCounter> Counters { get; set; }

        public static UserLimits From(User user, DateTime now, long packsPerDay)
        {
            var freeCard = GetOrEmpty(user, StatusType.Card).Sub(TimeSpan.FromHours(user.GameDeck.GetFreeCardCooldownReductionHours()));
            var pvp = GetOrEmpty(user, StatusType.Pvp);

            return new UserLimits
            {
                Cooldowns = new List<UserCooldown>
                {
                    UserCooldown.From(StatusType.Daily, "drobne", GetOrEmpty(user, StatusType.Daily), now),
                    UserCooldown.From(StatusType.Hourly, "zaskórniaki", GetOrEmpty(user, StatusType.Hourly), now),
                    UserCooldown.From(StatusType.Card, "karta+", freeCard, now),
                    UserCooldown.From(StatusType.Market, "rynek", GetOrEmpty(user, StatusType.Market), now),
                },
                Counters = new List<UserDailyCounter>
                {
                    UserDailyCounter.From(StatusType.Pvp, "pojedynki PvP", pvp, now, UserExtension.MAX_PVP_DAILY_GAMES,
                        pvp.IsActive(now) ? (long)user.GameDeck.PVPDailyGamesPlayed : 0),
                    UserDailyCounter.From(StatusType.Packet, "pakiety za aktywność", GetOrEmpty(user, StatusType.Packet), now, packsPerDay < 1 ? 3 : packsPerDay),
                    UserDailyCounter.From(StatusType.Tinkering, "druciarstwo", GetOrEmpty(user, StatusType.Tinkering), now, null),
                },
            };
        }

        private static TimeStatus GetOrEmpty(User user, StatusType type)
            => user.TimeStatuses?.FirstOrDefault(x => x.Type == type) ?? type.NewTimeStatus();
    }

    /// <summary>
    /// Akcja z czasem odnowienia
    /// </summary>
    public class UserCooldown
    {
        /// <summary>
        /// Typ
        /// </summary>
        public StatusType Type { get; set; }
        /// <summary>
        /// Nazwa (polecenie bota)
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// Czy można użyć teraz
        /// </summary>
        public bool Available { get; set; }
        /// <summary>
        /// Od kiedy będzie dostępne (null - dostępne teraz)
        /// </summary>
        public DateTime? AvailableAt { get; set; }

        public static UserCooldown From(StatusType type, string name, TimeStatus status, DateTime now)
        {
            var active = status.IsActive(now);
            return new UserCooldown
            {
                Type = type,
                Name = name,
                Available = !active,
                AvailableAt = active ? status.EndsAt : null,
            };
        }
    }

    /// <summary>
    /// Dzienny licznik
    /// </summary>
    public class UserDailyCounter
    {
        /// <summary>
        /// Typ
        /// </summary>
        public StatusType Type { get; set; }
        /// <summary>
        /// Nazwa
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// Wykorzystane dzisiaj
        /// </summary>
        public long Used { get; set; }
        /// <summary>
        /// Dzienny limit (null - brak limitu)
        /// </summary>
        public long? Max { get; set; }
        /// <summary>
        /// Kiedy licznik się wyzeruje (null - dziś nie był używany)
        /// </summary>
        public DateTime? ResetsAt { get; set; }

        public static UserDailyCounter From(StatusType type, string name, TimeStatus status, DateTime now, long? max, long? used = null)
        {
            var active = status.IsActive(now);
            var value = used ?? (active ? status.IValue : 0);
            return new UserDailyCounter
            {
                Type = type,
                Name = name,
                Used = max.HasValue ? Math.Min(value, max.Value) : value,
                Max = max,
                ResetsAt = active ? status.EndsAt : null,
            };
        }
    }
}
