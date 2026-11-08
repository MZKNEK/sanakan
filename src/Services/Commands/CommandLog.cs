#pragma warning disable 1591

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Discord.Commands;

namespace Sanakan.Services.Commands
{
    // One line per command, written when Discord.Net reports the result; async commands report their exceptions only this way.
    public static class CommandLog
    {
        private static readonly ConditionalWeakTable<ICommandContext, Stopwatch> _started = new ConditionalWeakTable<ICommandContext, Stopwatch>();

        public static void Started(ICommandContext context)
        {
            if (context != null)
                _started.AddOrUpdate(context, Stopwatch.StartNew());
        }

        public static void Finished(ILogger logger, string command, ICommandContext context, IResult result)
        {
            long ms = -1;
            if (context != null && _started.TryGetValue(context, out var watch))
            {
                ms = watch.ElapsedMilliseconds;
                _started.Remove(context);
            }

            var (isError, text) = Describe(command, context?.User?.Id ?? 0, ms, result);
            if (isError) logger.LogError(text);
            else logger.LogRoutine(text);
        }

        public static (bool IsError, string Text) Describe(string command, ulong userId, long ms, IResult result)
        {
            var head = ms >= 0 ? $"Cmd: u{userId} {command} ({ms}ms)" : $"Cmd: u{userId} {command}";
            if (result == null || result.IsSuccess)
                return (false, head);

            if (result is ExecuteResult execute && execute.Exception != null)
                return (true, $"{head} - {execute.Exception}");

            return (true, $"{head} - {result.Error}: {result.ErrorReason}");
        }
    }
}
