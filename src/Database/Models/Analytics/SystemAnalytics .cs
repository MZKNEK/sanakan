#pragma warning disable 1591

using System;

namespace Sanakan.Database.Models.Analytics
{
    public enum SystemAnalyticsEventType
    {
        Ram = 0
    }

    public class SystemAnalytics
    {
        public ulong Id { get; set; }
        public long Value { get; set; }
        public DateTime MeasureDate { get; set; }
        public SystemAnalyticsEventType Type { get; set; }
    }
}