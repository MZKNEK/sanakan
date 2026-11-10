using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;

namespace Sanakan.Simulation.FakeDiscord;

// Speaks just enough of the Discord gateway (JSON, uncompressed text frames) for Discord.Net to log in and receive events.
public sealed class Gateway
{
    private const int MembersPerChunk = 1000;

    private readonly World _world;
    private readonly SimLog _log;
    private readonly int _membersInline;
    private readonly SemaphoreSlim _sending = new(1, 1);
    private WebSocket _socket;
    private int _sequence;

    public Gateway(World world, SimLog log, int membersInline)
    {
        _world = world;
        _log = log;
        _membersInline = membersInline;
    }

    public TaskCompletionSource Identified { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool IsConnected => _socket?.State == WebSocketState.Open;
    public string ResumeUrl { get; set; }

    public async Task HandleAsync(HttpContext context)
    {
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        _socket = socket;
        _log.Info($"gateway: connected {context.Request.QueryString}");

        await SendAsync(new JsonObject { ["op"] = 10, ["d"] = new JsonObject { ["heartbeat_interval"] = 41250 }, ["s"] = null, ["t"] = null });

        var buffer = new byte[64 * 1024];
        var message = new MemoryStream();
        while (socket.State == WebSocketState.Open)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await socket.ReceiveAsync(buffer, context.RequestAborted);
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
            {
                break;
            }

            if (result.MessageType == WebSocketMessageType.Close)
                break;

            message.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage)
                continue;

            var text = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
            message.SetLength(0);
            await OnMessageAsync(JsonNode.Parse(text));
        }

        _log.Info($"gateway: disconnected ({socket.CloseStatus} {socket.CloseStatusDescription})");
        Identified = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private async Task OnMessageAsync(JsonNode payload)
    {
        var op = payload["op"]?.GetValue<int>() ?? -1;
        switch (op)
        {
            case 1:
                await SendAsync(new JsonObject { ["op"] = 11, ["d"] = null, ["s"] = null, ["t"] = null });
                break;

            case 2:
                await DispatchAsync("READY", new JsonObject
                {
                    ["v"] = 10,
                    ["user"] = _world.UserJson(_world.BotUser),
                    ["guilds"] = new JsonArray(new JsonObject { ["id"] = _world.GuildId.ToString(), ["unavailable"] = true }),
                    ["session_id"] = Guid.NewGuid().ToString("N"),
                    ["resume_gateway_url"] = ResumeUrl,
                    ["private_channels"] = new JsonArray(),
                    ["relationships"] = new JsonArray(),
                    ["presences"] = new JsonArray(),
                    ["application"] = new JsonObject { ["id"] = _world.BotUser.Id.ToString(), ["flags"] = 0 },
                });
                await DispatchAsync("GUILD_CREATE", _world.GuildCreateJson(_membersInline));
                Identified.TrySetResult();
                break;

            case 3:
                break;

            case 6:
                _log.Info("gateway: resume requested, invalidating the session");
                await SendAsync(new JsonObject { ["op"] = 9, ["d"] = false, ["s"] = null, ["t"] = null });
                break;

            case 8:
                await SendMembersAsync(payload["d"]);
                break;

            default:
                _log.Info($"gateway: unhandled op {op}: {payload.ToJsonString()}");
                break;
        }
    }

    private async Task SendMembersAsync(JsonNode request)
    {
        var nonce = request?["nonce"]?.ToString();
        var members = _world.Users.Select(x => _world.MemberJson(x)).Prepend(_world.MemberJson(_world.BotUser)).ToList();
        var chunks = Math.Max(1, (members.Count + MembersPerChunk - 1) / MembersPerChunk);
        for (int i = 0; i < chunks; i++)
        {
            var chunk = new JsonObject
            {
                ["guild_id"] = _world.GuildId.ToString(),
                ["members"] = new JsonArray(members.Skip(i * MembersPerChunk).Take(MembersPerChunk).Select(x => (JsonNode)x).ToArray()),
                ["chunk_index"] = i,
                ["chunk_count"] = chunks,
                ["not_found"] = new JsonArray(),
            };
            if (nonce != null)
                chunk["nonce"] = nonce;
            await DispatchAsync("GUILD_MEMBERS_CHUNK", chunk);
        }
    }

    public Task DispatchAsync(string type, JsonNode data)
        => SendAsync(new JsonObject { ["op"] = 0, ["d"] = data, ["s"] = Interlocked.Increment(ref _sequence), ["t"] = type });

    private async Task SendAsync(JsonObject payload)
    {
        var socket = _socket;
        if (socket == null || socket.State != WebSocketState.Open)
            return;

        var bytes = Encoding.UTF8.GetBytes(payload.ToJsonString());
        await _sending.WaitAsync();
        try
        {
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException)
        {
            _log.Info($"gateway: send failed: {ex.Message}");
        }
        finally
        {
            _sending.Release();
        }
    }
}
