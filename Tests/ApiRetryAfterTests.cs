using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Sanakan;
using Sanakan.Api;
using Sanakan.Api.Controllers;
using Xunit;

namespace Artifacts
{
    public class ApiRetryAfterTests : IDisposable
    {
        private readonly ApiServer _api = new ApiServer();

        public void Dispose() => _api.Dispose();

        private static int RetryAfter(HttpResponseMessage res) => int.Parse(res.Headers.GetValues("Retry-After").Single());

        [Fact]
        public void Unavailable_SetsRetryAfter_AndWorksWithoutAResponse()
        {
            var ctx = new DefaultHttpContext();

            Assert.Equal(503, ApiResponses.QueueFull(ctx.Response).StatusCode);
            Assert.Equal(ApiResponses.QueueFullRetryAfter.ToString(), ctx.Response.Headers["Retry-After"].ToString());
            Assert.Equal(503, ApiResponses.ShindenUnavailable(null).StatusCode);

            ApiResponses.TooManyAttempts(ctx.Response, TimeSpan.FromMilliseconds(10));
            Assert.Equal("1", ctx.Response.Headers["Retry-After"].ToString());
        }

        [Fact]
        public async Task Token_Locked_Returns429WithRetryAfter()
        {
            for (int i = 0; i < TokenAttemptGuard.MaxAttempts; i++)
                await _api.Client.PostAsync("/api/token", ApiServer.Json("\"wrong\""));

            var res = await _api.Client.PostAsync("/api/token", ApiServer.Json("\"wrong\""));

            Assert.Equal(HttpStatusCode.TooManyRequests, res.StatusCode);
            Assert.InRange(RetryAfter(res), 1, (int)TimeSpan.FromDays(1).TotalSeconds);
        }

        [Fact]
        public async Task UserKey_Locked_Returns429WithRetryAfter()
        {
            HttpRequestMessage Request()
            {
                var req = new HttpRequestMessage(HttpMethod.Post, "/api/UserKey/discord/1");
                req.Headers.Add("x-app-key", "unknown-app-key");
                return req;
            }

            for (int i = 0; i < TokenAttemptGuard.MaxAttempts; i++)
                Assert.Equal(HttpStatusCode.Forbidden, (await _api.Client.SendAsync(Request())).StatusCode);

            var res = await _api.Client.SendAsync(Request());

            Assert.Equal(HttpStatusCode.TooManyRequests, res.StatusCode);
            Assert.True(RetryAfter(res) > 0);
        }

        // the caller gets its answer before the process ends, so it has no reason to send the request again
        [Theory]
        [InlineData("kill", ExitCodes.Stop)]
        [InlineData("update", ExitCodes.Update)]
        public async Task DebugStop_Answers202_ThenExits(string action, int exitCode)
        {
            var exited = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var exit = DebugController.Exit;
            var delay = DebugController.ExitDelay;
            DebugController.Exit = code => exited.TrySetResult(code);
            DebugController.ExitDelay = TimeSpan.FromMilliseconds(50);
            try
            {
                var client = await _api.AsSiteAsync();

                var res = await client.PostAsync($"/api/Debug/{action}", null);

                Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
                Assert.Equal(exitCode, await exited.Task.WaitAsync(TimeSpan.FromSeconds(10)));
            }
            finally
            {
                DebugController.Exit = exit;
                DebugController.ExitDelay = delay;
            }
        }
    }
}
