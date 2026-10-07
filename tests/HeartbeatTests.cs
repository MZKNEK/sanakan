using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Sanakan.Api.Models;
using Sanakan.Config.Model;
using Sanakan.Services;
using Xunit;

namespace Artifacts
{
    public class HeartbeatTests
    {
        private class FakeHandler : HttpMessageHandler
        {
            public List<(HttpRequestMessage Request, string Body)> Requests { get; } = new();
            public Func<HttpResponseMessage> Respond { get; set; } = () => new HttpResponseMessage(HttpStatusCode.OK);
            public Queue<Func<HttpResponseMessage>> Next { get; } = new();

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Requests.Add((request, await request.Content.ReadAsStringAsync()));
                return Next.Count > 0 ? Next.Dequeue()() : Respond();
            }
        }

        private readonly FakeHandler _handler = new FakeHandler();
        private readonly FakeConfig _config = new FakeConfig();
        private readonly ListLogger _logger = new ListLogger();

        private Heartbeat Create() => new Heartbeat(() => Task.FromResult(new HealthStatus
        {
            Status = "ok",
            Version = "1.2.3",
            Shinden = new HealthShinden { Ok = false, Requests5Min = 7 },
        }), _config, _logger, _handler, _ => Task.CompletedTask);

        [Fact]
        public async Task DisabledWithoutUrl()
        {
            Assert.False(await Create().SendAsync());
            _config.Model.Heartbeat = new HeartbeatConfig { Url = " " };
            Assert.False(await Create().SendAsync());
            Assert.Empty(_handler.Requests);
        }

        [Fact]
        public async Task PostsStatusWithSecret()
        {
            _config.Model.Heartbeat = new HeartbeatConfig { Url = "https://sanakan.pl/alive", Secret = "abc" };

            Assert.True(await Create().SendAsync());

            var (request, body) = Assert.Single(_handler.Requests);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://sanakan.pl/alive", request.RequestUri.ToString());
            Assert.Equal("Bearer", request.Headers.Authorization.Scheme);
            Assert.Equal("abc", request.Headers.Authorization.Parameter);

            Assert.StartsWith("SanakanBot/1.2.3 ", request.Headers.UserAgent.ToString());

            var json = JObject.Parse(body);
            Assert.Equal("ok", (string)json["status"]);
            Assert.Equal("1.2.3", (string)json["version"]);
            Assert.False((bool)json["shinden"]["ok"]);
            Assert.Equal(7, (int)json["shinden"]["requests5min"]);
        }

        [Fact]
        public async Task NoAuthorizationWithoutSecret()
        {
            _config.Model.Heartbeat = new HeartbeatConfig { Url = "https://sanakan.pl/alive" };

            Assert.True(await Create().SendAsync());
            Assert.Null(_handler.Requests.Single().Request.Headers.Authorization);
        }

        [Fact]
        public async Task RetriesUntilSiteAnswers()
        {
            _config.Model.Heartbeat = new HeartbeatConfig { Url = "https://sanakan.pl/alive/" };
            _handler.Next.Enqueue(() => throw new HttpRequestException("down"));
            _handler.Next.Enqueue(() => new HttpResponseMessage((HttpStatusCode)522));

            Assert.True(await Create().SendAsync());
            Assert.Equal(3, _handler.Requests.Count);
            Assert.Empty(_logger.Messages);
        }

        [Fact]
        public async Task GivesUpAfterThreeAttempts()
        {
            _config.Model.Heartbeat = new HeartbeatConfig { Url = "https://sanakan.pl/alive/" };
            _handler.Respond = () => new HttpResponseMessage(HttpStatusCode.BadGateway);

            Assert.False(await Create().SendAsync());
            Assert.Equal(3, _handler.Requests.Count);
        }

        [Fact]
        public async Task DoesNotRetryBadSecret()
        {
            _config.Model.Heartbeat = new HeartbeatConfig { Url = "https://sanakan.pl/alive/", Secret = "zly" };
            _handler.Respond = () => new HttpResponseMessage(HttpStatusCode.Unauthorized);

            var heartbeat = Create();
            Assert.False(await heartbeat.SendAsync());
            Assert.Single(_handler.Requests);
            Assert.Equal("odpowiedź 401", heartbeat.LastError);
        }

        [Fact]
        public async Task LogsOnlyStateChanges()
        {
            _config.Model.Heartbeat = new HeartbeatConfig { Url = "https://sanakan.pl/alive" };
            var heartbeat = Create();

            _handler.Respond = () => new HttpResponseMessage(HttpStatusCode.BadGateway);
            Assert.False(await heartbeat.SendAsync());
            Assert.False(await heartbeat.SendAsync());

            _handler.Respond = () => throw new HttpRequestException("down");
            Assert.False(await heartbeat.SendAsync());

            _handler.Respond = () => new HttpResponseMessage(HttpStatusCode.OK);
            Assert.True(await heartbeat.SendAsync());
            Assert.True(await heartbeat.SendAsync());

            var messages = _logger.Messages.ToList();
            Assert.Equal(2, messages.Count);
            Assert.Contains("502", messages[0]);
            Assert.Contains("znowu działa", messages[1]);
        }
    }
}
