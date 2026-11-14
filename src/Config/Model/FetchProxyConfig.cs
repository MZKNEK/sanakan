#pragma warning disable 1591

using System.Collections.Generic;

namespace Sanakan.Config.Model
{
    public class FetchProxyConfig
    {
        public string Url { get; set; }
        public string Key { get; set; }
        // a plain HTTP proxy (http and https links): for everything without Url, with it only for files over MaxBytes
        public string HttpProxy { get; set; }
        public long MaxBytes { get; set; } = 5 * 1024 * 1024;
        // null keeps FetchProxy.DefaultDirectHosts; a host also matches its subdomains
        public List<string> DirectHosts { get; set; }
        // host -> address its files are downloaded from instead, with the same paths after it; works without Url too
        public Dictionary<string, string> Mirrors { get; set; }
    }
}
