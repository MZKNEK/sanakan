#pragma warning disable 1591

using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Sanakan.Database
{
    public static class DbActivity
    {
        private static long _queries;
        private static long _writeCommands;
        private static long _saves;
        private static long _savedEntities;

        private static long _dayQueries;
        private static long _dayWriteCommands;
        private static long _daySaves;
        private static long _daySavedEntities;

        private static Timer _timer;
        private static DateTime _lastReport = DateTime.UtcNow;
        private static ILogger _logger;

        // over 1 s on the log channel, over 5 s also pinged
        public static readonly Services.SlowLog Slow = new Services.SlowLog(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(10));
        private const int SqlInLog = 300;
        private static readonly Lock _slowLock = new();
        private static long _slowQueries;
        private static long _slowestMs;
        private static string _slowestSql;

        private static readonly Services.RecentLatency _recent = new Services.RecentLatency();

        public static readonly Services.MinuteStats Stats = new Services.MinuteStats();

        public static readonly DbCommandInterceptor Commands = new CommandCounter();
        public static readonly SaveChangesInterceptor Saves = new SaveCounter();

        public static void Start(ILogger logger, TimeSpan interval)
        {
            _logger = logger;
            _lastReport = DateTime.UtcNow;
            _timer = new Timer(_ =>
            {
                var report = Flush();
                if (report != null) logger.LogReport(report);
            }, null, interval, interval);
        }

        public static string Flush()
        {
            var queries = Interlocked.Exchange(ref _queries, 0);
            var writes = Interlocked.Exchange(ref _writeCommands, 0);
            var saves = Interlocked.Exchange(ref _saves, 0);
            var entities = Interlocked.Exchange(ref _savedEntities, 0);

            var now = DateTime.UtcNow;
            var seconds = Math.Max(1, (now - _lastReport).TotalSeconds);
            _lastReport = now;

            long slow, slowestMs;
            string slowestSql;
            lock (_slowLock)
            {
                slow = _slowQueries;
                slowestMs = _slowestMs;
                slowestSql = _slowestSql;
                _slowQueries = 0;
                _slowestMs = 0;
                _slowestSql = null;
            }

            if (queries == 0 && writes == 0 && saves == 0)
                return null;

            var report = System.FormattableString.Invariant($"DB ({seconds / 60:0} min): {queries} queries ({queries / seconds:0.0}/s), ")
                + System.FormattableString.Invariant($"{saves} saves ({saves / seconds:0.0}/s, {entities} entities), {writes} write commands");
            if (slow > 0)
                report += System.FormattableString.Invariant($", {slow} over {Slow.LogAfter.TotalSeconds:0} s, slowest {slowestMs}ms: {slowestSql}");
            return report;
        }

        public static string DescribeSql(string sql)
        {
            var text = System.Text.RegularExpressions.Regex.Replace(sql ?? "", @"\s+", " ").Trim();
            return text.Length > SqlInLog ? text[..SqlInLog] + "…" : text;
        }

        private static void CheckSlow(DbCommand command, TimeSpan duration)
        {
            if (!Slow.IsSlow(duration))
                return;

            var sql = DescribeSql(command?.CommandText);
            var ms = (long)duration.TotalMilliseconds;
            lock (_slowLock)
            {
                _slowQueries++;
                if (ms > _slowestMs)
                {
                    _slowestMs = ms;
                    _slowestSql = sql;
                }
            }

            // queries differing only in their parameters share the alert
            Slow.Record(_logger, sql.Length > 80 ? sql[..80] : sql, duration, () => $"DB: slow query ({ms}ms): {sql}");
        }

        public static string FlushDaily()
        {
            var queries = Interlocked.Exchange(ref _dayQueries, 0);
            var writes = Interlocked.Exchange(ref _dayWriteCommands, 0);
            var saves = Interlocked.Exchange(ref _daySaves, 0);
            var entities = Interlocked.Exchange(ref _daySavedEntities, 0);

            return $"DB: {queries} queries, {saves} saves ({entities} entities), {writes} write commands";
        }

        public static long? GetRecentLatencyMs() => _recent.Get();

        private static void CountLatency(TimeSpan duration)
        {
            var ms = (long)duration.TotalMilliseconds;
            Stats.AddTimed(ms);
            _recent.Success(ms);
        }

        private static void CountFailure()
        {
            Stats.Add(true);
            _recent.Failure();
        }

        private static void CountQuery(TimeSpan duration)
        {
            CountLatency(duration);
            Interlocked.Increment(ref _queries);
            Interlocked.Increment(ref _dayQueries);
        }

        private static void CountWriteCommand(TimeSpan duration)
        {
            CountLatency(duration);
            Interlocked.Increment(ref _writeCommands);
            Interlocked.Increment(ref _dayWriteCommands);
        }

        private class CommandCounter : DbCommandInterceptor
        {
            public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
            {
                CountQuery(eventData.Duration);
                CheckSlow(command, eventData.Duration);
                return result;
            }

            public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
                DbDataReader result, CancellationToken cancellationToken = default)
            {
                CountQuery(eventData.Duration);
                CheckSlow(command, eventData.Duration);
                return new ValueTask<DbDataReader>(result);
            }

            public override object ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object result)
            {
                CountQuery(eventData.Duration);
                CheckSlow(command, eventData.Duration);
                return result;
            }

            public override ValueTask<object> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
                object result, CancellationToken cancellationToken = default)
            {
                CountQuery(eventData.Duration);
                CheckSlow(command, eventData.Duration);
                return new ValueTask<object>(result);
            }

            public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
            {
                CountWriteCommand(eventData.Duration);
                CheckSlow(command, eventData.Duration);
                return result;
            }

            public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
                int result, CancellationToken cancellationToken = default)
            {
                CountWriteCommand(eventData.Duration);
                CheckSlow(command, eventData.Duration);
                return new ValueTask<int>(result);
            }

            public override void CommandFailed(DbCommand command, CommandErrorEventData eventData) => CountFailure();

            public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData,
                CancellationToken cancellationToken = default)
            {
                CountFailure();
                return Task.CompletedTask;
            }
        }

        private class SaveCounter : SaveChangesInterceptor
        {
            public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
            {
                Count(result);
                return result;
            }

            public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
                CancellationToken cancellationToken = default)
            {
                Count(result);
                return new ValueTask<int>(result);
            }

            private static void Count(int entities)
            {
                if (entities <= 0) return;

                Interlocked.Increment(ref _saves);
                Interlocked.Add(ref _savedEntities, entities);
                Interlocked.Increment(ref _daySaves);
                Interlocked.Add(ref _daySavedEntities, entities);
            }
        }
    }
}
