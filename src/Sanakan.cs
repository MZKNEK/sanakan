﻿#pragma warning disable 1591

using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Sanakan.Api;
using Sanakan.Config;
using Sanakan.Services;
using Sanakan.Services.Commands;
using Sanakan.Services.Executor;
using Sanakan.Services.PocketWaifu;
using Sanakan.Services.Session;
using Sanakan.Services.Supervisor;
using Sanakan.Services.Time;
using Shinden;
using Shinden.Logger;
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
        private DeletedLog _deleted;
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
            LoadConfig();
            EnsureDbIsCreated();
            CreateModules();
            AddSigTermHandler();

            var tmpCnf = _config.Get();
            await _client.LoginAsync(TokenType.Bot, tmpCnf.BotToken);
            await _client.SetGameAsync(tmpCnf.Prefix + "pomoc");
            await _client.StartAsync();

            var services = BuildServiceProvider();
            BotWebHost.RunWebHost(_client, _shindenClient, _waifu,
                _config, _helper, _executor, _logger, _time, _tags, _expedition);

            _executor.Initialize(services);
            _sessions.Initialize(services);
            await _handler.InitializeAsync(services, _helper);

            await Task.Delay(-1);
        }

        private void EnsureDbIsCreated()
        {
            using (var db = new Database.DatabaseContext(_config))
            {
                db.Database.EnsureCreated();
                _tags = new TagHelper(db);
            }
        }

        private void CreateModules()
        {
            Services.Dir.Create();

            _logger = new ConsoleLogger();

            _client = new DiscordSocketClient(new DiscordSocketConfig()
            {
                GatewayIntents = GatewayIntents.All,
                LogLevel = LogSeverity.Error,
                AlwaysDownloadUsers = true,
                MessageCacheSize = 600
            });

            _client.Log += log =>
            {
                _logger.Log(log.ToString());
                return Task.CompletedTask;
            };

            var tmpCnf = _config.Get();
            _shindenClient = new ShindenClient(new Auth(tmpCnf.Shinden.Token,
                tmpCnf.Shinden.UserAgent, tmpCnf.Shinden.Marmolade), _logger,
                LogLevel.Information, tmpCnf.Shinden.BaseUri, TimeSpan.FromSeconds(10));

            _time = new SystemTime();
            _events = new Events(_time);
            _helper = new Helper(_config, _logger);
            _expedition = new Expedition(_time);
            _img = new ImageProcessing(_shindenClient,
                _tags.GetTag(Services.PocketWaifu.TagType.Gallery));
            _deleted = new DeletedLog(_client, _config);
            _chaos = new Chaos(_client, _config, _logger);
            _executor = new UserBasedExecutor(_logger);
            _eCounter = new EmoteCounter(_client, _time);
            _sessions = new SessionManager(_client, _executor, _logger);
            _mod = new Moderator(_logger, _config, _client, _time, _img);
            _daemon = new Daemonizer(_client, _logger, _config);
            _shinden = new Services.Shinden(_shindenClient, _sessions, _img);
            _waifu = new Waifu(_img, _shindenClient, _events, _logger,
                 _expedition, _client, _helper, _time, _shinden, _tags, _config);
            _supervisor = new Supervisor(_client, _config, _logger, _mod, _time);
            _greeting = new Greeting(_client, _logger, _config, _executor, _time);
            _exp = new ExperienceManager(_client, _executor, _config, _img, _time);
            _spawn = new Spawn(_client, _executor, _waifu, _config, _logger, _time);
            _handler = new CommandHandler(_client, _config, _logger, _executor, _time);
            _profile = new Profile(_client, _shindenClient, _img, _logger, _config, _time, _executor);
        }

        private void LoadConfig()
        {
#if !DEBUG
            _config = new ConfigManager("Config.json");
#else
            _config = new ConfigManager("ConfigDebug.json");
#endif
        }

        private void AddSigTermHandler()
        {
            Console.CancelKeyPress += delegate
            {
                _ = Task.Run(async () =>
                {
                    _logger.Log("SIGTERM Received!");
                    await _client.LogoutAsync();
                    await Task.Delay(1000);
                    Environment.Exit(0);
                });
            };
        }

        private IServiceProvider BuildServiceProvider()
        {
            return new ServiceCollection()
                .AddSingleton<IExecutor>(_executor)
                .AddSingleton(_shindenClient)
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
                .AddSingleton<Services.Fun>()
                .AddSingleton<Services.LandManager>()
                .AddSingleton<Services.PocketWaifu.Lottery>()
                .BuildServiceProvider();
        }
    }
}
