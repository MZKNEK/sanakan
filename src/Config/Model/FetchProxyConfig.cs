#pragma warning disable 1591

using System.Collections.Generic;

namespace Sanakan.Config.Model
{
    public class FetchProxyConfig
    {
        public string Url { get; set; }
        public string Key { get; set; }
        public long MaxBytes { get; set; } = 5 * 1024 * 1024;
        // null keeps FetchProxy.DefaultDirectHosts; a host also matches its subdomains
        public List<string> DirectHosts { get; set; }
    }
}
