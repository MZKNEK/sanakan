using System.Collections.Concurrent;
using System.Text.Json.Nodes;

namespace Sanakan.Simulation.FakeDiscord;

// The REST endpoints Discord.Net calls while the bot logs in and answers commands; anything else is counted and gets 404.
public sealed class RestApi
{
    public const string Prefix = "/api/v10";
    private const int StoredMessages = 5000;

    private readonly World _world;
    private readonly Gateway _gateway;
    private readonly SimLog _log;
    private readonly ReplyTracker _replies;
    private readonly ConcurrentDictionary<ulong, JsonObject> _messages = new();
    private readonly ConcurrentQueue<ulong> _messageOrder = new();
    private readonly ConcurrentDictionary<ulong, ulong> _dmChannels = new();

    public RestApi(World world, Gateway gateway, SimLog log, ReplyTracker replies)
    {
        _world = world;
        _gateway = gateway;
        _log = log;
        _replies = replies;
    }

    // the default encoder escapes '<' and '>', which would hide the <@id> mentions from the reply tracker
    private static readonly System.Text.Json.JsonSerializerOptions Relaxed = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public int SentMessages => _sent;
    private int _sent;

    public ConcurrentQueue<string> Previews { get; } = new();

    private static string Preview(JsonNode payload, int files)
    {
        var text = payload["content"]?.ToString();
        if (string.IsNullOrWhiteSpace(text))
            text = payload["embeds"]?[0]?["description"]?.ToString() ?? payload["embeds"]?[0]?["title"]?.ToString() ?? "";
        text = text.ReplaceLineEndings(" ");
        return (text.Length > 110 ? text[..110] + "..." : text) + (files > 0 ? $" [+{files} file(s)]" : "");
    }

    public async Task HandleAsync(HttpContext context)
    {
        var request = context.Request;
        var path = request.Path.Value![Prefix.Length..].Trim('/');
        var segments = path.Split('/');
        var method = request.Method;
        _log.Route("discord", method, path);

        object response = (method, segments) switch
        {
            ("GET", ["users", "@me"]) => _world.UserJson(_world.BotUser),
            ("GET", ["users", var id]) => UserOrNull(id),
            ("POST", ["users", "@me", "channels"]) => await CreateDmAsync(request),
            ("GET", ["gateway", ..]) => new JsonObject
            {
                ["url"] = _gateway.ResumeUrl,
                ["shards"] = 1,
                ["session_start_limit"] = new JsonObject { ["total"] = 1000, ["remaining"] = 1000, ["reset_after"] = 0, ["max_concurrency"] = 1 },
            },
            ("GET", ["oauth2", "applications", "@me"]) or ("GET", ["applications", "@me"]) => new JsonObject
            {
                ["id"] = _world.BotUser.Id.ToString(),
                ["name"] = _world.BotUser.Name,
                ["icon"] = null,
                ["description"] = "",
                ["bot_public"] = false,
                ["bot_require_code_grant"] = false,
                ["owner"] = _world.UserJson(_world.Users[0]),
                ["flags"] = 0,
            },
            ("GET", ["channels", var id]) => ChannelOrNull(id),
            ("POST", ["channels", var channel, "messages"]) => await CreateMessageAsync(context, channel),
            ("PATCH", ["channels", _, "messages", var id]) => await EditMessageAsync(request, id),
            ("GET", ["channels", _, "messages", var id]) => ulong.TryParse(id, out var mid) && _messages.TryGetValue(mid, out var stored) ? stored : null,
            ("GET", ["channels", _, "messages"]) => new JsonArray(),
            ("DELETE", ["channels", _, "messages", var id]) => Deleted(id),
            ("PUT" or "DELETE", ["channels", _, "messages", _, "reactions", ..]) => Empty.Value,
            ("POST", ["channels", _, "typing"]) => Empty.Value,
            ("GET", ["guilds", _, "members", var id]) => MemberOrNull(id),
            ("PUT" or "DELETE", ["guilds", _, "members", var user, "roles", var role]) => ChangeRole(user, role, method == "PUT"),
            _ => null,
        };

        if (response is Empty)
        {
            context.Response.StatusCode = 204;
            return;
        }

        if (response == null)
        {
            context.Response.StatusCode = 404;
            await context.Response.WriteAsJsonAsync(new { code = 10000, message = $"sim: {method} {path} not implemented" });
            return;
        }

        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(((JsonNode)response).ToJsonString());
    }

    private sealed class Empty
    {
        public static readonly Empty Value = new();
    }

    private JsonObject UserOrNull(string id)
        => ulong.TryParse(id, out var userId) && _world.FindUser(userId) is { } user ? _world.UserJson(user) : null;

    private JsonObject MemberOrNull(string id)
        => ulong.TryParse(id, out var userId) && _world.FindUser(userId) is { } user ? _world.MemberJson(user) : null;

    private object ChangeRole(string userText, string roleText, bool add)
    {
        if (!ulong.TryParse(userText, out var userId) || !ulong.TryParse(roleText, out var roleId) || _world.FindUser(userId) is not { } user)
            return null;

        _world.ChangeRole(userId, roleId, add);
        // Discord announces the new role list, the bot's member cache only learns it this way
        var update = _world.MemberJson(user);
        update["guild_id"] = _world.GuildId.ToString();
        _ = Task.Run(() => _gateway.DispatchAsync("GUILD_MEMBER_UPDATE", update));
        return Empty.Value;
    }

    private JsonObject ChannelOrNull(string id)
    {
        if (!ulong.TryParse(id, out var channelId)) return null;
        if (_dmChannels.TryGetValue(channelId, out var userId))
            return DmJson(channelId, userId);

        var index = _world.Channels.FindIndex(x => x.Id == channelId);
        return index < 0 ? null : _world.ChannelJson(_world.Channels[index], index);
    }

    private JsonObject DmJson(ulong channelId, ulong userId) => new()
    {
        ["id"] = channelId.ToString(),
        ["type"] = 1,
        ["last_message_id"] = null,
        ["recipients"] = new JsonArray(_world.UserJson(_world.FindUser(userId) ?? _world.Users[0])),
    };

    private async Task<JsonObject> CreateDmAsync(HttpRequest request)
    {
        var body = await JsonNode.ParseAsync(request.Body);
        var userId = ulong.Parse(body?["recipient_id"]?.ToString() ?? "0");
        var existing = _dmChannels.FirstOrDefault(x => x.Value == userId);
        var channelId = existing.Key != 0 ? existing.Key : Snowflake.Next();
        _dmChannels[channelId] = userId;
        return DmJson(channelId, userId);
    }

    private static async Task<(JsonNode Payload, IFormFileCollection Files)> ReadPayloadAsync(HttpRequest request)
    {
        if (request.HasFormContentType)
        {
            var form = await request.ReadFormAsync();
            var json = form["payload_json"].FirstOrDefault();
            return (json != null ? JsonNode.Parse(json) : new JsonObject(), form.Files);
        }
        return (await JsonNode.ParseAsync(request.Body) ?? new JsonObject(), null);
    }

    private async Task<JsonObject> CreateMessageAsync(HttpContext context, string channel)
    {
        if (!ulong.TryParse(channel, out var channelId)) return null;

        var (payload, files) = await ReadPayloadAsync(context.Request);
        var attachments = new JsonArray();
        foreach (var file in (IEnumerable<IFormFile>)files ?? Array.Empty<IFormFile>())
        {
            var id = Snowflake.Next();
            attachments.Add(new JsonObject
            {
                ["id"] = id.ToString(),
                ["filename"] = file.FileName,
                ["size"] = file.Length,
                ["url"] = $"http://127.0.0.1/attachments/{channelId}/{id}/{file.FileName}",
                ["proxy_url"] = $"http://127.0.0.1/attachments/{channelId}/{id}/{file.FileName}",
                ["content_type"] = file.ContentType,
            });
        }

        var message = _world.MessageJson(Snowflake.Next(), channelId, _world.BotUser, payload["content"]?.ToString(),
            payload["embeds"]?.DeepClone() as JsonArray, attachments, payload["components"]?.DeepClone() as JsonArray);
        if (_dmChannels.ContainsKey(channelId))
            message.Remove("guild_id");

        Store(message);
        Interlocked.Increment(ref _sent);
        _replies.Replied(channelId, payload.ToJsonString(Relaxed));
        Previews.Enqueue(Preview(payload, attachments.Count));

        // Discord echoes the bot's own messages through the gateway, they end up in the bot's message cache
        var echo = message.DeepClone();
        _ = Task.Run(() => _gateway.DispatchAsync("MESSAGE_CREATE", echo));
        return message;
    }

    private async Task<JsonObject> EditMessageAsync(HttpRequest request, string id)
    {
        if (!ulong.TryParse(id, out var messageId) || !_messages.TryGetValue(messageId, out var message)) return null;

        var (payload, _) = await ReadPayloadAsync(request);
        var edited = (JsonObject)message.DeepClone();
        foreach (var key in new[] { "content", "embeds", "components" })
        {
            if (payload[key] != null)
                edited[key] = payload[key].DeepClone();
        }
        edited["edited_timestamp"] = World.Time(DateTimeOffset.UtcNow);
        _messages[messageId] = edited;
        return edited;
    }

    private object Deleted(string id)
    {
        if (ulong.TryParse(id, out var messageId))
            _messages.TryRemove(messageId, out _);
        return Empty.Value;
    }

    private void Store(JsonObject message)
    {
        var id = ulong.Parse(message["id"]!.ToString());
        _messages[id] = message;
        _messageOrder.Enqueue(id);
        while (_messageOrder.Count > StoredMessages && _messageOrder.TryDequeue(out var old))
            _messages.TryRemove(old, out _);
    }

    public void StoreUserMessage(JsonObject message) => Store(message);
}
