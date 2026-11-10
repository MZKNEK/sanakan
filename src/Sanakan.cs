#pragma warning disable 1591

using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Sanakan.Api;
using Sanakan.Config;
using Sanakan.Services;
using Sanakan.Services.Commands;
using Sanakan.Services.Executor;
using Sanakan.Services.PocketWaifu;
using Sanakan.Services.ScamImages;
using Sanakan.Services.Session;
using Sanakan.Services.Supervisor;
using Sanakan.Services.Time;
using Sanakan.ShindenCompat;
using System;
using System.Threading.Tasks;

namespace Sanakan
{
    class Sanakan
    {
        private UserBasedExecutor _executor;
        private ShindenClient _shindenClient;
        private DiscordSocketClient _client;
        private Services.Shinden _shinden;
        private SessionManager _sessions;
        private CommandHandler _handler;
        private ExperienceManager _exp;
        private Supervisor _supervisor;
        private Expedition _expedition;
        private EmoteCounter _eCounter;
        private ImageProcessing _img;
        private ScamImageScanner _scamImages;
        private Api.TokenAttemptGuard _apiGuard;
        private DeletedLog _deleted;
        private DmTrash _dmTrash;
        private HealthMonitor _health;
        private Heartbeat _heartbeat;
        private DiscordIngest _ingest;
        private Daemonizer _daemon;
        private Greeting _greeting;
        private ISystemTime _time;
        private Profile _profile;
        private IConfig _config;
        private TagHelper _tags;
        private ILogger _logger;
        private Moderator _mod;
        private Helper _helper;
        private Events _events;
        private Waifu _waifu;
        private Spawn _spawn;
        private Chaos _chaos;

        public static void Main() => new Sanakan().MainAsync().GetAwaiter().GetResult();

        public async Task MainAsync()
        {
            ImageProcessing.LimitMemoryPool();
            LoadConfig();
            MigrateConfig();
            ValidateConfig();

            await EnsureDbIsCreatedAsync();

            CreateModules();
            WarnAboutWeakAppKeys();
            AddSigTermHandler();

            _client.Ready += () =>
            {
                _logger.Log($"Discord: connected as {_client.CurrentUser}, guilds: {_client.Guilds.Count}");
                return Task.CompletedTask;
            };

            var tmpCnf = _config.Get();
            await _client.LoginAsync(TokenType.Bot, tmpCnf.BotToken);
            await _client.SetGameAsync(tmpCnf.Prefix + "pomoc");
            await _client.StartAsync();

            var serviceCollection = CreateServices();
            var services = serviceCollection.BuildServiceProvider();
            _executor.Initialize(services);
            _sessions.Initialize(services);
            await _handler.InitializeAsync(services, _helper);

            await BotWebHost.StartAsync(_config, serviceCollection, _logger);

            _heartbeat.Start();
            _ingest.Start();

            _logger.Log($"Bot: Sanakan {HealthMonitor.GetVersion()} started, API: {BotWebHost.Url}");

            await Task.Delay(-1);
        }

        private static string DescribeDiscordLog(LogMessage log)
            => log.Exception == null ? $"Discord: {log.Source}: {log.Message}" : $"Discord: {log.Source}: {log.Message} - {log.Exception}";

        private void MigrateConfig()
        {
            var migrated = LegacySiteKeys.Migrate(_config.Get());
            if (migrated == null) return;

            _config.Save();
            Console.WriteLine($"Config: moved ApiKeys to UserKeyApps with the Site permission: {string.Join(", ", migrated)}");
        }

        private void ValidateConfig()
        {
            try
            {
                BotWebHost.ValidateConfig(_config.Get());
            }
            catch (InvalidOperationException ex)
            {
                Console.Error.WriteLine($"Config: invalid configuration: {ex.Message}");
                Environment.Exit(1);
            }
        }

        private void WarnAboutWeakAppKeys()
        {
            var weak = BotWebHost.WeakAppKeys(_config.Get());
            if (weak.Count == 0) return;

            _logger.Log($"API: x-app-key keys shorter than {BotWebHost.MinAppKeyLength} characters, regenerate them with ukapp add: {string.Join(", ", weak)}");
        }

        private async Task EnsureDbIsCreatedAsync()
        {
            using (var db = new Database.DatabaseContext(_config))
            {
                db.EnsureReady();
                _tags = await TagHelper.CreateAsync(db);
            }
        }

        private void CreateModules()
        {
            Services.Dir.Create();

            var discordLogger = new DiscordChannelLogger(new ConsoleLogger(_config), _config);
            _logger = discordLogger;
            Database.DbActivity.Start(_logger, TimeSpan.FromHours(1));
            QueryCacheStats.Start(_logger, TimeSpan.FromHours(1));
            Func<string> discordMemory = () => MemoryReport.DescribeDiscord(_client);
            MemoryReport.Start(_logger, discordMemory, TimeSpan.FromHours(1));
            DailyReport.Start(_logger, Api.ApiStats.FlushDaily, Database.DbActivity.FlushDaily, QueryCacheStats.FlushDaily,
                () => MemoryReport.FlushDaily(discordMemory));

            AppDomain.CurrentDomain.ProcessExit += (_, _) => discordLogger.FlushAllAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();

            var socketConfig = new DiscordSocketConfig()
            {
                GatewayIntents = GatewayIntents.All & ~GatewayIntents.GuildPresences,
                LogLevel = LogSeverity.Error,
                AlwaysDownloadUsers = true,
                MessageCacheSize = 250
            };

            var endpoints = _config.Get();
            if (!string.IsNullOrWhiteSpace(endpoints.DiscordGatewayUrl))
                socketConfig.GatewayHost = endpoints.DiscordGatewayUrl;
            if (!string.IsNullOrWhiteSpace(endpoints.DiscordApiUrl))
                socketConfig.RestClientProvider = _ => Discord.Net.Rest.DefaultRestClientProvider.Instance(endpoints.DiscordApiUrl);

            _client = new DiscordSocketClient(socketConfig);

            _client.Log += log =>
            {
                if (log.Severity <= LogSeverity.Error)
                    _logger.LogError(DescribeDiscordLog(log));
                else
                    _logger.Log(DescribeDiscordLog(log));
                return Task.CompletedTask;
            };

            var tmpCnf = _config.Get();
            _shindenClient = ShindenClient.Create(tmpCnf.Shinden, discordLogger.Shinden,
                new ShindenActivityHandler(ShindenActivity.Default), TimeSpan.FromSeconds(10));

            _health = new HealthMonitor(_client, _shindenClient, _config);
            _heartbeat = new Heartbeat(_health.GetAsync, _config, _logger);
            _ingest = new DiscordIngest(_client, _config, _logger);
            _time = new SystemTime();
            _apiGuard = new Api.TokenAttemptGuard(_time);
            MemoryReport.StartStoring(_config, _time, _logger, TimeSpan.FromMinutes(5));
            _events = new Events(_time);
            _helper = new Helper(_config, _logger);
            _expedition = new Expedition(_time);
            _img = new ImageProcessing(_shindenClient,
                _tags.GetTag(Services.PocketWaifu.TagType.Gallery), logger: _logger);
            _scamImages = new ScamImageScanner(_img,
                new ScamImageStore(Services.Dir.GetResource("ScamHashes.txt")), _logger,
                () => _config.Get().ScamImageMaxDistance);
            _deleted = new DeletedLog(_client, _config, _logger);
            _dmTrash = new DmTrash(_client, _logger);
            _chaos = new Chaos(_client, _config, _logger);
            _executor = new UserBasedExecutor(_logger);
            _eCounter = new EmoteCounter(_client, _time, _logger);
            _sessions = new SessionManager(_client, _executor, _logger);
            _mod = new Moderator(_logger, _config, _client, _time, _img);
            _daemon = new Daemonizer(_client, _logger, _config);
            _shinden = new Services.Shinden(_shindenClient, _sessions, _img, _logger, _time, Services.Dir.ShindenCache);
            _shinden.CustomCharacterInfo = async id =>
            {
                using var db = new Database.DatabaseContext(_config);
                return await Extensions.ContextExtension.GetCustomCharacterInfoAsync(db, id);
            };
            AppDomain.CurrentDomain.ProcessExit += (_, _) => _shinden.SaveCacheAsync().GetAwaiter().GetResult();
            _shinden.StartSeasonRefresh(TimeSpan.FromHours(1));
            _waifu = new Waifu(_img, _shindenClient, _events, _logger,
                 _expedition, _client, _helper, _time, _shinden, _tags, _config);
            WishlistRecount.Start(_config, _time, _logger);
            _supervisor = new Supervisor(_client, _config, _logger, _mod, _time, _scamImages);
            _greeting = new Greeting(_client, _logger, _config, _executor, _time);
            _exp = new ExperienceManager(_client, _executor, _config, _img, _time, _logger);
            _spawn = new Spawn(_client, _executor, _waifu, _config, _logger, _time);
            _handler = new CommandHandler(_client, _config, _logger, _executor, _time);
            _profile = new Profile(_client, _shindenClient, _img, _logger, _config, _time, _executor);
        }

        private void LoadConfig()
        {
#if !DEBUG
            _config = new ConfigManager("Config.json");
#else
            _config = new ConfigManager("ConfigDebug.json", true);
#endif
        }

        private void AddSigTermHandler()
        {
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                _ = Task.Run(async () =>
                {
                    _logger.Log("Bot: SIGTERM received");
                    await _client.LogoutAsync();
                    await BotWebHost.StopAsync(TimeSpan.FromSeconds(5));
                    _executor?.Dispose();
                    await Task.Delay(1000);
                    Environment.Exit(0);
                });
            };
        }

        private IServiceCollection CreateServices()
        {
            return new ServiceCollection()
                .AddSingleton<IExecutor>(_executor)
                .AddSingleton(_shindenClient)
                .AddSingleton(_heartbeat)
                .AddSingleton(_health)
                .AddSingleton(_ingest)
                .AddSingleton(_expedition)
                .AddSingleton(_sessions)
                .AddSingleton(_eCounter)
                .AddSingleton(_profile)
                .AddSingleton(_shinden)
                .AddSingleton(_config)
                .AddSingleton(_logger)
                .AddSingleton(_client)
                .AddSingleton(_helper)
                .AddSingleton(_events)
                .AddSingleton(_chaos)
                .AddSingleton(_waifu)
                .AddSingleton(_spawn)
                .AddSingleton(_tags)
                .AddSingleton(_time)
                .AddSingleton(_mod)
                .AddSingleton(_exp)
                .AddSingleton(_img)
                .AddSingleton(_scamImages)
                .AddSingleton<Api.ITokenAttemptGuard>(_apiGuard)
                .AddSingleton<Services.Fun>()
                .AddSingleton<Services.LandManager>()
                .AddSingleton<Services.PocketWaifu.Lottery>();
        }
    }
}
