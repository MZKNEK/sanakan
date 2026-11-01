#pragma warning disable 1591

using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Sanakan.Services;

namespace Sanakan.ShindenCompat
{
    // Sees every attempt, retries included, so the health stats count real traffic to Shinden.
    public class ShindenActivityHandler : DelegatingHandler
    {
        private readonly ShindenActivity _activity;

        public ShindenActivityHandler(ShindenActivity activity, HttpMessageHandler inner = null)
            : base(inner ?? new HttpClientHandler
            {
                // the library attaches PHP session cookies itself
                UseCookies = false,
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            })
        {
            _activity = activity;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var timer = Stopwatch.StartNew();
            try
            {
                var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
                _activity.RequestCompleted(response.StatusCode, timer.ElapsedMilliseconds);
                return response;
            }
            catch (OperationCanceledException)
            {
                _activity.RequestFailed(true);
                throw;
            }
            catch
            {
                _activity.RequestFailed(false);
                throw;
            }
        }
    }
}
