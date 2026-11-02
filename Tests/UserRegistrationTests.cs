using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Newtonsoft.Json.Linq;
using Sanakan.Api.Controllers;
using Sanakan.Api.Models;
using Sanakan.Database;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Xunit;

namespace Artifacts
{
    public class UserRegistrationTests : IDisposable
    {
        private readonly SqliteConnection _connection = new SqliteConnection("Data Source=:memory:");
        private readonly FakeExecutor _executor = new FakeExecutor { RunImmediately = true };
        private Func<ulong, Task<Discord.IUser>> _userLookup = _ => Task.FromResult<Discord.IUser>(null);

        private sealed class TestDatabase : DatabaseContext
        {
            private readonly SqliteConnection _connection;
            public TestDatabase(SqliteConnection connection) : base(new FakeConfig()) => _connection = connection;
            protected override void OnConfiguring(DbContextOptionsBuilder builder) => builder.UseSqlite(_connection);
        }

        private sealed class TestController : UserController
        {
            private readonly SqliteConnection _connection;
            private readonly Func<ulong, Task<Discord.IUser>> _userLookup;

            public TestController(SqliteConnection connection, FakeExecutor executor,
                Func<ulong, Task<Discord.IUser>> userLookup)
                : base(null, null, new ListLogger(), executor, new FakeConfig(), new FixedTime(), null, null, null)
            {
                _connection = connection;
                _userLookup = userLookup;
            }

            protected override DatabaseContext CreateDatabaseContext() => new TestDatabase(_connection);
            protected override Task<Discord.IUser> GetLinkedDiscordUserAsync(ulong discordUserId) => _userLookup(discordUserId);
        }

        public UserRegistrationTests()
        {
            _connection.Open();
            using var db = Open();
            db.Database.EnsureCreated();
        }

        public void Dispose() => _connection.Dispose();
        private TestDatabase Open() => new TestDatabase(_connection);

        private UserController Controller()
            => ControllerHarness.Attach(new TestController(_connection, _executor, _userLookup));

        private static UserRegistration Request(ulong discordId = 10) => new UserRegistration
        {
            DiscordUserId = discordId,
            ShindenUserId = 42
        };

        private async Task SeedAsync(ulong discordId, ulong shindenId)
        {
            using var db = Open();
            var user = new User().Default(discordId, new FixedTime().Now());
            user.Shinden = shindenId;
            user.TcCnt = 1234;
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        [Fact]
        public async Task MissingPlayer_IsCreatedWithoutDiscordCacheOrShindenClient()
        {
            var result = await Controller().RegisterUserAsync(Request());

            Assert.Equal(200, result.StatusOf());
            using var db = Open();
            var user = await db.GetUserOrCreateSimpleAsync(10);
            Assert.Equal(42ul, user.Shinden);
            Assert.NotNull(user.Stats);
            Assert.NotNull(user.SMConfig);
            Assert.NotNull(user.GameDeck);
            Assert.NotNull(user.GameDeck.ExpContainer);
            Assert.Single(await db.Users.ToListAsync());
        }

        [Fact]
        public async Task ExistingUnlinkedPlayer_KeepsItsProgress()
        {
            await SeedAsync(10, 0);

            Assert.Equal(200, (await Controller().RegisterUserAsync(Request())).StatusOf());

            using var db = Open();
            var user = await db.Users.SingleAsync();
            Assert.Equal(42ul, user.Shinden);
            Assert.Equal(1234, user.TcCnt);
        }

        [Fact]
        public async Task ShindenLinkedToAnotherDiscord_ReturnsSpecificConflictWithoutCreatingPlayer()
        {
            await SeedAsync(20, 42);

            var result = await Controller().RegisterUserAsync(Request());

            Assert.Equal(409, result.StatusOf());
            var body = JObject.FromObject(((Microsoft.AspNetCore.Mvc.ObjectResult)result).Value);
            Assert.False(body.Value<bool>("success"));
            Assert.Equal("shinden_account_already_linked", body.Value<string>("code"));
            Assert.Contains("another Discord account", result.MessageOf());
            using var db = Open();
            var user = await db.Users.SingleAsync();
            Assert.Equal(20ul, user.Id);
            Assert.Equal(42ul, user.Shinden);
        }

        [Fact]
        public async Task DiscordLinkedToAnotherShinden_ReturnsDifferentConflictWithoutOverwriting()
        {
            await SeedAsync(10, 99);

            var result = await Controller().RegisterUserAsync(Request());

            Assert.Equal(409, result.StatusOf());
            var body = JObject.FromObject(((Microsoft.AspNetCore.Mvc.ObjectResult)result).Value);
            Assert.Equal("discord_account_already_linked", body.Value<string>("code"));
            using var db = Open();
            Assert.Equal(99ul, (await db.Users.SingleAsync()).Shinden);
        }

        [Fact]
        public async Task RepeatingTheSameLink_IsSuccessfulAndDoesNotCreateDuplicates()
        {
            var controller = Controller();
            Assert.Equal(200, (await controller.RegisterUserAsync(Request())).StatusOf());
            Assert.Equal(200, (await controller.RegisterUserAsync(Request())).StatusOf());

            using var db = Open();
            Assert.Equal(42ul, (await db.Users.SingleAsync()).Shinden);
        }

        [Fact]
        public async Task LinkCreatedWhileWaitingInQueue_IsDetectedBeforeSaving()
        {
            _executor.RunImmediately = false;
            var pending = Controller().RegisterUserAsync(Request());
            Assert.Single(_executor.Added);
            Assert.False(pending.IsCompleted);
            await SeedAsync(20, 42);
            Assert.True(_executor.Added.TryDequeue(out var task));
            await task.ExecuteAsync(new EmptyServiceProvider());

            Assert.Equal(409, (await pending.WaitAsync(TimeSpan.FromSeconds(5))).StatusOf());
            using var db = Open();
            Assert.Equal(20ul, (await db.Users.SingleAsync()).Id);
        }

        [Theory]
        [InlineData(0ul, 42ul)]
        [InlineData(10ul, 0ul)]
        public async Task InvalidRequest_Returns400WithoutQueueing(ulong discordId, ulong shindenId)
        {
            var request = new UserRegistration { DiscordUserId = discordId, ShindenUserId = shindenId };

            Assert.Equal(400, (await Controller().RegisterUserAsync(request)).StatusOf());
            Assert.Empty(_executor.Added);
        }

        [Fact]
        public async Task FullQueue_DoesNotCreateOrLinkPlayer()
        {
            _executor.Accept = false;

            Assert.Equal(503, (await Controller().RegisterUserAsync(Request())).StatusOf());

            using var db = Open();
            Assert.False(await db.Users.AnyAsync());
        }

        private static JObject Body(Microsoft.AspNetCore.Mvc.IActionResult result)
            => JObject.FromObject(((Microsoft.AspNetCore.Mvc.ObjectResult)result).Value);

        [Fact]
        public async Task Conflict_ReturnsAllLinkedDiscordAccountsWithDisplayDataAndPreciseIds()
        {
            const ulong firstId = 123456789012345678;
            const ulong secondId = 223456789012345678;
            await SeedAsync(firstId, 42);
            await SeedAsync(secondId, 42);
            var member = new Mock<Discord.IGuildUser>();
            member.SetupGet(x => x.Username).Returns("discord-login");
            member.SetupGet(x => x.GlobalName).Returns("Display name");
            member.SetupGet(x => x.Nickname).Returns("Guild nickname");
            member.Setup(x => x.GetAvatarUrl(It.IsAny<Discord.ImageFormat>(), It.IsAny<ushort>()))
                .Returns("https://cdn.discordapp.com/avatars/test.png?size=128");
            _userLookup = _ => Task.FromResult<Discord.IUser>(member.Object);

            var result = await Controller().RegisterUserAsync(Request());

            Assert.Equal(409, result.StatusOf());
            var accounts = (JArray)Body(result)["accounts"];
            Assert.Equal(2, accounts.Count);
            Assert.Equal(firstId.ToString(), accounts[0].Value<string>("discordUserId"));
            Assert.Equal(secondId.ToString(), accounts[1].Value<string>("discordUserId"));
            foreach (var account in accounts)
            {
                Assert.Equal(JTokenType.String, account["discordUserId"].Type);
                Assert.Equal("42", account.Value<string>("shindenUserId"));
                Assert.Equal("discord-login", account.Value<string>("username"));
                Assert.Equal("Guild nickname", account.Value<string>("nickname"));
                Assert.Equal("https://cdn.discordapp.com/avatars/test.png", account.Value<string>("avatarUrl"));
            }
        }

        [Theory]
        [InlineData("Display name", "Display name")]
        [InlineData(null, "discord-login")]
        public async Task Conflict_WithoutGuildNickname_UsesDisplayNameAndDefaultAvatar(string globalName, string expectedNickname)
        {
            await SeedAsync(20, 42);
            var user = new Mock<Discord.IUser>();
            user.SetupGet(x => x.Username).Returns("discord-login");
            user.SetupGet(x => x.GlobalName).Returns(globalName);
            user.Setup(x => x.GetDefaultAvatarUrl()).Returns("https://cdn.discordapp.com/embed/avatars/0.png");
            _userLookup = _ => Task.FromResult(user.Object);

            var account = Body(await Controller().RegisterUserAsync(Request()))["accounts"].Single();

            Assert.Equal(expectedNickname, account.Value<string>("nickname"));
            Assert.Equal("https://cdn.discordapp.com/embed/avatars/0.png", account.Value<string>("avatarUrl"));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Conflict_WhenDiscordIsUnavailable_StillReturnsIdsAnd409(bool throws)
        {
            await SeedAsync(20, 42);
            _userLookup = _ => throws ? Task.FromException<Discord.IUser>(new InvalidOperationException("Discord unavailable"))
                : Task.FromResult<Discord.IUser>(null);

            var result = await Controller().RegisterUserAsync(Request());

            Assert.Equal(409, result.StatusOf());
            var account = Body(result)["accounts"].Single();
            Assert.Equal("20", account.Value<string>("discordUserId"));
            Assert.Equal("42", account.Value<string>("shindenUserId"));
            Assert.Null(account.Value<string>("username"));
            Assert.Null(account.Value<string>("nickname"));
            Assert.Null(account.Value<string>("avatarUrl"));
        }

        [Fact]
        public async Task DiscordConflict_ReportsItsCurrentShindenLink()
        {
            await SeedAsync(10, 99);

            var account = Body(await Controller().RegisterUserAsync(Request()))["accounts"].Single();

            Assert.Equal("10", account.Value<string>("discordUserId"));
            Assert.Equal("99", account.Value<string>("shindenUserId"));
        }

        [Fact]
        public async Task Disconnect_PreservesPlayerProgressAndOtherLinks_AndAllowsRegisteringAgain()
        {
            await SeedAsync(10, 42);
            await SeedAsync(20, 42);
            using (var db = Open())
            {
                db.Cards.Add(new Card { Id = 123, Character = 456, GameDeckId = 10 });
                await db.SaveChangesAsync();
            }
            var controller = Controller();

            Assert.Equal(200, (await controller.DisconnectUserAsync(10, 42)).StatusOf());

            using (var db = Open())
            {
                var user = await db.GetUserOrCreateSimpleAsync(10);
                Assert.Equal(0ul, user.Shinden);
                Assert.Equal(1234, user.TcCnt);
                Assert.NotNull(user.Stats);
                Assert.NotNull(user.GameDeck.ExpContainer);
                Assert.Equal(123ul, (await db.Cards.SingleAsync()).Id);
                Assert.Equal(42ul, (await db.Users.SingleAsync(x => x.Id == 20)).Shinden);
            }

            Assert.Equal(200, (await controller.DisconnectUserAsync(10, 42)).StatusOf());
            Assert.Equal(409, (await controller.RegisterUserAsync(Request(30))).StatusOf());
            Assert.Equal(200, (await controller.DisconnectUserAsync(20, 42)).StatusOf());
            Assert.Equal(200, (await controller.RegisterUserAsync(Request(30))).StatusOf());
        }

        [Fact]
        public async Task Disconnect_WithStaleShindenId_ReturnsConflictAndPreservesCurrentLink()
        {
            await SeedAsync(10, 99);

            var result = await Controller().DisconnectUserAsync(10, 42);

            Assert.Equal(409, result.StatusOf());
            Assert.Equal("account_link_mismatch", Body(result).Value<string>("code"));
            Assert.Equal("99", Body(result)["accounts"].Single().Value<string>("shindenUserId"));
            using var db = Open();
            Assert.Equal(99ul, (await db.Users.SingleAsync()).Shinden);
        }

        [Fact]
        public async Task Disconnect_UnknownPlayer_Returns404AndDoesNotCreatePlayer()
        {
            Assert.Equal(404, (await Controller().DisconnectUserAsync(10, 42)).StatusOf());

            using var db = Open();
            Assert.False(await db.Users.AnyAsync());
        }

        [Theory]
        [InlineData(0ul, 42ul)]
        [InlineData(10ul, 0ul)]
        public async Task Disconnect_InvalidIds_Returns400WithoutQueueing(ulong discordId, ulong shindenId)
        {
            Assert.Equal(400, (await Controller().DisconnectUserAsync(discordId, shindenId)).StatusOf());
            Assert.Empty(_executor.Added);
        }

        [Fact]
        public async Task Disconnect_FullQueue_PreservesLink()
        {
            await SeedAsync(10, 42);
            _executor.Accept = false;

            Assert.Equal(503, (await Controller().DisconnectUserAsync(10, 42)).StatusOf());

            using var db = Open();
            Assert.Equal(42ul, (await db.Users.SingleAsync()).Shinden);
        }

        [Fact]
        public async Task Disconnect_LinkChangedWhileWaitingInQueue_DoesNotDisconnectNewLink()
        {
            await SeedAsync(10, 42);
            _executor.RunImmediately = false;
            var pending = Controller().DisconnectUserAsync(10, 42);
            Assert.Single(_executor.Added);
            Assert.False(pending.IsCompleted);
            using (var db = Open())
            {
                (await db.Users.SingleAsync()).Shinden = 99;
                await db.SaveChangesAsync();
            }
            Assert.True(_executor.Added.TryDequeue(out var task));
            await task.ExecuteAsync(new EmptyServiceProvider());

            Assert.Equal(409, (await pending.WaitAsync(TimeSpan.FromSeconds(5))).StatusOf());
            using var read = Open();
            Assert.Equal(99ul, (await read.Users.SingleAsync()).Shinden);
        }
    }
}
