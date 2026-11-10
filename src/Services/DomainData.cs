#pragma warning disable 1591

using System;
using System.Threading.Tasks;

namespace Sanakan.Services
{
    public class DomainData
    {
        public string Url;
        public bool CheckExt;
        public bool IncludeSubdomains;
        public Func<string, Task<string>> Transform;

        public DomainData(string url, bool ext = false, bool includeSubdomains = false)
        {
            Url = url;
            CheckExt = ext;
            IncludeSubdomains = includeSubdomains;
            Transform = null;
        }

        public bool Matches(string host)
        {
            if (string.IsNullOrEmpty(host))
                return false;

            host = host.TrimEnd('.');
            return host.Equals(Url, StringComparison.OrdinalIgnoreCase)
                || (IncludeSubdomains && host.EndsWith($".{Url}", StringComparison.OrdinalIgnoreCase));
        }
    }
}