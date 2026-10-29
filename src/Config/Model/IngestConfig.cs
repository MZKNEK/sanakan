#pragma warning disable 1591

using System.Collections.Generic;

namespace Sanakan.Config.Model
{
    public class IngestConfig
    {
        public string Url { get; set; }
        public string Secret { get; set; }
        public List<ulong> Channels { get; set; } = new List<ulong>();
    }
}
