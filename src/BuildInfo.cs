#pragma warning disable 1591

namespace Sanakan
{
    public static class BuildInfo
    {
        // a debug bot may run on the production database: it leaves out the background work that writes to it
        // not a const, so the code behind it builds without unreachable code warnings in either configuration
#if DEBUG
        public static readonly bool IsDebug = true;
#else
        public static readonly bool IsDebug = false;
#endif
    }
}
