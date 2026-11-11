using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Sanakan.Simulation.FakeDiscord;

namespace Sanakan.Simulation;

// Replays the production traffic mix: commands by name and hour of day from tools/traffic_mix.py --json,
// players weighted by their activity, chat messages between commands; samples the bot's memory and CPU.
public sealed class LoadRunner
{
    // Extra: commands sent on top of the mix, one every ExtraEvery, so rare ones get exercised; api: names go to the
    // web API, one every ApiExtraEvery. Api: the web API traffic of the mix, at the same speed and scale as the commands.
    public sealed record Settings(double Minutes, double Speed, double Scale, double ChatPerHour, int StartHour, string Prefix,
        IReadOnlyList<string> Extra = null, bool Api = false);

    private static readonly TimeSpan ExtraEvery = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ApiExtraEvery = TimeSpan.FromSeconds(2);

    private static readonly string[] Words =
    {
        "no", "to", "jest", "ale", "karta", "pakiet", "kto", "ma", "dzisiaj", "anime", "manga", "fajne", "serio", "xd", "wyprawa",
        "nie", "tak", "czemu", "bo", "znowu", "ultimate", "SSS", "mam", "chce", "wymienię", "się", "z", "kimś", "na", "postać",
    };

    private readonly World _world;
    private readonly Gateway _gateway;
    private readonly RestApi _rest;
    private readonly ReplyTracker _replies;
    private readonly BotProcess _bot;
    private readonly SimLog _log;
    private readonly Settings _settings;
    private readonly Random _random = new(7);

    private readonly double[] _hourly;
    private readonly List<(string Name, double Weight)> _commands;
    private readonly List<SimUser> _players;
    private readonly double[] _playerWeights;
    private readonly List<SimUser> _chatters;
    private readonly Dictionary<string, int> _sent = new();
    private readonly List<(double Seconds, long WorkingSet, double Cpu)> _samples = new();
    private int _chatSent;
    private readonly ApiRunner _api;
    private readonly Dictionary<string, double> _apiRates;
    private readonly List<string> _extraCommands;
    private readonly List<string> _extraApi;

    public LoadRunner(World world, Gateway gateway, RestApi rest, ReplyTracker replies, BotProcess bot, SimLog log, JsonNode mix, Settings settings)
    {
        _world = world;
        _gateway = gateway;
        _rest = rest;
        _replies = replies;
        _bot = bot;
        _log = log;
        _settings = settings;

        var unknown = settings.Extra?.Where(x => !CommandCatalog.Templates.ContainsKey(x) && !ApiCatalog.Templates.ContainsKey(x)).ToList();
        if (unknown is { Count: > 0 })
            throw new ArgumentException($"--extra: no template in CommandCatalog or ApiCatalog for {string.Join(", ", unknown)}");
        _extraCommands = settings.Extra?.Where(x => CommandCatalog.Templates.ContainsKey(x)).ToList() ?? new List<string>();
        _extraApi = settings.Extra?.Where(x => ApiCatalog.Templates.ContainsKey(x)).ToList() ?? new List<string>();
        _apiRates = settings.Api ? ApiRunner.RatesFromMix(mix) : new Dictionary<string, double>();

        _hourly = mix["commands"]!["avg_per_hour_of_day"]!.AsArray().Select(x => (double)x!).ToArray();
        _commands = mix["commands"]!["by_name"]!.AsObject()
            .Where(x => CommandCatalog.Templates.ContainsKey(x.Key))
            .Select(x => (x.Key, (double)x.Value!["count"]!)).ToList();

        var active = (int?)mix["users"]?["active"] ?? 330;
        _players = world.Users.Where(x => x.Cards.Count > 0).Take(active).ToList();
        _playerWeights = _players.Select(x => Math.Max(1, x.Weight)).ToArray();
        _chatters = world.Users.Take(3000).ToList();
        if (settings.Api || _extraApi.Count > 0)
            _api = new ApiRunner(bot, log, _players);

        var covered = _commands.Sum(x => x.Weight) / mix["commands"]!["by_name"]!.AsObject().Sum(x => (double)x.Value!["count"]!);
        _log.Info($"load: {_commands.Count} commands cover {covered:P1} of the logged traffic, {_players.Count} players, {_chatters.Count} chatters");
    }

    public async Task<JsonObject> RunAsync()
    {
        if (_api != null)
        {
            await _api.PrepareAsync();
            if (_apiRates.Count > 0)
                _log.Info($"api: {_apiRates.Count} routes of the mix, {_apiRates.Values.Sum() * 60 * _settings.Speed * _settings.Scale:0} requests per hour");
        }

        var watch = Stopwatch.StartNew();
        var duration = TimeSpan.FromMinutes(_settings.Minutes);
        var start = DateTime.Today.AddHours(_settings.StartHour);
        var meanHourly = _hourly.Average();
        var tick = TimeSpan.FromMilliseconds(100);
        var nextSample = TimeSpan.Zero;
        var nextStatus = TimeSpan.FromMinutes(1);
        var nextExtra = ExtraEvery;
        var extraIndex = 0;
        var nextApiExtra = ApiExtraEvery;
        var apiExtraIndex = 0;
        var lastCpu = TimeSpan.Zero;
        var lastCpuAt = TimeSpan.Zero;

        while (watch.Elapsed < duration && !_bot.HasExited)
        {
            var virtualTime = start + watch.Elapsed * _settings.Speed;
            var commandsPerHour = _hourly[virtualTime.Hour] * _settings.Scale;
            var chatPerHour = _settings.ChatPerHour * _hourly[virtualTime.Hour] / meanHourly * _settings.Scale;
            var perTick = _settings.Speed * tick.TotalHours;

            for (int i = Poisson(commandsPerHour * perTick); i > 0; i--)
                _ = SendCommandAsync();
            for (int i = Poisson(chatPerHour * perTick); i > 0; i--)
                _ = SendChatAsync();
            if (_extraCommands.Count > 0 && watch.Elapsed >= nextExtra)
            {
                _ = SendCommandAsync(_extraCommands[extraIndex++ % _extraCommands.Count]);
                nextExtra += ExtraEvery;
            }
            foreach (var (name, perMinute) in _apiRates)
            {
                for (int i = Poisson(perMinute * 60 * _settings.Scale * perTick); i > 0; i--)
                    _ = _api.SendAsync(name);
            }
            if (_extraApi.Count > 0 && watch.Elapsed >= nextApiExtra)
            {
                _ = _api.SendAsync(_extraApi[apiExtraIndex++ % _extraApi.Count]);
                nextApiExtra += ApiExtraEvery;
            }

            if (watch.Elapsed >= nextSample)
            {
                var process = _bot.Process;
                process.Refresh();
                var cpu = process.TotalProcessorTime;
                var cpuPercent = lastCpuAt == TimeSpan.Zero ? 0
                    : (cpu - lastCpu).TotalMilliseconds / (watch.Elapsed - lastCpuAt).TotalMilliseconds / Environment.ProcessorCount * 100;
                lastCpu = cpu;
                lastCpuAt = watch.Elapsed;
                _samples.Add((watch.Elapsed.TotalSeconds, process.WorkingSet64, cpuPercent));
                nextSample += TimeSpan.FromSeconds(5);
            }

            if (watch.Elapsed >= nextStatus)
            {
                var last = _samples.LastOrDefault();
                _log.Info($"load: {watch.Elapsed:mm\\:ss} virtual {virtualTime:HH:mm}, commands {_sent.Values.Sum()}, chat {_chatSent}, " +
                    $"replies {_replies.Answered.Count}, waiting {_replies.Waiting}, bot {last.WorkingSet / 1024 / 1024} MiB, CPU {last.Cpu:0}%");
                nextStatus += TimeSpan.FromMinutes(1);
            }

            await Task.Delay(tick);
        }

        _log.Info("load: finished sending, waiting 30 s for the last replies");
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (_replies.Waiting > 0 && DateTime.UtcNow < deadline)
            await Task.Delay(250);

        return Result(watch.Elapsed);
    }

    private int Poisson(double lambda)
    {
        if (lambda <= 0) return 0;
        var limit = Math.Exp(-lambda);
        var product = _random.NextDouble();
        var count = 0;
        while (product > limit)
        {
            count++;
            product *= _random.NextDouble();
        }
        return count;
    }

    private T Weighted<T>(IReadOnlyList<T> items, Func<int, double> weight, double total)
    {
        var target = _random.NextDouble() * total;
        for (int i = 0; i < items.Count; i++)
        {
            target -= weight(i);
            if (target <= 0) return items[i];
        }
        return items[^1];
    }

    private async Task SendCommandAsync(string forced = null)
    {
        string name;
        SimUser user;
        string text;
        string followUp;
        SimChannel channel;
        lock (_random)
        {
            name = forced ?? Weighted(_commands, i => _commands[i].Weight, _commands.Sum(x => x.Weight)).Name;
            user = Weighted(_players, i => _playerWeights[i], _playerWeights.Sum());
            var template = CommandCatalog.Templates[name];
            var context = new CommandCatalog.Context { User = user, World = _world, Random = _random };
            text = template.Text(context);
            followUp = template.FollowUp?.Invoke(context);
            channel = _world.Channel(template.Channel, _random);
            _sent[name] = _sent.GetValueOrDefault(name) + 1;
        }

        var message = _world.MessageJson(Snowflake.Next(), channel.Id, user, _settings.Prefix + text);
        _rest.StoreUserMessage(message);
        _replies.Sent(channel.Id, user.Id, name);
        await _gateway.DispatchAsync("MESSAGE_CREATE", message);
        if (followUp == null) return;

        await Task.Delay(TimeSpan.FromSeconds(1.5));
        var next = _world.MessageJson(Snowflake.Next(), channel.Id, user, followUp);
        _rest.StoreUserMessage(next);
        await _gateway.DispatchAsync("MESSAGE_CREATE", next);
    }

    private Task SendChatAsync()
    {
        SimUser user;
        SimChannel channel;
        string text;
        lock (_random)
        {
            user = _chatters[(int)Math.Min(_chatters.Count - 1, Math.Pow(_random.NextDouble(), 2) * _chatters.Count)];
            channel = _world.Channel(ChannelKind.Chat, _random);
            var words = _random.Next(1, 25);
            text = string.Join(" ", Enumerable.Range(0, words).Select(_ => Words[_random.Next(Words.Length)]));
            _chatSent++;
        }

        var message = _world.MessageJson(Snowflake.Next(), channel.Id, user, text);
        _rest.StoreUserMessage(message);
        return _gateway.DispatchAsync("MESSAGE_CREATE", message);
    }

    private JsonObject Result(TimeSpan elapsed)
    {
        var answered = _replies.Answered.Where(x => x.Exact).GroupBy(x => x.Command).ToDictionary(x => x.Key, x => x.Select(y => y.Ms).OrderBy(y => y).ToList());
        var anyReply = _replies.Answered.GroupBy(x => x.Command).ToDictionary(x => x.Key, x => x.Count());
        var commands = new JsonObject();
        foreach (var (name, sent) in _sent.OrderByDescending(x => x.Value))
        {
            var ms = answered.GetValueOrDefault(name) ?? new List<double>();
            commands[name] = new JsonObject
            {
                ["sent"] = sent,
                ["replied"] = anyReply.GetValueOrDefault(name),
                ["timed"] = ms.Count,
                ["p50_ms"] = ms.Count > 0 ? Math.Round(ms[ms.Count / 2]) : null,
                ["p95_ms"] = ms.Count > 0 ? Math.Round(ms[(int)Math.Min(ms.Count - 1, ms.Count * 0.95)]) : null,
                ["max_ms"] = ms.Count > 0 ? Math.Round(ms[^1]) : null,
            };
        }

        var memory = _samples.Select(x => x.WorkingSet / 1024.0 / 1024).ToList();
        var cpu = _samples.Skip(1).Select(x => x.Cpu).ToList();
        return new JsonObject
        {
            ["settings"] = JsonSerializer.SerializeToNode(_settings),
            ["elapsed_minutes"] = Math.Round(elapsed.TotalMinutes, 1),
            ["virtual_hours"] = Math.Round(elapsed.TotalHours * _settings.Speed, 1),
            ["commands_sent"] = _sent.Values.Sum(),
            ["chat_sent"] = _chatSent,
            ["bot_messages"] = _rest.SentMessages,
            ["unanswered"] = _replies.Waiting,
            ["bot_errors"] = _bot.Errors.Count,
            ["memory_mib"] = memory.Count == 0 ? null : new JsonObject { ["min"] = Math.Round(memory.Min()), ["avg"] = Math.Round(memory.Average()), ["max"] = Math.Round(memory.Max()), ["last"] = Math.Round(memory[^1]) },
            ["cpu_percent"] = cpu.Count == 0 ? null : new JsonObject { ["avg"] = Math.Round(cpu.Average(), 1), ["max"] = Math.Round(cpu.Max(), 1) },
            ["commands"] = commands,
            ["api"] = _api?.Result(),
        };
    }
}
