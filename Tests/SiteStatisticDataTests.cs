using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Sanakan.Config.Model;
using Sanakan.ShindenCompat;
using Xunit;

namespace Artifacts
{
    public class SiteStatisticDataTests
    {
        private const string UserInfo = @"{ ""user_id"": ""42"", ""name"": ""Sniku"", ""rank"": ""Admin"", ""vb_id"": ""7"", ""avatar"": ""3"",
            ""watched_cnt"": ""1200"", ""chapters_cnt"": ""340"" }";
        private const string LastWatched = @"[ { ""title_id"": ""100"", ""episode_id"": ""8"", ""episode_no"": ""3"", ""episodes"": ""25"", ""title"": ""Code Geass"", ""cover_artifact_id"": ""777"" } ]";
        private const string LastRead = @"[ { ""title_id"": ""5"", ""chapter_id"": ""9"", ""chapter_no"": ""12"", ""chapters"": ""200"", ""title"": ""Solo"" } ]";

        // answers only when all expected requests are in flight together, so sequential calls would time out
        private class GateHandler : HttpMessageHandler
        {
            private readonly int _expected;
            private readonly TaskCompletionSource _allIn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            private int _inFlight;
            public HttpStatusCode InfoCode { get; set; } = HttpStatusCode.OK;

            public GateHandler(int expected) => _expected = expected;

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (Interlocked.Increment(ref _inFlight) == _expected)
                    _allIn.TrySetResult();

                // generous, because a busy CI runner can start the parallel requests seconds apart
                await _allIn.Task.WaitAsync(TimeSpan.FromSeconds(30));

                var path = request.RequestUri.AbsolutePath;
                var (json, code) = path.EndsWith("/info") ? (UserInfo, InfoCode)
                    : path.EndsWith("/last_view") ? (LastWatched, HttpStatusCode.OK)
                    : (LastRead, HttpStatusCode.OK);
                return new HttpResponseMessage(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            }
        }

        private static Sanakan.Services.Shinden Service(HttpMessageHandler handler)
        {
            var client = ShindenClient.Create(new ConfigShinden
            {
                Token = "old-php-key",
                ApiToken = "shk_new",
                UserAgent = "tests",
                Marmolade = "X-Marmolade",
            }, null, handler, TimeSpan.FromSeconds(60));
            return new Sanakan.Services.Shinden(client, null, null, new ListLogger(), new FixedTime());
        }

        [Fact]
        public async Task SiteStatisticData_IsFetchedInParallel_AndMapped()
        {
            var (info, read, watched) = await Service(new GateHandler(3)).GetSiteStatisticDataAsync(42);

            Assert.Equal("Sniku", info.Name);
            Assert.Equal(1200ul, info.ListStats.EpisodesCount);
            Assert.Equal("Solo: 12/200", Assert.Single(read).ToString());
            Assert.Equal("Code Geass: 3/25", Assert.Single(watched).ToString());
        }

        [Fact]
        public async Task SiteStatisticData_MissingUserThrowsWithStatusCode()
        {
            var handler = new GateHandler(3) { InfoCode = HttpStatusCode.NotFound };

            var ex = await Assert.ThrowsAsync<Exception>(() => Service(handler).GetSiteStatisticDataAsync(42));
            Assert.Equal(HttpStatusCode.NotFound.ToString(), ex.Message);
        }
    }
}
