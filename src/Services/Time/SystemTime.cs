#pragma warning disable 1591

using System;

namespace Sanakan.Services.Time
{
    public class SystemTime : ISystemTime
    {
        private TimeSpan _offset;

        public SystemTime() => _offset = TimeSpan.FromSeconds(0);
        public SystemTime(TimeSpan offset) => _offset = offset;

        public DateTime Now()
        {
            return DateTime.Now + _offset;
        }
    }
}