#pragma warning disable 1591

namespace Sanakan.Services
{
    public class DomainData
    {
        public string Url;
        public bool CheckExt;

        public DomainData(string url)
        {
            Url = url;
            CheckExt = true;
        }
    }
}