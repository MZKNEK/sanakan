#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Sanakan.Config;
using Sanakan.Config.Model;
using Shinden.Logger;

namespace Sanakan.Services
{
    // Forwards new messages from selected channels to an external endpoint (Shinden), signed with HMAC.
    public class DiscordIngest
    {
        public const int MaxBatch = 50;
        public const int MaxBodyBytes = 256 * 1024;
        public const int BackfillCount = 100;
        public const int Retries = 6;
        public static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(5);
        private const int MaxQueue = 2000;
        private const string BodyStart = "{\"messages\":[";
        private const string BodyEnd = "]}";
        private static readonly TimeSpan CollectDelay = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

        private readonly DiscordSocketClient _client;
        private readonly IConfig _config;
        private readonly ILogger _logger;
        private readonly HttpClient _http;
        private readonly Func<TimeSpan, Task> _delay;
        private readonly Func<DateTimeOffset> _now;
        private readonly Channel<string> _queue;
        private readonly string _userAgent;
        private string _carry;
        private volatile bool _failing;
        private volatile bool _overflowLogged;
        private long _lastSuccessTicks;
        private volatile string _lastError;

        public DiscordIngest(DiscordSocketClient client, IConfig config, ILogger logger, HttpMessageHandler handler = null,
            Func<TimeSpan, Task> delay = null, Func<DateTimeOffset> now = null)
        {
            _client = client;
            _config = config;
            _logger = logger;
            _delay = delay ?? (x => Task.Delay(x));
            _now = now ?? (() => DateTimeOffset.UtcNow);
            _http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = RequestTimeout };
            _userAgent = $"SanakanBot/{Api.HealthMonitor.GetVersion()} (ingest; +https://sanakan.pl)";
            _queue = Channel.CreateBounded<string>(new BoundedChannelOptions(MaxQueue)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
            }, _ => Overflowed());

            if (_client != null)
            {
                _client.MessageReceived += HandleMessageAsync;
                _client.Ready += HandleReadyAsync;
            }
        }

        public bool Enabled => IsEnabled(_config.Get()?.Ingest);
        public bool Failing => _failing;
        public string LastError => _lastError;
        public int Queued => _queue.Reader.Count + (_carry != null ? 1 : 0);
        public DateTime? LastSuccess
        {
            get
            {
                var ticks = Interlocked.Read(ref _lastSuccessTicks);
                return ticks > 0 ? new DateTime(ticks, DateTimeKind.Utc) : null;
            }
        }

        public void Start() => _ = Task.Run(RunAsync);

        public bool Enqueue(IMessage msg)
        {
            var cfg = _config.Get()?.Ingest;
            if (!IsEnabled(cfg) || msg?.Channel == null || cfg.Channels?.Contains(msg.Channel.Id) != true)
                return false;

            return _queue.Writer.TryWrite(IngestMessage.Serialize(msg));
        }

        public async Task<int> BackfillAsync(ulong channelId)
        {
            if (_client?.GetChannel(channelId) is not SocketTextChannel channel || channel is SocketThreadChannel)
                return 0;

            var messages = await channel.GetMessagesAsync(BackfillCount).FlattenAsync().ConfigureAwait(false);
            return messages.OrderBy(x => x.Id).Count(Enqueue);
        }

        public async Task<int> BackfillAllAsync()
        {
            var cfg = _config.Get()?.Ingest;
            if (!IsEnabled(cfg) || cfg.Channels == null)
                return 0;

            int count = 0;
            foreach (var id in cfg.Channels.ToList())
            {
                try
                {
                    count += await BackfillAsync(id).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Ingest: pobranie ostatnich wiadomości z kanału {id}: {ex.Message}");
                }
            }
            return count;
        }

        // Sends everything queued so far in batches; the background loop calls it after each new message.
        public async Task FlushAsync()
        {
            List<string> batch;
            while ((batch = TakeBatch()).Count > 0)
            {
                await SendAsync(batch).ConfigureAwait(false);
                await _delay(MinInterval).ConfigureAwait(false);
            }
        }

        public static string Sign(string secret, string timestamp, string body)
            => Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}"))).ToLowerInvariant();

        private static bool IsEnabled(IngestConfig cfg)
            => !string.IsNullOrWhiteSpace(cfg?.Url) && !string.IsNullOrEmpty(cfg.Secret);

        private Task HandleMessageAsync(SocketMessage message)
        {
            try
            {
                Enqueue(message);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Ingest: wiadomość {message?.Id}: {ex.Message}");
            }
            return Task.CompletedTask;
        }

        // Ready fires after every full reconnect, so this also fills gaps after downtime; the receiver drops duplicates.
        private Task HandleReadyAsync()
        {
            _ = Task.Run(BackfillAllAsync);
            return Task.CompletedTask;
        }

        private async Task RunAsync()
        {
            while (true)
            {
                try
                {
                    await _queue.Reader.WaitToReadAsync().ConfigureAwait(false);
                    await _delay(CollectDelay).ConfigureAwait(false);
                    await FlushAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Failed($"ingest: {ex.Message}");
                    await _delay(MinInterval).ConfigureAwait(false);
                }
            }
        }

        private List<string> TakeBatch()
        {
            var batch = new List<string>();
            var size = Encoding.UTF8.GetByteCount(BodyStart + BodyEnd);

            while (batch.Count < MaxBatch)
            {
                var next = _carry;
                _carry = null;
                if (next == null && !_queue.Reader.TryRead(out next))
                    break;

                var bytes = Encoding.UTF8.GetByteCount(next) + (batch.Count > 0 ? 1 : 0);
                if (size + bytes > MaxBodyBytes)
                {
                    if (batch.Count == 0)
                    {
                        _logger.LogError($"Ingest: pominięto wiadomość większą niż {MaxBodyBytes / 1024} KB.");
                        continue;
                    }
                    _carry = next;
                    break;
                }

                batch.Add(next);
                size += bytes;
            }
            return batch;
        }

        private async Task<bool> SendAsync(List<string> batch)
        {
            var body = BodyStart + string.Join(",", batch) + BodyEnd;

            string reason = null;
            TimeSpan? wait = null;
            for (int attempt = 0; attempt <= Retries; attempt++)
            {
                if (attempt > 0)
                    await _delay(wait ?? RetryDelay).ConfigureAwait(false);

                var cfg = _config.Get()?.Ingest;
                if (!IsEnabled(cfg))
                    return false;

                bool retry;
                (reason, retry, wait) = await TrySendAsync(cfg, body).ConfigureAwait(false);
                if (reason == null)
                    return Succeeded();

                Failed(reason);
                if (!retry)
                    break;
            }

            _logger.LogError($"Ingest: porzucono {batch.Count} wiadomości: {reason}");
            return false;
        }

        // (null, _, _) - sent; otherwise (reason, whether to retry, server-requested wait)
        private async Task<(string Reason, bool Retry, TimeSpan? Wait)> TrySendAsync(IngestConfig cfg, string body)
        {
            try
            {
                // a fresh timestamp and signature on every attempt - the receiver rejects ones older than 5 minutes
                var timestamp = _now().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
                using var request = new HttpRequestMessage(HttpMethod.Post, cfg.Url)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                };
                request.Headers.TryAddWithoutValidation("User-Agent", _userAgent);
                request.Headers.TryAddWithoutValidation("X-Timestamp", timestamp);
                request.Headers.TryAddWithoutValidation("X-Signature", "sha256=" + Sign(cfg.Secret, timestamp, body));

                using var response = await _http.SendAsync(request).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                    return (null, false, null);

                var code = (int)response.StatusCode;
                var retry = code >= 500 || response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests;
                return ($"odpowiedź {code}", retry, RetryAfter(response));
            }
            catch (Exception ex)
            {
                return (ex is OperationCanceledException ? "timeout" : ex.Message, true, null);
            }
        }

        private TimeSpan? RetryAfter(HttpResponseMessage response)
        {
            var header = response.Headers.RetryAfter;
            var wait = header?.Delta ?? (header?.Date - _now());
            if (wait == null)
                return null;

            return wait < MinInterval ? MinInterval : (wait > RetryDelay ? RetryDelay : wait);
        }

        private void Overflowed()
        {
            if (_overflowLogged) return;
            _overflowLogged = true;
            _logger.LogError($"Ingest: kolejka pełna ({MaxQueue}), najstarsze wiadomości są pomijane.");
        }

        private bool Succeeded()
        {
            Interlocked.Exchange(ref _lastSuccessTicks, DateTime.UtcNow.Ticks);
            _lastError = null;
            _overflowLogged = false;
            if (_failing)
            {
                _failing = false;
                _logger.Log("Ingest: wysyłanie znowu działa.");
            }
            return true;
        }

        // Only state changes are logged so a longer outage does not flood the log channel.
        private bool Failed(string reason)
        {
            _lastError = reason;
            if (!_failing)
            {
                _failing = true;
                _logger.LogError($"Ingest: nie udało się wysłać wiadomości: {reason}");
            }
            return false;
        }
    }
}
