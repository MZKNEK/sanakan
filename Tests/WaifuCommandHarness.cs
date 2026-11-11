using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Moq;
using Sanakan.Config.Model;
using Sanakan.Database;
using Sanakan.Services.Executor;
using Sanakan.Services.PocketWaifu;
using WaifuModule = Sanakan.Modules.PocketWaifu;

namespace Artifacts
{
    // runs PocketWaifu commands against a SQLite file and returns the reply embed's text
    public sealed class WaifuCommandHarness : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"sanakan-command-{Guid.NewGuid():N}.db");
        private readonly DiscordSocketClient _client = new DiscordSocketClient();
        private readonly LocalHttpServer _shindenServer;

        // the random character pool; everything else on Shinden answers 404, so character info and seasons are missing
        public List<(ulong Id, string Name)> Pool { get; } = new List<(ulong, string)>();

        public FixedTime Time { get; } = new FixedTime();
        public FakeConfig Config { get; } = new FakeConfig();
        public TagHelper Tags { get; private set; }

        public WaifuCommandHarness()
        {
            _shindenServer = new LocalHttpServer(async ctx =>
            {
                if (ctx.Request.Url.AbsolutePath.Contains("/character/flat-list") && Pool.Count > 0)
                {
                    var list = new Newtonsoft.Json.Linq.JArray(Pool.Select(x => new Newtonsoft.Json.Linq.JObject
                    {
                        ["c"] = x.Id, ["p"] = x.Id, ["f"] = x.Name, ["l"] = "", ["t"] = "Seria", ["n"] = x.Id,
                    }));
                    ctx.Response.ContentType = "application/json";
                    await ctx.Response.OutputStream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(list.ToString()));
                }
                else
                {
                    ctx.Response.StatusCode = 404;
                }
                ctx.Response.Close();
            });
            Config.Model.DatabaseProvider = DatabaseProvider.Sqlite;
            Config.Model.ConnectionString = $"Data Source={_path}";
            var client = Sanakan.ShindenCompat.ShindenClient.Create(new ConfigShinden { Token = "token", UserAgent = "tests",
                Marmolade = "marmolade", BaseUri = _shindenServer.BaseUrl }, null, null, TimeSpan.FromSeconds(5));
            Shinden = new Sanakan.Services.Shinden(client, null, null, new ListLogger(), Time);
        }

        public Sanakan.Services.Shinden Shinden { get; }

        public DatabaseContext Open() => new DatabaseContext(Config);

        public async Task<DatabaseContext> CreateAsync()
        {
            var db = Open();
            db.EnsureReady();
            Tags = await TagHelper.CreateAsync(db, Time);
            return db;
        }

        public async Task<string> RunAsync(ulong caller, Func<WaifuModule, Task> command, Waifu waifu = null)
        {
            waifu ??= new Waifu(null, null, null, new ListLogger(), null, null, null, Time, Shinden, Tags, Config);
            var module = new WaifuModule(waifu, null, new ListLogger(), new Lottery(Shinden),
                new Sanakan.Services.Session.SessionManager(_client, Mock.Of<IExecutor>(), new ListLogger()), Config, null, null, Time, null, Shinden, Tags, new Expedition(Time))
            {
                Config = Config,
                Logger = new ListLogger(),
            };
            var channel = new Mock<ISocketMessageChannel>();
            channel.SetReturnsDefault(Task.FromResult(Mock.Of<IUserMessage>()));
            var context = new SocketCommandContext(_client, SocketTestObjects.Message(SocketTestObjects.User(caller), ".cmd", channel.Object));
            typeof(ModuleBase<SocketCommandContext>).GetProperty("Context").SetValue(module, context);

            await command(module);

            // some replies are sent in the background
            for (var i = 0; i < 100 && !Replies(channel).Any(); i++)
                await Task.Delay(20);
            return string.Join("\n", Replies(channel));
        }

        private static IEnumerable<string> Replies(Mock<ISocketMessageChannel> channel)
            => channel.Invocations.ToList().Where(x => x.Method.Name == "SendMessageAsync").SelectMany(x => x.Arguments).OfType<Embed>().Select(x => x.Description);

        public void Dispose()
        {
            _client.Dispose();
            _shindenServer.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(_path); } catch (IOException) { }
        }
    }
}
