#pragma warning disable 1591

using System;
using System.Diagnostics;
using System.Net;
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
    // Co minutę wysyła stan bota na zewnętrzny adres - działa też wtedy, gdy API bota jest nieosiągalne z zewnątrz.
    public class Heartbeat
    {
        public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan FirstDelay = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);
        private const int Attempts = 3;

        private static readonly JsonSerializerSettings _json = new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() },
            Converters = { new StringEnumConverter { NamingStrategy = new CamelCaseNamingStrategy() } },
        };

        private readonly Func<Task<HealthStatus>> _status;
        private readonly IConfig _config;
        private readonly ILogger _logger;
        private readonly HttpClient _http;
        private readonly Func<TimeSpan, Task> _delay;
        private Timer _timer;
        private volatile bool _failing;
        private long _lastSuccessTicks;
        private volatile string _lastError;

        public Heartbeat(Func<Task<HealthStatus>> status, IConfig config, ILogger logger, HttpMessageHandler handler = null, Func<TimeSpan, Task> delay = null)
        {
            _status = status;
            _config = config;
            _logger = logger;
            _delay = delay ?? (x => Task.Delay(x));
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

        // kolejne wysłanie minutę po początku poprzedniego, niezależnie od ponowień
        private async Task TickAsync()
        {
            var watch = Stopwatch.StartNew();
            try
            {
                await SendAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Failed($"heartbeat: {ex.Message}");
            }
            finally
            {
                var next = Interval - watch.Elapsed;
                _timer?.Change(next > TimeSpan.FromSeconds(1) ? next : TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
            }
        }

        // false - wyłączony w konfiguracji lub wysłanie się nie udało mimo ponowień
        public async Task<bool> SendAsync()
        {
            var cfg = _config.Get()?.Heartbeat;
            if (string.IsNullOrWhiteSpace(cfg?.Url))
                return false;

            string body;
            string version;
            try
            {
                var status = await _status().ConfigureAwait(false);
                body = JsonConvert.SerializeObject(status, _json);
                version = string.IsNullOrEmpty(status?.Version) ? "unknown" : status.Version;
            }
            catch (Exception ex)
            {
                return Failed($"stan bota: {ex.Message}");
            }

            string reason = null;
            for (int attempt = 1; attempt <= Attempts; attempt++)
            {
                if (attempt > 1)
                    await _delay(RetryDelay).ConfigureAwait(false);

                bool retry;
                (reason, retry) = await TrySendAsync(cfg.Url, cfg.Secret, body, version).ConfigureAwait(false);
                if (reason == null)
                    return Succeeded();
                if (!retry)
                    break;
            }

            return Failed(reason);
        }

        // (null, _) - wysłane; (powód, czy ponowić)
        private async Task<(string Reason, bool Retry)> TrySendAsync(string url, string secret, string body, string version)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                };
                request.Headers.TryAddWithoutValidation("User-Agent", $"SanakanBot/{version} (heartbeat; +https://sanakan.pl)");
                if (!string.IsNullOrEmpty(secret))
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);

                using var response = await _http.SendAsync(request).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                    return (null, false);

                var code = (int)response.StatusCode;
                // błędy serwera (też 52x Cloudflare) mijają, zły sekret czy adres - nie
                return ($"odpowiedź {code}", code >= 500 || response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests);
            }
            catch (Exception ex)
            {
                return (ex is OperationCanceledException ? "timeout" : ex.Message, true);
            }
        }

        private bool Succeeded()
        {
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
    }
}
