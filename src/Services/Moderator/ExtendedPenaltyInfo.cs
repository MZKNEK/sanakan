#pragma warning disable 1591

using Sanakan.Database.Models.Management;

namespace Sanakan.Services
{
    public class ExtendedPenaltyInfo
    {
        public PenaltyInfo Info { get; set; }
        public long BaseDuration { get; set; }
        public long BonusDuration { get; set; }

        public bool IsBonusDuration() => BonusDuration > 0;
    }
}
