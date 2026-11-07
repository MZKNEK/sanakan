#pragma warning disable 1591

namespace Sanakan
{
    public static class LoggerExtensions
    {
        public const string ErrorPrefix = "[ERROR] ";
        public const string ReportPrefix = "[REPORT] ";

        public static void LogError(this ILogger logger, string message) => logger.Log(ErrorPrefix + message);

        public static void LogReport(this ILogger logger, string message) => logger.Log(ReportPrefix + message);
    }
}
