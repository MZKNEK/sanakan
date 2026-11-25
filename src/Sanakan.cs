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
using System.Linq;
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
            await StepAsync("config", ExitCodes.BadConfig, () =>
            {
                ImageProcessing.LimitMemoryPool();
                LoadConfig();
                MigrateConfig();
                ValidateConfig();
                return Task.CompletedTask;
            });

            _time = new SystemTime();
            await StepAsync("database", ExitCodes.Restart, EnsureDbIsCreatedAsync);

            await StepAsync("modules", ExitCodes.Restart, () =>
            {
                CreateModules();
                WarnAboutWeakAppKeys();
                AddSigTermHandler();
                return Task.CompletedTask;
            });

            var connected = new TaskCompletionSource();
            _client.Ready += () =>
            {
                _logger.Log($"Discord: connected as {_client.CurrentUser}, guilds: {_client.Guilds.Count}");
                connected.TrySetResult();
                return Task.CompletedTask;
            };
            // the client logs only errors, a gateway that closes the connection right away would go unnoticed
            _client.Disconnected += ex =>
            {
                if (!connected.Task.IsCompleted)
                    _logger.LogError(StartupDiagnostics.DescribeDisconnect(ex));
                return Task.CompletedTask;
            };

            await StepAsync("Discord login", ExitCodes.Restart, async () =>
            {
                var tmpCnf = _config.Get();
                await _client.LoginAsync(TokenType.Bot, tmpCnf.BotToken);
                await _client.SetGameAsync(tmpCnf.Prefix + "pomoc");
                await _client.StartAsync();
            });

            var serviceCollection = CreateServices();
            await StepAsync("commands", ExitCodes.Restart, async () =>
            {
                var services = serviceCollection.BuildServiceProvider();
                _executor.Initialize(services);
                _sessions.Initialize(services);
                await _handler.InitializeAsync(services, _helper);
            });

            await StepAsync("API", ExitCodes.Restart, () => BotWebHost.StartAsync(_config, serviceCollection, _logger));

            await StepAsync("background services", ExitCodes.Restart, () =>
            {
                _heartbeat.Start();
                _ingest.Start();
                _waifu.ImageQueue.Start();
                return Task.CompletedTask;
            });

            _logger.Log($"Bot: Sanakan {HealthMonitor.GetVersion()} started, API: {BotWebHost.Url}");

            StartLog.Log("Start: waiting for Discord");
            if (await Task.WhenAny(connected.Task, Task.Delay(StartupDiagnostics.DiscordReadyWarning)) != connected.Task)
                _logger.LogError(StartupDiagnostics.NotReadyWarning(StartupDiagnostics.DiscordReadyWarning));

            await connected.Task;
            ReadyMarker.Write(Environment.GetEnvironmentVariable(ReadyMarker.Variable), _logger);

            await Task.Delay(-1);
        }

        private ILogger StartLog => new ConsoleLogger(_config);

        // the last "Start:" line shows where a start that hangs got stuck
        private async Task StepAsync(string name, int exitCode, Func<Task> step)
        {
            StartLog.Log($"Start: {name}");
            try
            {
                // on the thread pool, a step that blocks (the database connection retries) still gets reported
                var running = Task.Run(step);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                while (await Task.WhenAny(running, Task.Delay(StartupDiagnostics.SlowStepWarning)) != running)
                    StartLog.LogError(StartupDiagnostics.SlowStep(name, watch.Elapsed));

                await running;
            }
            catch (Exception ex)
            {
                StartLog.LogError($"Start: {name} failed: {ex}");
                StopAfterFailedStart(exitCode);
            }
        }

        private static void StopAfterFailedStart(int exitCode)
        {
            // F5 in VS Code runs the bot in its own console window, which closes together with the bot
            if (System.Diagnostics.Debugger.IsAttached)
            {
                Console.WriteLine("Press Enter to close.");
                Console.ReadLine();
            }
            Environment.Exit(exitCode);
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
                StopAfterFailedStart(ExitCodes.BadConfig);
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
                db.EnsureReady(new ConsoleLogger(_config));
                _tags = await TagHelper.CreateAsync(db, _time);
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
            Extensions.EmbedAuthorBuilderExtension.PublicApiUrl = () => _config.Get().PublicApiUrl;

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
            _apiGuard = new Api.TokenAttemptGuard(_time);
            if (!BuildInfo.IsDebug)
                MemoryReport.StartStoring(_config, _time, _logger, TimeSpan.FromMinutes(5));
            _events = new Events(_time);
            var fetchProxy = new FetchProxy(_config, _logger);
            _helper = new Helper(_config, _logger, fetchProxy.CreateHandler(new System.Net.Http.HttpClientHandler { AllowAutoRedirect = false }));
            _expedition = new Expedition(_time);
            _img = new ImageProcessing(_shindenClient,
                _tags.GetTag(Services.PocketWaifu.TagType.Gallery), logger: _logger, handler: fetchProxy.CreateHandler());
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
            _img.CurrentCharacterPicture = id => _shinden.GetCurrentPictureAsync(id);
            _img.CharacterPictureMoved = (id, oldUrl, newUrl) => _waifu.MoveCharacterPictureAsync(id, oldUrl, newUrl);
            _waifu.ImageQueue = new CardImageQueue((id, force) => _waifu.DrawInBackgroundAsync(id, force), _logger);
            if (BuildInfo.IsDebug)
            {
                StartLog.Log("Start: Debug build, background work that writes to the database is off (penalty sweep, KC recount, "
                    + "game telemetry, RAM history, profile check, greetings and left users cleanup, safari, experience from messages)");
            }
            else
            {
                WishlistRecount.Start(_config, _time, _logger);
                StartTelemetry();
            }
            _supervisor = new Supervisor(_client, _config, _logger, _mod, _time, _scamImages);
            _greeting = new Greeting(_client, _logger, _config, _executor, _time);
            _exp = new ExperienceManager(_client, _executor, _config, _img, _time, _logger);
            _spawn = new Spawn(_client, _executor, _waifu, _config, _logger, _time);
            _handler = new CommandHandler(_client, _config, _logger, _executor, _time);
            _profile = new Profile(_client, _shindenClient, _img, _logger, _config, _time, _executor);
        }

        private void StartTelemetry()
        {
            var shops = new[] { (ShopType.Normal, CurrencyType.TC), (ShopType.Pvp, CurrencyType.PC), (ShopType.Activity, CurrencyType.AC) };
            GameTelemetry.Default.ExcludedUsers = () => _config.Get().Dev;
            GameTelemetry.Default.Start(() => new Database.DatabaseContext(_config), _time, _logger,
                () => shops.SelectMany(shop => _waifu.GetItemsWithCostForShop(shop.Item1).Select((x, i) => new GameTelemetry.ShopPrice
                {
                    Label = _waifu.GetShopItemLabel(shop.Item1, i + 1, x),
                    Currency = shop.Item2,
                    Cost = x.Cost,
                })).ToList());

            // what was counted since the last flush
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                try { GameTelemetry.Default.FlushAsync(() => new Database.DatabaseContext(_config)).GetAwaiter().GetResult(); }
                catch (Exception ex) { Console.Error.WriteLine($"Telemetry: flush on exit: {ex.Message}"); }
            };
        }

        private void LoadConfig()
        {
#if !DEBUG
            var (file, debug) = ("Config.json", false);
#else
            var (file, debug) = ("ConfigDebug.json", true);
#endif
            var path = System.IO.Path.GetFullPath(file);
            var existed = System.IO.File.Exists(path);
            StartLog.Log($"Start: reading {path}");
            _config = new ConfigManager(file, debug);

            if (!existed && debug)
                StartLog.LogError($"Config: {file} did not exist, an empty one was created at {path}, fill it in (BotToken first)");
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
                    Environment.Exit(ExitCodes.Stop);
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
