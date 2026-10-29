using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Sanakan.Config.Model;
using Sanakan.Services;
using Xunit;

namespace Artifacts
{
    public class DiscordIngestTests
    {
        private const string Secret = "0123456789abcdef0123456789abcdef0123456789abcdef";
        private const ulong Channel = 1565336313962565713;

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
        private readonly List<TimeSpan> _delays = new();
        private DateTimeOffset _now = new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

        public DiscordIngestTests()
        {
            _config.Model.Ingest = new IngestConfig { Url = "https://beta.shinden.pl/gosana/ingest/discord", Secret = Secret, Channels = new List<ulong> { Channel } };
        }

        private DiscordIngest Create() => new DiscordIngest(null, _config, _logger, _handler, x =>
        {
            _delays.Add(x);
            _now += x;
            return Task.CompletedTask;
        }, () => _now);

        private static Mock<IUserMessage> Msg(ulong id, ulong channel = Channel, string content = "hej")
        {
            var ch = new Mock<IMessageChannel>();
            ch.Setup(x => x.Id).Returns(channel);
            var author = new Mock<IUser>();
            author.Setup(x => x.Id).Returns(5);
            author.Setup(x => x.Username).Returns("grupa");

            var msg = new Mock<IUserMessage>();
            msg.Setup(x => x.Id).Returns(id);
            msg.Setup(x => x.Channel).Returns(ch.Object);
            msg.Setup(x => x.Author).Returns(author.Object);
            msg.Setup(x => x.Content).Returns(content);
            msg.Setup(x => x.Type).Returns(MessageType.Default);
            msg.Setup(x => x.Timestamp).Returns(new DateTimeOffset(2026, 10, 29, 10, 0, 0, TimeSpan.FromHours(1)));
            msg.Setup(x => x.Embeds).Returns(Array.Empty<IEmbed>());
            msg.Setup(x => x.Components).Returns(Array.Empty<IMessageComponent>());
            return msg;
        }

        private static string Hmac(string timestamp, string body)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
            return "sha256=" + BitConverter.ToString(hmac.ComputeHash(Encoding.UTF8.GetBytes(timestamp + "." + body))).Replace("-", "").ToLowerInvariant();
        }

        private static JObject Parse(string json)
            => JsonConvert.DeserializeObject<JObject>(json, new JsonSerializerSettings { DateParseHandling = DateParseHandling.None });

        private static string Header(HttpRequestMessage request, string name) => request.Headers.GetValues(name).Single();

        private JArray SentMessages(int request) => (JArray)Parse(_handler.Requests[request].Body)["messages"];

        [Fact]
        public void IgnoresWhenDisabledOrOtherChannel()
        {
            var ingest = Create();
            Assert.False(ingest.Enqueue(Msg(1, channel: 999).Object));

            _config.Model.Ingest.Secret = null;
            Assert.False(ingest.Enqueue(Msg(1).Object));
            _config.Model.Ingest.Secret = Secret;
            _config.Model.Ingest.Url = " ";
            Assert.False(ingest.Enqueue(Msg(1).Object));

            Assert.Equal(0, ingest.Queued);
        }

        [Fact]
        public async Task SendsSignedBatch()
        {
            var ingest = Create();
            Assert.True(ingest.Enqueue(Msg(1).Object));
            Assert.True(ingest.Enqueue(Msg(2).Object));
            var start = _now;

            await ingest.FlushAsync();

            var (request, body) = Assert.Single(_handler.Requests);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://beta.shinden.pl/gosana/ingest/discord", request.RequestUri.ToString());
            Assert.Equal("application/json", request.Content.Headers.ContentType.MediaType);

            var timestamp = Header(request, "X-Timestamp");
            Assert.Equal(start.ToUnixTimeSeconds().ToString(), timestamp);
            Assert.Equal(Hmac(timestamp, body), Header(request, "X-Signature"));

            Assert.Equal(new[] { "1", "2" }, SentMessages(0).Select(x => (string)x["id"]));
            Assert.Equal(0, ingest.Queued);
            Assert.NotNull(ingest.LastSuccess);
        }

        [Fact]
        public void MapsDiscordShape()
        {
            var msg = Msg(1234567890123456789);
            var webhook = new Mock<IWebhookUser>();
            webhook.Setup(x => x.Id).Returns(77);
            webhook.Setup(x => x.Username).Returns("Grupa X");
            webhook.Setup(x => x.IsWebhook).Returns(true);
            webhook.Setup(x => x.WebhookId).Returns(88);
            msg.Setup(x => x.Author).Returns(webhook.Object);
            msg.Setup(x => x.Flags).Returns(MessageFlags.IsCrosspost);
            msg.Setup(x => x.Type).Returns(MessageType.Default);
            msg.Setup(x => x.Reference).Returns(new MessageReference(111, 222, 333));
            msg.Setup(x => x.Embeds).Returns(new IEmbed[]
            {
                new EmbedBuilder().WithTitle("Odcinek 5").WithUrl("https://grupa.pl/5").WithColor(new Color(0x112233))
                    .WithFooter("stopka").AddField("Seria", "Coś", true).Build(),
            });
            msg.Setup(x => x.Components).Returns(new ComponentBuilder()
                .WithButton("Pobierz", url: "https://grupa.pl/dl", style: ButtonStyle.Link)
                .WithButton("Klik", "custom-id")
                .Build().Components.Cast<IMessageComponent>().ToList());

            var json = Parse(IngestMessage.Serialize(msg.Object));

            Assert.Equal("1234567890123456789", (string)json["id"]);
            Assert.Equal(Channel.ToString(), (string)json["channel_id"]);
            Assert.Equal(0, (int)json["type"]);
            Assert.Equal((int)MessageFlags.IsCrosspost, (int)json["flags"]);
            Assert.Equal("2026-10-29T09:00:00.000000+00:00", (string)json["timestamp"]);
            Assert.Equal("hej", (string)json["content"]);
            Assert.Equal(JObject.Parse("{\"id\":\"77\",\"username\":\"Grupa X\",\"bot\":true}"), json["author"]);
            Assert.Equal("88", (string)json["webhook_id"]);
            Assert.Equal(JObject.Parse("{\"guild_id\":\"333\",\"channel_id\":\"222\",\"message_id\":\"111\"}"), json["message_reference"]);
            Assert.Equal(JTokenType.Null, json["interaction_metadata"].Type);

            var embed = (JObject)Assert.Single(json["embeds"]);
            Assert.Equal("rich", (string)embed["type"]);
            Assert.Equal("Odcinek 5", (string)embed["title"]);
            Assert.Equal("https://grupa.pl/5", (string)embed["url"]);
            Assert.Equal(0x112233, (int)embed["color"]);
            Assert.Equal("stopka", (string)embed["footer"]["text"]);
            Assert.Equal(JArray.Parse("[{\"name\":\"Seria\",\"value\":\"Coś\",\"inline\":true}]"), embed["fields"]);
            Assert.Null(embed["image"]);

            Assert.Equal(JArray.Parse("[{\"type\":1,\"components\":[{\"type\":2,\"style\":5,\"label\":\"Pobierz\",\"url\":\"https://grupa.pl/dl\"}]}]"), json["components"]);
        }

        [Fact]
        public void PlainMessageHasNullOptionalFields()
        {
            var json = Parse(IngestMessage.Serialize(Msg(1).Object));

            Assert.Equal(JTokenType.Null, json["message_reference"].Type);
            Assert.Equal(JTokenType.Null, json["webhook_id"].Type);
            Assert.Equal(JTokenType.Null, json["interaction_metadata"].Type);
            Assert.False((bool)json["author"]["bot"]);
            Assert.Empty(json["embeds"]);
            Assert.Empty(json["components"]);
        }

        [Fact]
        public void CommandResponseHasInteractionMetadataWithoutUser()
        {
            var meta = new Mock<IMessageInteractionMetadata>();
            meta.Setup(x => x.Id).Returns(42);
            meta.Setup(x => x.Type).Returns(InteractionType.ApplicationCommand);
            var msg = Msg(1);
            msg.Setup(x => x.InteractionMetadata).Returns(meta.Object);

            var json = Parse(IngestMessage.Serialize(msg.Object));

            Assert.Equal(JObject.Parse("{\"id\":\"42\",\"type\":2}"), json["interaction_metadata"]);
        }

        [Fact]
        public async Task SplitsIntoBatchesOf50InOrder()
        {
            var ingest = Create();
            for (ulong i = 1; i <= 120; i++)
                ingest.Enqueue(Msg(i).Object);

            await ingest.FlushAsync();

            Assert.Equal(3, _handler.Requests.Count);
            Assert.Equal(new[] { 50, 50, 20 }, Enumerable.Range(0, 3).Select(x => SentMessages(x).Count));
            var ids = Enumerable.Range(0, 3).SelectMany(x => SentMessages(x).Select(m => ulong.Parse((string)m["id"])));
            Assert.Equal(Enumerable.Range(1, 120).Select(x => (ulong)x), ids);
            Assert.All(_delays, x => Assert.True(x >= TimeSpan.FromSeconds(1)));
        }

        [Fact]
        public async Task KeepsBodyUnderSizeLimit()
        {
            var ingest = Create();
            var big = new string('ż', 4000);
            for (ulong i = 1; i <= 50; i++)
                ingest.Enqueue(Msg(i, content: big).Object);

            await ingest.FlushAsync();

            Assert.True(_handler.Requests.Count > 1);
            Assert.All(_handler.Requests, x => Assert.True(Encoding.UTF8.GetByteCount(x.Body) <= DiscordIngest.MaxBodyBytes));
            Assert.Equal(50, Enumerable.Range(0, _handler.Requests.Count).Sum(x => SentMessages(x).Count));
        }

        [Fact]
        public async Task RetriesServerErrorsWithFreshSignature()
        {
            var ingest = Create();
            ingest.Enqueue(Msg(1).Object);
            _handler.Next.Enqueue(() => new HttpResponseMessage(HttpStatusCode.BadGateway));
            _handler.Next.Enqueue(() => throw new HttpRequestException("brak połączenia"));

            await ingest.FlushAsync();

            Assert.Equal(3, _handler.Requests.Count);
            Assert.Equal(new[] { DiscordIngest.RetryDelay, DiscordIngest.RetryDelay }, _delays.Take(2));
            var timestamps = _handler.Requests.Select(x => Header(x.Request, "X-Timestamp")).ToList();
            Assert.Equal(3, timestamps.Distinct().Count());
            Assert.All(_handler.Requests, x => Assert.Equal(Hmac(Header(x.Request, "X-Timestamp"), x.Body), Header(x.Request, "X-Signature")));
            Assert.Single(_handler.Requests.Select(x => x.Body).Distinct());
            Assert.False(ingest.Failing);
        }

        [Fact]
        public async Task GivesUpAfterSixRetries()
        {
            var ingest = Create();
            ingest.Enqueue(Msg(1).Object);
            _handler.Respond = () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

            await ingest.FlushAsync();

            Assert.Equal(1 + DiscordIngest.Retries, _handler.Requests.Count);
            Assert.True(ingest.Failing);
            Assert.Equal("odpowiedź 503", ingest.LastError);
            Assert.Contains(_logger.Messages, x => x.Contains("porzucono 1"));
            Assert.Equal(0, ingest.Queued);
        }

        [Fact]
        public async Task DoesNotRetryClientErrors()
        {
            var ingest = Create();
            ingest.Enqueue(Msg(1).Object);
            ingest.Enqueue(Msg(2).Object);
            _handler.Next.Enqueue(() => new HttpResponseMessage(HttpStatusCode.Unauthorized));

            await ingest.FlushAsync();

            Assert.Single(_handler.Requests);
            Assert.Contains(_logger.Messages, x => x.Contains("porzucono 2"));
        }

        [Fact]
        public async Task HonorsRetryAfter()
        {
            var ingest = Create();
            ingest.Enqueue(Msg(1).Object);
            _handler.Next.Enqueue(() =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(17));
                return response;
            });

            await ingest.FlushAsync();

            Assert.Equal(2, _handler.Requests.Count);
            Assert.Equal(TimeSpan.FromSeconds(17), _delays[0]);
        }

        [Fact]
        public async Task StopsRetryingWhenDisabled()
        {
            var ingest = Create();
            ingest.Enqueue(Msg(1).Object);
            _handler.Respond = () =>
            {
                _config.Model.Ingest.Url = null;
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            };

            await ingest.FlushAsync();

            Assert.Single(_handler.Requests);
        }

        [Fact]
        public void SignatureMatchesReference()
        {
            Assert.Equal(Hmac("1761739200", "{\"messages\":[]}"), "sha256=" + DiscordIngest.Sign(Secret, "1761739200", "{\"messages\":[]}"));
        }
    }
}
