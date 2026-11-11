using System.Text.Json.Nodes;
using Sanakan.Simulation;
using Sanakan.Simulation.FakeDiscord;

var options = Options.Parse(args);
if (options == null)
{
    Console.WriteLine("usage: dotnet run --project Simulation -- smoke|init [--port 5199] [--users 50] [--keep-db] [--world run/world.json] [--repo PATH]");
    Console.WriteLine("  init: fresh database, the bot creates the schema and stops (for tools/mysql_dump_to_sqlite.py)");
    Console.WriteLine("  load --world W --mix M [--minutes 10] [--speed 1] [--scale 1] [--chat 2300] [--start-hour 20]: replays the logged traffic mix");
    Console.WriteLine("       speed: virtual hours per real hour (hour-of-day profile), scale: traffic multiplier, chat: chat messages per hour");
    Console.WriteLine($"  --mysql: the bot uses MySQL from {BotProcess.MySqlVariable} instead of SQLite (tools/mysql_sim_db.py)");
    return 1;
}

var log = new SimLog();
var world = options.World != null ? World.FromFile(options.World) : new World(options.Users, new[] { "ogólny", "waifu", "komendy" });
var prefix = world.Prefix ?? "s.";
var replies = new ReplyTracker();
var baseUrl = $"http://127.0.0.1:{options.Port}";
var gateway = new Gateway(world, log, membersInline: 100) { ResumeUrl = $"ws://127.0.0.1:{options.Port}/gateway" };
var rest = new RestApi(world, gateway, log, replies);
var shinden = new FakeShinden(log);

var builder = WebApplication.CreateSlimBuilder();
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.WebHost.UseUrls(baseUrl);
var app = builder.Build();
app.UseWebSockets();
app.Run(async context =>
{
    var path = context.Request.Path.Value ?? "";
    var host = context.Request.Host.Host;
    if (host == FakeShinden.ImageHost) await shinden.HandleImageAsync(context);
    else if (host != "127.0.0.1" && host != "localhost")
    {
        // plain http the bot sent through its proxy setting to anything we do not model
        log.Route("proxy", context.Request.Method, $"{host}{path}");
        context.Response.StatusCode = 404;
    }
    else if (context.WebSockets.IsWebSocketRequest && path.StartsWith("/gateway")) await gateway.HandleAsync(context);
    else if (path.StartsWith(RestApi.Prefix + "/")) await rest.HandleAsync(context);
    else if (path.StartsWith(FakeShinden.Prefix + "/")) await shinden.HandleAsync(context);
    else
    {
        log.Route("other", context.Request.Method, path);
        context.Response.StatusCode = 404;
    }
});
await app.StartAsync();
log.Info($"fake Discord and Shinden listening on {baseUrl}");

var runDir = Path.Combine(options.Repo, "Simulation", "run");
var dll = Path.Combine(options.Repo, "src", "bin", "Release", "net6.0", "Sanakan.dll");
if (!File.Exists(dll))
{
    log.Info($"bot build not found: {dll} (dotnet build src/Sanakan.csproj -c Release)");
    return 1;
}

using var bot = new BotProcess(options.Repo, runDir, log);
Console.CancelKeyPress += (_, _) => bot.Dispose();
string mySql = null;
if (options.MySql)
{
    mySql = BotProcess.MySqlConnection();
    if (mySql == null)
    {
        log.Info($"--mysql needs the connection string in {BotProcess.MySqlVariable}");
        return 1;
    }
    log.Info("database: MySQL (reset it with tools/mysql_sim_db.py reset before comparable runs)");
}
bot.Prepare(world, baseUrl, fresh: !options.KeepDb, mySql);
bot.Start(dll);

var ready = Task.WhenAll(gateway.Identified.Task, bot.Started.Task);
if (await Task.WhenAny(ready, Task.Delay(TimeSpan.FromMinutes(2))) != ready || bot.HasExited)
{
    log.Info($"bot did not get ready (identified: {gateway.Identified.Task.IsCompleted}, started: {bot.Started.Task.IsCompleted}), see {bot.LogPath}");
    Report();
    return 1;
}
if (options.Mode == "init")
{
    log.Info($"bot created the schema in {Path.Combine(bot.BotDir, "sim.db")}, stopping");
    await app.StopAsync();
    return 0;
}
if (options.Mode == "load")
{
    if (options.World == null || options.Mix == null)
    {
        log.Info("load needs --world (tools/mysql_dump_to_sqlite.py) and --mix (tools/traffic_mix.py --json)");
        return 1;
    }

    // give the bot a moment to finish its startup work before measuring
    await Task.Delay(TimeSpan.FromSeconds(10));
    var settings = new LoadRunner.Settings(options.Minutes, options.Speed, options.Scale, options.ChatPerHour, options.StartHour, prefix, options.Extra);
    var runner = new LoadRunner(world, gateway, rest, replies, bot, log, JsonNode.Parse(File.ReadAllText(options.Mix)), settings);
    var result = await runner.RunAsync();
    result["requests"] = new JsonObject(log.Routes.OrderBy(x => x.Key).Select(x => KeyValuePair.Create(x.Key, (JsonNode)x.Value)));
    result["errors"] = new JsonArray(bot.Errors.Select(x => x.Length > 300 ? x[..300] : x).GroupBy(x => System.Text.RegularExpressions.Regex.Replace(x, @"^\[[^\]]+\] |https?://\S+|u\d+|\d+", "#"))
        .OrderByDescending(x => x.Count()).Take(20).Select(x => (JsonNode)$"{x.Count()}x {x.First()}").ToArray());

    var resultsDir = Path.Combine(runDir, "results");
    Directory.CreateDirectory(resultsDir);
    var resultPath = Path.Combine(resultsDir, $"load-{DateTime.Now:yyyyMMdd-HHmmss}.json");
    File.WriteAllText(resultPath, result.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    PrintLoad(result);
    log.Info($"result saved to {resultPath}");
    await app.StopAsync();
    return 0;
}
log.Info("bot is ready, sending commands");

var waifu = world.WaifuChannel;
// player 0 owns the guild (administrator); in a fresh database it sets up the channels like a real server,
// gets cards and then looks at them (card ids start at 1); the setup toggles channels, so it runs only there
var setup = new[] { (0, waifu, "mod wcmdch"), (0, world.TrashChannel, "mod tcmdch") };
var phases = options.World != null
    ? new[]
    {
        // players with the most commands in production, with their real collections
        new[] { (0, waifu, "harem"), (1, waifu, "profil"), (2, waifu, "portfel"), (3, waifu, "karta+"), (4, waifu, "pakiet"),
            (5, waifu, "misje"), (6, waifu, "drobne"), (7, waifu, "wyprawa status"), (8, waifu, "ping"), (9, waifu, "życzenia") },
    }
    : new[]
    {
        new[] { (0, waifu, "karta+"), (0, waifu, "pakiet 1"), (1, waifu, "ping"), (2, waifu, "info"), (3, waifu, "pakiet"),
            (4, waifu, "portfel"), (5, waifu, "profil"), (6, waifu, "misje"), (7, waifu, "drobne") },
        new[] { (0, waifu, "harem"), (0, waifu, "karta 1"), (0, waifu, "karta 2"), (0, waifu, "profil") },
    };
foreach (var phase in options.KeepDb ? phases : phases.Prepend(setup))
{
    foreach (var (player, channel, command) in phase)
    {
        var user = world.Users[player % world.Users.Count];
        var message = world.MessageJson(Snowflake.Next(), channel.Id, user, $"{prefix}{command}");
        rest.StoreUserMessage(message);
        replies.Sent(channel.Id, user.Id, command);
        await gateway.DispatchAsync("MESSAGE_CREATE", message);
        await Task.Delay(500);
    }

    var deadline = DateTime.UtcNow.AddSeconds(30);
    while (replies.Waiting > 0 && DateTime.UtcNow < deadline)
        await Task.Delay(250);
}

Report();
await app.StopAsync();
return replies.Waiting == 0 ? 0 : 2;

void PrintLoad(JsonObject result)
{
    Console.WriteLine();
    Console.WriteLine($"== load: {result["elapsed_minutes"]} min real, {result["virtual_hours"]} h virtual; commands {result["commands_sent"]}, chat {result["chat_sent"]}, " +
        $"bot messages {result["bot_messages"]}, unanswered {result["unanswered"]}, bot errors {result["bot_errors"]}");
    Console.WriteLine($"   memory MiB {result["memory_mib"]?.ToJsonString()}, CPU % {result["cpu_percent"]?.ToJsonString()}");
    Console.WriteLine($"   {"command",-16} {"sent",6} {"reply",6} {"timed",6} {"p50",7} {"p95",7} {"max",7}");
    foreach (var (name, c) in result["commands"]!.AsObject())
        Console.WriteLine($"   {name,-16} {c!["sent"],6} {c["replied"],6} {c["timed"],6} {c["p50_ms"],7} {c["p95_ms"],7} {c["max_ms"],7}");
    Console.WriteLine("== errors");
    foreach (var error in result["errors"]!.AsArray())
        Console.WriteLine($"   {error}");
}

void Report()
{
    Console.WriteLine();
    Console.WriteLine("== replies");
    foreach (var (command, ms, _) in replies.Answered.OrderBy(x => x.Ms).Take(40))
        Console.WriteLine($"  {command,-12} {ms,8:0} ms");
    foreach (var command in replies.Unanswered.Take(40))
        Console.WriteLine($"  {command,-12} no reply");
    Console.WriteLine($"  bot messages: {rest.SentMessages}, matched without a mention: {replies.MatchedByOrder}, not matched: {replies.Unmatched}");
    foreach (var preview in rest.Previews.Take(20))
        Console.WriteLine($"    > {preview}");

    Console.WriteLine("== requests to the simulator");
    foreach (var (route, count) in log.Routes.OrderBy(x => x.Key))
        Console.WriteLine($"  {count,5}  {route}");

    Console.WriteLine($"== bot errors ({bot.Errors.Count})");
    foreach (var error in bot.Errors.Take(15))
        Console.WriteLine($"  {(error.Length > 300 ? error[..300] + "..." : error)}");
}

sealed record Options(string Mode, int Port, int Users, bool KeepDb, string Repo, string World,
    string Mix = null, double Minutes = 10, double Speed = 1, double Scale = 1, double ChatPerHour = 2300, int StartHour = 20, bool MySql = false,
    IReadOnlyList<string> Extra = null)
{
    public static Options Parse(string[] args)
    {
        if (args.Length == 0 || args[0] is not ("smoke" or "init" or "load")) return null;

        int port = 5199, users = 50, startHour = 20;
        bool keepDb = false, mySql = false;
        string repo = Directory.GetCurrentDirectory();
        string world = null, mix = null;
        List<string> extra = null;
        double minutes = 10, speed = 1, scale = 1, chat = 2300;
        double D(string value) => double.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--port": port = int.Parse(args[++i]); break;
                case "--users": users = int.Parse(args[++i]); break;
                case "--keep-db": keepDb = true; break;
                case "--repo": repo = args[++i]; break;
                case "--world": world = args[++i]; break;
                case "--mix": mix = args[++i]; break;
                case "--minutes": minutes = D(args[++i]); break;
                case "--speed": speed = D(args[++i]); break;
                case "--scale": scale = D(args[++i]); break;
                case "--chat": chat = D(args[++i]); break;
                case "--start-hour": startHour = int.Parse(args[++i]); break;
                case "--mysql": mySql = true; break;
                case "--extra": extra = args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(); break;
                default: return null;
            }
        }
        // a world file belongs to an imported database, which a fresh start would delete
        keepDb |= world != null;
        var root = FindRepo(Path.GetFullPath(repo));
        // relative paths are relative to the repository, dotnet run changes the working directory
        string Resolve(string path) => path == null || Path.IsPathRooted(path) ? path : Path.Combine(root, path);
        return new Options(args[0], port, users, keepDb && args[0] != "init", root, Resolve(world),
            Resolve(mix), minutes, speed, scale, chat, startHour, mySql, extra);
    }

    // dotnet run starts in the project directory, the repository is the first parent with the bot project
    private static string FindRepo(string start)
    {
        for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "Sanakan.csproj")))
                return dir.FullName;
        }
        return start;
    }
}
