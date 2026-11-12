using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Sanakan.Simulation.FakeDiscord;

namespace Sanakan.Simulation;

// Prepares an isolated working directory for the bot and runs it with all outgoing internet traffic blocked.
public sealed class BotProcess : IDisposable
{
    // nothing listens there, so any request that is not for the simulator fails at once
    private const string BlackholeProxy = "http://127.0.0.1:9";

    private readonly string _repo;
    private readonly string _runDir;
    private readonly SimLog _log;
    private Process _process;
    private StreamWriter _output;

    public BotProcess(string repo, string runDir, SimLog log)
    {
        _repo = repo;
        _runDir = runDir;
        _log = log;
    }

    public const string JwtIssuer = "sanakan-simulation";

    // written into the bot's config, the API runner signs player tokens and calls as the site with them
    public string JwtKey { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    public string SiteKey { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    public string BotDir => Path.Combine(_runDir, "bot");
    public string LogPath => Path.Combine(_runDir, "bot.log");
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public ConcurrentQueue<string> Errors { get; } = new();

    public static string FakeToken(ulong botId)
    {
        var id = Convert.ToBase64String(Encoding.ASCII.GetBytes(botId.ToString())).TrimEnd('=');
        return $"{id}.SIMULA.{Convert.ToBase64String(RandomNumberGenerator.GetBytes(30)).Replace('+', 'a').Replace('/', 'b').TrimEnd('=')}";
    }

    private string _simulatorUrl = BlackholeProxy;

    public const string MySqlVariable = "SANAKAN_SIM_MYSQL";

    // the connection string stays in the run directory, which git ignores
    public static string MySqlConnection()
        => Environment.GetEnvironmentVariable(MySqlVariable)
            ?? (OperatingSystem.IsWindows() ? Environment.GetEnvironmentVariable(MySqlVariable, EnvironmentVariableTarget.User) : null);

    public void Prepare(World world, string baseUrl, bool fresh, string mySql = null)
    {
        _simulatorUrl = baseUrl;
        Directory.CreateDirectory(BotDir);
        foreach (var dir in new[] { "Pictures", "Fonts" })
            CopyDirectory(Path.Combine(_repo, "src", dir), Path.Combine(BotDir, dir));

        var db = Path.Combine(BotDir, "sim.db");
        if (fresh)
        {
            foreach (var file in new[] { db, db + "-wal", db + "-shm" })
                File.Delete(file);

            // the bot keeps the character pool and generated cards in ../GOut, a fresh run starts without them
            var output = Path.Combine(_runDir, "GOut");
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
        }

        var config = new JsonObject
        {
            ["Prefix"] = "s.",
            ["BotToken"] = FakeToken(world.BotUser.Id),
            ["Supervision"] = false,
            ["Demonization"] = false,
            ["SafariEnabled"] = false,
            ["AutoCleanCards"] = false,
            ["DatabaseProvider"] = mySql != null ? "MySql" : "Sqlite",
            ["ConnectionString"] = mySql ?? "Data Source=sim.db",
            ["CharPerPacket"] = 250000,
            ["PacksPerDay"] = 4,
            ["Shinden"] = new JsonObject
            {
                ["Token"] = "sim",
                ["ApiToken"] = "sim",
                ["UserAgent"] = "sanakan-simulation",
                ["Marmolade"] = "",
                ["BaseUri"] = $"{baseUrl}{FakeShinden.Prefix}/",
            },
            ["Exp"] = new JsonObject { ["CharPerPoint"] = 60, ["MinPerMessage"] = 0.00005, ["MaxPerMessage"] = 5 },
            ["Dev"] = new JsonArray(),
            ["Jwt"] = new JsonObject { ["Key"] = JwtKey, ["Issuer"] = JwtIssuer },
            ["GiveBanForUrlSpam"] = false,
            ["ApiKeys"] = new JsonArray(),
            ["UserKeyApps"] = new JsonArray(new JsonObject { ["Key"] = SiteKey, ["Bearer"] = "sim-site", ["Permissions"] = "Info, Site" }),
            ["AdminNotifyChannels"] = new JsonArray(),
            ["BlacklistedGuilds"] = new JsonArray(),
            ["LogChannel"] = new JsonObject { ["GuildId"] = 0, ["ChannelId"] = 0, ["ErrorRoleId"] = 0 },
            ["Heartbeat"] = null,
            ["Ingest"] = null,
            ["FetchProxy"] = new JsonObject { ["Url"] = $"{baseUrl}{FakeFetchProxy.Path}", ["Key"] = FakeFetchProxy.Key },
            ["DiscordGatewayUrl"] = $"{baseUrl.Replace("http://", "ws://")}/gateway",
            ["DiscordApiUrl"] = $"{baseUrl}{RestApi.Prefix}/",
        };
        File.WriteAllText(Path.Combine(BotDir, "Config.json"), config.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    public void Start(string dll)
    {
        var info = new ProcessStartInfo("dotnet", $"\"{dll}\"")
        {
            WorkingDirectory = BotDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        // plain http (Shinden pictures) goes to the simulator, https has nowhere to go
        foreach (var name in new[] { "HTTPS_PROXY", "ALL_PROXY", "https_proxy", "all_proxy" })
            info.Environment[name] = BlackholeProxy;
        info.Environment["HTTP_PROXY"] = info.Environment["http_proxy"] = _simulatorUrl;
        info.Environment["NO_PROXY"] = info.Environment["no_proxy"] = "127.0.0.1,localhost";

        _output = new StreamWriter(LogPath, append: false) { AutoFlush = true };
        _process = new Process { StartInfo = info, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, e) => OnLine(e.Data);
        _process.ErrorDataReceived += (_, e) => OnLine(e.Data);
        _process.Exited += (_, _) => _log.Info($"bot: exited with code {_process.ExitCode}");
        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
        _log.Info($"bot: started pid {_process.Id}, log {LogPath}");
    }

    public bool HasExited => _process?.HasExited ?? true;
    public Process Process => _process;

    private void OnLine(string line)
    {
        if (line == null) return;
        lock (_output)
            _output.WriteLine(line);

        if (line.Contains("] Bot: Sanakan ") && line.Contains(" started"))
            Started.TrySetResult();
        if (line.Contains("] ERROR "))
            Errors.Enqueue(line);
    }

    private static void CopyDirectory(string source, string target)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            if (File.Exists(destination) && File.GetLastWriteTimeUtc(destination) >= File.GetLastWriteTimeUtc(file))
                continue;
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }

    public void Dispose()
    {
        try
        {
            if (_process is { HasExited: false })
                _process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        _process?.Dispose();
        _output?.Dispose();
    }
}
