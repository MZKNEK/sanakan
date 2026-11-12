#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Sanakan.Config;
using Sanakan.Config.Model;

namespace Sanakan.Services
{
    // Downloads from links given by players go through the host's proxy, so the server's own address is not shown to
    // whoever runs the linked site. Off while FetchProxy.Url is empty.
    public class FetchProxy
    {
        public static readonly IReadOnlyList<string> DefaultDirectHosts = new[] { "cdn.discordapp.com", "media.discordapp.net", "cdn.shinden.eu" };
        public const string KeyHeader = "X-Internal-Key";
        public const int MaxParallel = 4;
        public const int TimeoutMs = 8000;
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

        private readonly IConfig _config;
        private readonly ILogger _logger;
        private readonly HttpClient _http;
        private readonly SemaphoreSlim _slots = new SemaphoreSlim(MaxParallel);
        private volatile bool _failing;

        public FetchProxy(IConfig config, ILogger logger, HttpMessageHandler transport = null)
        {
            _config = config;
            _logger = logger;
            _http = new HttpClient(transport ?? new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }) { Timeout = RequestTimeout };
        }

        public HttpMessageHandler CreateHandler(HttpMessageHandler direct = null) => new Handler(this, direct ?? new HttpClientHandler());

        public static bool IsDirect(string host, FetchProxyConfig config)
        {
            var hosts = config?.DirectHosts ?? (IEnumerable<string>)DefaultDirectHosts;
            return !string.IsNullOrEmpty(host) && hosts.Any(x => !string.IsNullOrWhiteSpace(x)
                && (host.Equals(x.Trim(), StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + x.Trim(), StringComparison.OrdinalIgnoreCase)));
        }

        public static Uri Mirrored(Uri uri, FetchProxyConfig config)
        {
            if (uri == null || config?.Mirrors == null)
                return null;

            var mirror = config.Mirrors.FirstOrDefault(x => uri.Host.Equals(x.Key?.Trim(), StringComparison.OrdinalIgnoreCase)).Value;
            if (string.IsNullOrWhiteSpace(mirror) || !Uri.TryCreate(mirror.Trim().TrimEnd('/') + "/" + uri.PathAndQuery.TrimStart('/'), UriKind.Absolute, out var mirrored))
                return null;

            return mirrored;
        }

        private sealed class Handler : DelegatingHandler
        {
            private readonly FetchProxy _proxy;

            public Handler(FetchProxy proxy, HttpMessageHandler direct) : base(direct) => _proxy = proxy;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var cfg = _proxy._config.Get()?.FetchProxy;
                var mirrored = Mirrored(request.RequestUri, cfg);
                if (mirrored != null)
                {
                    request.RequestUri = mirrored;
                    return base.SendAsync(request, cancellationToken);
                }

                if (string.IsNullOrWhiteSpace(cfg?.Url) || IsDirect(request.RequestUri?.Host, cfg))
                    return base.SendAsync(request, cancellationToken);

                return _proxy.FetchAsync(request, cfg, cancellationToken);
            }
        }

        private sealed class FetchResult
        {
            [JsonProperty("status")] public int Status { get; set; }
            [JsonProperty("content_type")] public string ContentType { get; set; }
            [JsonProperty("body")] public string Body { get; set; }
            [JsonProperty("truncated")] public bool Truncated { get; set; }
        }

        private sealed class FetchError
        {
            [JsonProperty("code")] public string Code { get; set; }
            [JsonProperty("retry_after")] public int RetryAfter { get; set; }
        }

        private async Task<HttpResponseMessage> FetchAsync(HttpRequestMessage request, FetchProxyConfig cfg, CancellationToken cancellationToken)
        {
            // only downloads are expected here; anything else is not sent at all rather than sent directly
            if (request.Method != HttpMethod.Get)
                throw new HttpRequestException($"fetch proxy: {request.Method} is not supported");

            var body = JsonConvert.SerializeObject(new
            {
                url = request.RequestUri.AbsoluteUri,
                method = "GET",
                kind = "image",
                max_bytes = cfg.MaxBytes,
                timeout_ms = TimeoutMs,
                purpose = "sanakan",
                any = true,
            });

            await _slots.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using var message = new HttpRequestMessage(HttpMethod.Post, cfg.Url) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
                message.Headers.TryAddWithoutValidation(KeyHeader, cfg.Key ?? "");

                using var response = await _http.SendAsync(message, cancellationToken).ConfigureAwait(false);
                var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    return ProxyError(request, response.StatusCode, text);

                var result = JsonConvert.DeserializeObject<FetchResult>(text);
                if (result == null || result.Status < 100 || result.Status > 599)
                    throw new JsonException("no status in the answer");

                Works();
                if (result.Truncated)
                    return Answer(request, HttpStatusCode.RequestEntityTooLarge);

                var answer = Answer(request, (HttpStatusCode)result.Status, string.IsNullOrEmpty(result.Body) ? Array.Empty<byte>() : Convert.FromBase64String(result.Body));
                if (MediaTypeHeaderValue.TryParse(result.ContentType, out var type))
                    answer.Content.Headers.ContentType = type;
                return answer;
            }
            catch (Exception ex) when (ex is JsonException or FormatException
                || (ex is HttpRequestException or OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                var reason = ex is OperationCanceledException ? "timeout" : ex.Message;
                Failed(reason);
                throw new HttpRequestException($"fetch proxy: {reason}", ex);
            }
            finally
            {
                _slots.Release();
            }
        }

        private HttpResponseMessage ProxyError(HttpRequestMessage request, HttpStatusCode status, string text)
        {
            FetchError error = null;
            try { error = JsonConvert.DeserializeObject<FetchError>(text); } catch (JsonException) { }

            // a wrong key, a wrong address or no way out stops every download, the others are about the one link
            var code = (int)status;
            if (status is HttpStatusCode.Unauthorized or HttpStatusCode.NotFound or HttpStatusCode.Gone || code == 500 || code == 503)
                Failed($"HTTP {code} {error?.Code}".TrimEnd());
            else
                Works();

            // a 404 or 410 means "the picture is gone" to the callers, which the proxy itself never says
            var answer = Answer(request, status is HttpStatusCode.NotFound or HttpStatusCode.Gone ? HttpStatusCode.BadGateway : status);
            if (error?.RetryAfter > 0)
                answer.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(error.RetryAfter));
            return answer;
        }

        private static HttpResponseMessage Answer(HttpRequestMessage request, HttpStatusCode status, byte[] body = null)
            => new HttpResponseMessage(status) { RequestMessage = request, Content = new ByteArrayContent(body ?? Array.Empty<byte>()) };

        private void Works()
        {
            if (!_failing) return;
            _failing = false;
            _logger?.Log("Fetch proxy: works again");
        }

        // only a change is logged, a longer outage would flood the log channel
        private void Failed(string reason)
        {
            if (_failing) return;
            _failing = true;
            _logger?.LogError($"Fetch proxy: downloads from players' links fail: {reason}");
        }
    }
}
