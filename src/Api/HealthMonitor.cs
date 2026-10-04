#pragma warning disable 1591

using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Sanakan.Api.Models;
using Sanakan.Config;
using Sanakan.Services;
using Shinden;

namespace Sanakan.Api
{
    public class HealthMonitor
    {
        public const string Ok = "ok";
        public const string Degraded = "degraded";
        public const string Down = "down";

        private const int HighLatencyMs = 1000;
        private const string ShindenProbeUser = "sniku";
        private static readonly TimeSpan ProbeCacheTime = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan ResponseCacheTime = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

        private readonly DiscordSocketClient _client;
        private readonly Cached<HealthProbe> _database;
        private readonly Cached<HealthProbe> _shinden;
        private readonly Cached<HealthStatus> _response;
        private readonly DateTime _startedAt;
        private readonly string _version;
        private long _connectedAtTicks;

        public HealthMonitor(DiscordSocketClient client, ShindenClient shinden, IConfig config)
        {
            _client = client;
            _version = GetVersion();
            using (var proc = Process.GetCurrentProcess())
                _startedAt = proc.StartTime.ToUniversalTime();

            _database = new Cached<HealthProbe>(() => ProbeAsync(async token =>
            {
                using var db = new Database.DatabaseContext(config);
                await db.Database.ExecuteSqlRawAsync("SELECT 1", token);
                return true;
            }), ProbeCacheTime);
            _shinden = new Cached<HealthProbe>(() => ProbeAsync(async _
                => (await shinden.Search.UserAsync(ShindenProbeUser)).IsSuccessStatusCode()), ProbeCacheTime);
            _response = new Cached<HealthStatus>(BuildAsync, ResponseCacheTime);

            client.Connected += () => { Volatile.Write(ref _connectedAtTicks, DateTime.UtcNow.Ticks); return Task.CompletedTask; };
            client.Disconnected += _ => { Volatile.Write(ref _connectedAtTicks, 0); return Task.CompletedTask; };
        }

        public Task<HealthStatus> GetAsync() => _response.GetAsync();

        private async Task<HealthStatus> BuildAsync()
        {
            var shindenActivity = ShindenActivity.Default;
            var shindenTask = GetRecentOrProbeAsync(shindenActivity.Recent.Get(), _shinden);
            var database = await GetRecentOrProbeAsync(Database.DbActivity.GetRecentLatencyMs(), _database);
            var shinden = await shindenTask;

            var state = _client.ConnectionState;
            var latency = _client.Latency;
            var guilds = _client.Guilds;
            var connectedAt = Volatile.Read(ref _connectedAtTicks);
            var last5min = MinuteStats.Commands.Get(5);
            var lastHour = MinuteStats.Commands.Get(60);
            var rejected5min = MinuteStats.RejectedCommands.Get(5);
            var rejectedHour = MinuteStats.RejectedCommands.Get(60);
            var database5min = Database.DbActivity.Stats.Get(5);
            var shinden5min = shindenActivity.Requests.Get(5);
            var shindenHour = shindenActivity.Requests.Get(60);

            return new HealthStatus
            {
                Status = GetStatus(state, latency, database.Ok, shinden.Ok, rejected5min.Total),
                Version = _version,
                StartedAt = _startedAt,
                Discord = new HealthDiscord
                {
                    State = state.ToString(),
                    LatencyMs = latency,
                    ConnectedAt = state == ConnectionState.Connected && connectedAt > 0 ? new DateTime(connectedAt, DateTimeKind.Utc) : null,
                    Guilds = guilds.Count,
                    Members = guilds.Sum(x => x.MemberCount),
                },
                Database = new HealthDatabase
                {
                    Ok = database.Ok,
                    LatencyMs = database.LatencyMs,
                    Queries5Min = database5min.Total,
                    Errors5Min = database5min.Errors,
                    AvgMs5Min = database5min.AvgMs,
                    MaxMs5Min = database5min.MaxMs,
                },
                Shinden = new HealthShinden
                {
                    Ok = shinden.Ok,
                    LatencyMs = shinden.LatencyMs,
                    Requests5Min = shinden5min.Total,
                    Errors5Min = shinden5min.Errors,
                    ErrorRate5Min = shinden5min.ErrorRate,
                    Timeouts5Min = shindenActivity.Timeouts.Get(5).Total,
                    RequestsHour = shindenHour.Total,
                    ErrorsHour = shindenHour.Errors,
                    ErrorRateHour = shindenHour.ErrorRate,
                    TimeoutsHour = shindenActivity.Timeouts.Get(60).Total,
                },
                Commands = new HealthCommands
                {
                    Last5Min = last5min.Total,
                    Errors5Min = last5min.Errors,
                    LastHour = lastHour.Total,
                    ErrorsHour = lastHour.Errors,
                    Rejected5Min = rejected5min.Total,
                    RejectedHour = rejectedHour.Total,
                },
            };
        }

        public static string GetStatus(ConnectionState state, int latencyMs, bool databaseOk, bool shindenOk, int rejected5min)
        {
            if (state != ConnectionState.Connected) return Down;
            if (!databaseOk || !shindenOk || latencyMs > HighLatencyMs || rejected5min > 0) return Degraded;
            return Ok;
        }

        private static Task<HealthProbe> GetRecentOrProbeAsync(long? recentLatencyMs, Cached<HealthProbe> probe)
            => recentLatencyMs.HasValue
                ? Task.FromResult(new HealthProbe { Ok = true, LatencyMs = recentLatencyMs.Value })
                : probe.GetAsync();

        private static async Task<HealthProbe> ProbeAsync(Func<CancellationToken, Task<bool>> check)
        {
            var watch = Stopwatch.StartNew();
            bool ok;
            try
            {
                using var cts = new CancellationTokenSource(ProbeTimeout);
                var task = check(cts.Token);
                if (await Task.WhenAny(task, Task.Delay(ProbeTimeout)) == task)
                {
                    ok = await task;
                }
                else
                {
                    ok = false;
                    _ = task.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
                }
            }
            catch
            {
                ok = false;
            }

            return new HealthProbe { Ok = ok, LatencyMs = watch.ElapsedMilliseconds };
        }

        private static string GetVersion()
        {
            var assembly = Assembly.GetEntryAssembly();
            var version = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? assembly?.GetName().Version?.ToString() ?? "unknown";

            var plus = version.IndexOf('+');
            if (plus >= 0 && version.Length - plus - 1 > 7)
                version = version.Substring(0, plus + 8);

            return version;
        }

        private class Cached<T>
        {
            private readonly Func<Task<T>> _factory;
            private readonly long _ttlMs;
            private readonly object _lock = new object();
            private Task<T> _task;
            private long _expiresAt;

            public Cached(Func<Task<T>> factory, TimeSpan ttl)
            {
                _factory = factory;
                _ttlMs = (long)ttl.TotalMilliseconds;
            }

            public Task<T> GetAsync()
            {
                lock (_lock)
                {
                    if (_task == null || (_task.IsCompleted && Environment.TickCount64 > Volatile.Read(ref _expiresAt)))
                        _task = RunAsync();

                    return _task;
                }
            }

            private async Task<T> RunAsync()
            {
                try
                {
                    return await _factory();
                }
                finally
                {
                    Volatile.Write(ref _expiresAt, Environment.TickCount64 + _ttlMs);
                }
            }
        }
    }
}
