using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Sanakan.Simulation.FakeDiscord;

namespace Sanakan.Simulation;

// Calls the bot's web API like the site (app key) and the players (their bearer token, signed with the key the
// simulation wrote into the bot's config) and keeps the status codes and times per template.
public sealed class ApiRunner : IDisposable
{
    public const string BaseUrl = "http://localhost:5000/";

    private readonly HttpClient _client = new(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri(BaseUrl), Timeout = TimeSpan.FromSeconds(60) };
    private readonly BotProcess _bot;
    private readonly SimLog _log;
    private readonly List<SimUser> _players;
    private readonly Random _random = new(11);
    private readonly Dictionary<string, (int Sent, Dictionary<int, int> Statuses, List<double> Ms)> _stats = new();
    private List<(SimUser User, ulong Shinden)> _withShinden = new();
    private readonly List<SimUser> _packOwners = new();

    public ApiRunner(BotProcess bot, SimLog log, List<SimUser> players)
    {
        _bot = bot;
        _log = log;
        _players = players;
    }

    // per production minute, from the hourly API reports and from the logged writes
    public static Dictionary<string, double> RatesFromMix(JsonNode mix)
    {
        var rates = new Dictionary<string, double>();
        var api = mix["api"];
        if (api == null) return rates;

        var minutes = (double?)api["minutes"] ?? 0;
        var writeMinutes = ((double?)mix["span"]?["hours"] ?? 0) * 60;
        foreach (var (name, template) in ApiCatalog.Templates)
        {
            if (minutes > 0 && api["by_route"]?[template.MixKey] is JsonNode count)
                rates[name] = (double)count / minutes;
            else if (writeMinutes > 0 && api["writes"]?[template.MixKey]?["count"] is JsonNode writes)
                rates[name] = (double)writes / writeMinutes;
        }
        return rates;
    }

    // the site reads the Shinden ids of a few players from their bot profile, which also exercises that endpoint
    public async Task PrepareAsync(int count = 30)
    {
        foreach (var user in _players.Take(count))
        {
            var (status, body) = await SendAsync("api:user", ApiCatalog.Templates["api:user"], new ApiCatalog.Context { User = user, Random = _random });
            if (status != 200 || body == null) continue;

            var node = JsonNode.Parse(body);
            var shinden = (ulong?)(node?["shinden"] ?? node?["Shinden"]) ?? 0;
            if (shinden != 0)
                _withShinden.Add((user, shinden));
        }
        _log.Info($"api: {_withShinden.Count} of {Math.Min(count, _players.Count)} players have a Shinden account");
    }

    public Task SendAsync(string name)
    {
        var template = ApiCatalog.Templates[name];
        ApiCatalog.Context context;
        lock (_random)
        {
            if (template.NeedsShinden && _withShinden.Count > 0)
            {
                var (user, shinden) = _withShinden[_random.Next(_withShinden.Count)];
                context = new ApiCatalog.Context { User = user, Shinden = shinden, Random = _random };
            }
            else if (template.NeedsPacks && _packOwners.Count > 0)
            {
                var owner = _packOwners[_random.Next(_packOwners.Count)];
                _packOwners.Remove(owner);
                context = new ApiCatalog.Context { User = owner, Random = _random };
            }
            else
            {
                context = new ApiCatalog.Context { User = _players[_random.Next(_players.Count)], Random = _random };
            }
        }
        return SendAsync(name, template, context);
    }

    private async Task<(int Status, string Body)> SendAsync(string name, ApiCatalog.Template template, ApiCatalog.Context context)
    {
        string path, requestBody;
        lock (_random)
        {
            path = template.Path(context);
            requestBody = template.Body?.Invoke(context);
        }

        using var request = new HttpRequestMessage(template.Method, path);
        if (requestBody != null)
            request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
        else if (template.Method == HttpMethod.Post)
            request.Content = new StringContent("", Encoding.UTF8, "application/json");
        if (template.Auth == ApiCatalog.Auth.Player)
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", PlayerToken(context.User.Id));
        else
            request.Headers.Add("x-app-key", _bot.SiteKey);

        var watch = Stopwatch.StartNew();
        int status;
        string body = null;
        try
        {
            using var response = await _client.SendAsync(request);
            status = (int)response.StatusCode;
            body = await response.Content.ReadAsStringAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            status = 0;
        }

        if (template.GivesPacks && status == 200)
        {
            lock (_random)
                _packOwners.Add(context.User);
        }

        lock (_stats)
        {
            var stats = _stats.TryGetValue(name, out var s) ? s : (Sent: 0, Statuses: new Dictionary<int, int>(), Ms: new List<double>());
            stats.Statuses[status] = stats.Statuses.GetValueOrDefault(status) + 1;
            stats.Ms.Add(watch.Elapsed.TotalMilliseconds);
            _stats[name] = (stats.Sent + 1, stats.Statuses, stats.Ms);
        }
        return (status, body);
    }

    private string PlayerToken(ulong discordId)
    {
        static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var header = Encode(Encoding.UTF8.GetBytes("{\"alg\":\"HS256\",\"typ\":\"JWT\"}"));
        var payload = Encode(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["jti"] = Guid.NewGuid().ToString(),
            ["DiscordId"] = discordId.ToString(),
            ["Player"] = "waifu_player",
            ["iss"] = BotProcess.JwtIssuer,
            ["aud"] = BotProcess.JwtIssuer,
            ["exp"] = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
        }));
        var signature = Encode(HMACSHA256.HashData(Encoding.UTF8.GetBytes(_bot.JwtKey), Encoding.UTF8.GetBytes($"{header}.{payload}")));
        return $"{header}.{payload}.{signature}";
    }

    public JsonObject Result()
    {
        var result = new JsonObject();
        lock (_stats)
        {
            foreach (var (name, (sent, statuses, ms)) in _stats.OrderByDescending(x => x.Value.Sent))
            {
                var sorted = ms.OrderBy(x => x).ToList();
                result[name] = new JsonObject
                {
                    ["sent"] = sent,
                    ["statuses"] = new JsonObject(statuses.OrderBy(x => x.Key).Select(x => KeyValuePair.Create(x.Key.ToString(), (JsonNode)x.Value))),
                    ["p50_ms"] = Math.Round(sorted[sorted.Count / 2]),
                    ["p95_ms"] = Math.Round(sorted[(int)Math.Min(sorted.Count - 1, sorted.Count * 0.95)]),
                    ["max_ms"] = Math.Round(sorted[^1]),
                };
            }
        }
        return result;
    }

    public void Dispose() => _client.Dispose();
}
