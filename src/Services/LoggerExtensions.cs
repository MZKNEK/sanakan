#pragma warning disable 1591

using System;

namespace Sanakan
{
    public enum LogKind
    {
        Info, Routine, Error, Report
    }

    public static class LoggerExtensions
    {
        public const string ErrorPrefix = "[ERROR] ";
        public const string ReportPrefix = "[REPORT] ";
        // routine entries (every command, executor task...) stay on the console and are not sent to the log channel
        public const string RoutinePrefix = "[ROUTINE] ";

        public static void LogError(this ILogger logger, string message) => logger.Log(ErrorPrefix + message);

        public static void LogReport(this ILogger logger, string message) => logger.Log(ReportPrefix + message);

        public static void LogRoutine(this ILogger logger, string message) => logger.Log(RoutinePrefix + message);

        public static (LogKind Kind, string Text) Parse(string message)
        {
            message ??= "";
            if (message.StartsWith(ErrorPrefix, StringComparison.Ordinal))
                return (LogKind.Error, message.Substring(ErrorPrefix.Length));
            if (message.StartsWith(ReportPrefix, StringComparison.Ordinal))
                return (LogKind.Report, message.Substring(ReportPrefix.Length));
            if (message.StartsWith(RoutinePrefix, StringComparison.Ordinal))
                return (LogKind.Routine, message.Substring(RoutinePrefix.Length));
            return (LogKind.Info, message);
        }

        public static string Label(LogKind kind) => kind switch
        {
            LogKind.Error => "ERROR ",
            LogKind.Report => "REPORT ",
            _ => ""
        };
    }
}
