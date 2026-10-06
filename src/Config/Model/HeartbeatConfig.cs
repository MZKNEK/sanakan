#pragma warning disable 1591

namespace Sanakan.Config.Model
{
    public class HeartbeatConfig
    {
        public string Url { get; set; }
        public string Secret { get; set; }
        public int IntervalSeconds { get; set; } = 60;
    }
}
