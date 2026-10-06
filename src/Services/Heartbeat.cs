#pragma warning disable 1591

using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using Sanakan.Api.Models;
using Sanakan.Config;
using Shinden.Logger;

namespace Sanakan.Services
{
    // Wysyła stan bota na zewnętrzny adres - działa też wtedy, gdy API bota jest nieosiągalne z zewnątrz.
    public class Heartbeat
    {
        public const int MinIntervalSeconds = 10;
        private static readonly TimeSpan FirstDelay = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan DisabledRecheck = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

        private static readonly JsonSerializerSettings _json = new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() },
            Converters = { new StringEnumConverter { NamingStrategy = new CamelCaseNamingStrategy() } },
        };

        private readonly Func<Task<HealthStatus>> _status;
        private readonly IConfig _config;
        private readonly ILogger _logger;
        private readonly HttpClient _http;
        private Timer _timer;
        private volatile bool _failing;
        private long _lastSuccessTicks;
        private volatile string _lastError;

        public Heartbeat(Func<Task<HealthStatus>> status, IConfig config, ILogger logger, HttpMessageHandler handler = null)
        {
            _status = status;
            _config = config;
            _logger = logger;
            _http = new HttpClient(handler ?? new HttpClientHandler()) { Timeout = RequestTimeout };
        }

        public bool Failing => _failing;
        public string LastError => _lastError;
        public DateTime? LastSuccess
        {
            get
            {
                var ticks = Interlocked.Read(ref _lastSuccessTicks);
                return ticks > 0 ? new DateTime(ticks, DateTimeKind.Utc) : null;
            }
        }

        public void Start()
        {
            _timer = new Timer(_ => _ = TickAsync(), null, FirstDelay, Timeout.InfiniteTimeSpan);
        }

        private async Task TickAsync()
        {
            try
            {
                await SendAsync().ConfigureAwait(false);
            }
            finally
            {
                _timer.Change(GetInterval(), Timeout.InfiniteTimeSpan);
            }
        }

        // false - wyłączony w konfiguracji lub wysłanie się nie udało
        public async Task<bool> SendAsync()
        {
            var cfg = _config.Get()?.Heartbeat;
            if (string.IsNullOrWhiteSpace(cfg?.Url))
                return false;

            try
            {
                var status = await _status().ConfigureAwait(false);
                using var request = new HttpRequestMessage(HttpMethod.Post, cfg.Url)
                {
                    Content = new StringContent(JsonConvert.SerializeObject(status, _json), Encoding.UTF8, "application/json"),
                };
                if (!string.IsNullOrEmpty(cfg.Secret))
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.Secret);

                using var response = await _http.SendAsync(request).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    return Failed($"odpowiedź {(int)response.StatusCode}");
            }
            catch (Exception ex)
            {
                return Failed(ex is OperationCanceledException ? "timeout" : ex.Message);
            }

            Interlocked.Exchange(ref _lastSuccessTicks, DateTime.UtcNow.Ticks);
            _lastError = null;
            if (_failing)
            {
                _failing = false;
                _logger.Log("Heartbeat: wysyłanie znowu działa.");
            }
            return true;
        }

        // Logujemy tylko zmianę stanu, żeby przy dłuższej awarii nie zasypać kanału logów.
        private bool Failed(string reason)
        {
            _lastError = reason;
            if (!_failing)
            {
                _failing = true;
                _logger.LogError($"Heartbeat: nie udało się wysłać stanu: {reason}");
            }
            return false;
        }

        private TimeSpan GetInterval()
        {
            var cfg = _config.Get()?.Heartbeat;
            if (string.IsNullOrWhiteSpace(cfg?.Url))
                return DisabledRecheck;

            return TimeSpan.FromSeconds(Math.Max(cfg.IntervalSeconds, MinIntervalSeconds));
        }
    }
}
