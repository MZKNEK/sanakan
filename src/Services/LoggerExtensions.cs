#pragma warning disable 1591

using Shinden.Logger;

namespace Sanakan
{
    public static class LoggerExtensions
    {
        public const string ErrorPrefix = "[ERROR] ";

        public static void LogError(this ILogger logger, string message) => logger.Log(ErrorPrefix + message);
    }
}
