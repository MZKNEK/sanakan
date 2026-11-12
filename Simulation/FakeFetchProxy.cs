using System.Text.Json.Nodes;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Sanakan.Simulation;

// The host's fetch proxy for downloads from players' links: the same request and answers as the real one, a picture
// for every address except those with "missing" in them (the linked site answers 404), and its limit of 4 at once.
public sealed class FakeFetchProxy
{
    public const string Path = "/fetch";
    public const string Key = "sim-fetch-key";
    // what the catalog's commands link to; the bot has to send it through the proxy, directly it would go nowhere
    public const string ImageHost = "img.sim.test";
    private const int MaxParallel = 4;

    private readonly SimLog _log;
    private readonly byte[] _png;
    private int _running;
    private int _mostAtOnce;

    public FakeFetchProxy(SimLog log)
    {
        _log = log;
        using var image = new Image<Rgba32>(800, 500, new Rgba32(90, 60, 140));
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        _png = stream.ToArray();
    }

    public int MostAtOnce => Volatile.Read(ref _mostAtOnce);

    public async Task HandleAsync(HttpContext context)
    {
        if (context.Request.Headers["X-Internal-Key"] != Key)
        {
            _log.Route("fetch-proxy", context.Request.Method, "bad_key");
            await AnswerAsync(context, 401, new JsonObject { ["code"] = "bad_key", ["message"] = "bad key", ["retry_after"] = 0 });
            return;
        }

        var request = await JsonNode.ParseAsync(context.Request.Body);
        var url = request?["url"]?.GetValue<string>();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || request?["method"]?.GetValue<string>() != "GET")
        {
            _log.Route("fetch-proxy", context.Request.Method, "bad_request");
            await AnswerAsync(context, 400, new JsonObject { ["code"] = "bad_request", ["message"] = "bad request", ["retry_after"] = 0 });
            return;
        }

        var now = Interlocked.Increment(ref _running);
        try
        {
            int most;
            while (now > (most = Volatile.Read(ref _mostAtOnce)) && Interlocked.CompareExchange(ref _mostAtOnce, now, most) != most) { }
            if (now > MaxParallel)
            {
                _log.Route("fetch-proxy", "GET", "busy");
                await AnswerAsync(context, 429, new JsonObject { ["code"] = "busy", ["message"] = "busy", ["retry_after"] = 1 });
                return;
            }

            // the real one downloads through a VPN
            await Task.Delay(Random.Shared.Next(150, 400));
            var missing = uri.AbsolutePath.Contains("missing", StringComparison.OrdinalIgnoreCase);
            _log.Route("fetch-proxy", "GET", $"{uri.Host} {(missing ? 404 : 200)}");
            await AnswerAsync(context, 200, missing
                ? new JsonObject { ["status"] = 404, ["content_type"] = "text/html", ["content_length"] = 0, ["body"] = "", ["truncated"] = false }
                : new JsonObject { ["status"] = 200, ["content_type"] = "image/png", ["content_length"] = _png.Length, ["body"] = Convert.ToBase64String(_png), ["truncated"] = false });
        }
        finally
        {
            Interlocked.Decrement(ref _running);
        }
    }

    private static async Task AnswerAsync(HttpContext context, int status, JsonObject body)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(body.ToJsonString());
    }
}
