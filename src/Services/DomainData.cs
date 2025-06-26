#pragma warning disable 1591

using System;
using System.Threading.Tasks;

namespace Sanakan.Services
{
    public class DomainData
    {
        public string Url;
        public bool CheckExt;
        public Func<string, Task<string>> Transform;

        public DomainData(string url, bool ext = false)
        {
            Url = url;
            CheckExt = ext;
            Transform = null;
        }
    }
}