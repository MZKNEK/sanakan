#pragma warning disable 1591

using Discord;
using Sanakan.Database.Models.Management;
using Sanakan.Extensions;

namespace Sanakan.Services
{
    public class ExtendedPenaltyInfo
    {
        public PenaltyInfo Info { get; set; }
        public long BaseDuration { get; set; }
        public long BonusDuration { get; set; }

        public bool IsBonusDuration() => BonusDuration > 0;
        public string GetBaseDurationAsString() => $"{BaseDuration/24} dni {BaseDuration%24} godzin";
        public string GetBonusDurationAsString() => $"{BonusDuration/24} dni {BonusDuration%24} godzin";
        public string GetTotalDurationAsString() => $"{Info.DurationInHours/24} dni {Info.DurationInHours%24} godzin";
        public string GetTimeAsString() => $"{Info.StartDate.ToShortDateString()} {Info.StartDate.ToShortTimeString()}";
        public Color GetColor() => (Info.Type == PenaltyType.Mute) ? EMType.Warning.Color() : EMType.Error.Color();
    }
}
