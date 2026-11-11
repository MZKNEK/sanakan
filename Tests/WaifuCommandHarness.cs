using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
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

        public FixedTime Time { get; } = new FixedTime();
        public FakeConfig Config { get; } = new FakeConfig();
        public TagHelper Tags { get; private set; }

        public WaifuCommandHarness()
        {
            Config.Model.DatabaseProvider = DatabaseProvider.Sqlite;
            Config.Model.ConnectionString = $"Data Source={_path}";
        }

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
            var module = new WaifuModule(waifu ?? (Waifu)FormatterServices.GetUninitializedObject(typeof(Waifu)), null, new ListLogger(), null,
                new Sanakan.Services.Session.SessionManager(_client, Mock.Of<IExecutor>(), new ListLogger()), Config, null, null, Time, null, null, Tags, null)
            {
                Config = Config,
                Logger = new ListLogger(),
            };
            var channel = new Mock<ISocketMessageChannel>();
            channel.SetReturnsDefault(Task.FromResult(Mock.Of<IUserMessage>()));
            var context = new SocketCommandContext(_client, SocketTestObjects.Message(SocketTestObjects.User(caller), ".cmd", channel.Object));
            typeof(ModuleBase<SocketCommandContext>).GetProperty("Context").SetValue(module, context);

            await command(module);

            return channel.Invocations.Where(x => x.Method.Name == "SendMessageAsync").SelectMany(x => x.Arguments).OfType<Embed>().Single().Description;
        }

        public void Dispose()
        {
            _client.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(_path); } catch (IOException) { }
        }
    }
}
