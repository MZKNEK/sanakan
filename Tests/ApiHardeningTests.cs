using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sanakan.Api;
using Sanakan.Api.Controllers;
using Sanakan.Api.Models;
using Sanakan.Config.Model;
using Sanakan.Database;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services.Executor;
using Sanakan.Services.PocketWaifu;
using Sanakan.Services.Session;
using Sanakan.Services.Session.Models;
using Sanakan.Services.Time;
using Xunit;

namespace Artifacts
{
    public class ApiTokenRecheckTests : IDisposable
    {
        private const string Shops = "/api/waifu/shops";

        private readonly ApiServer _api = new ApiServer();

        public void Dispose() => _api.Dispose();

        private void AsPlayer(ulong id)
        {
            var token = UserTokenBuilder.BuildUserToken(_api.Config, new User { Id = id }, new SystemTime());
            _api.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        }

        [Fact]
        public async Task PlayerToken_StopsWorkingOnceUserIsBlacklisted()
        {
            AsPlayer(42);
            Assert.Equal(HttpStatusCode.OK, (await _api.Client.GetAsync(Shops)).StatusCode);

            _api.Blacklist.Users[42] = true;

            Assert.Equal(HttpStatusCode.Unauthorized, (await _api.Client.GetAsync(Shops)).StatusCode);
        }

        [Fact]
        public async Task SiteToken_StopsWorkingWhenAppIsRemoved()
        {
            var client = await _api.AsSiteAsync();
            Assert.Equal(HttpStatusCode.OK, (await ApiServer.PostSiteEndpointAsync(client)).StatusCode);

            _api.Config.Model.UserKeyApps = new List<ApiApp>();

            Assert.Equal(HttpStatusCode.Unauthorized, (await ApiServer.PostSiteEndpointAsync(client)).StatusCode);
        }

        [Fact]
        public async Task SiteToken_StopsWorkingWhenSitePermissionIsTaken()
        {
            var client = await _api.AsSiteAsync();
            Assert.Equal(HttpStatusCode.OK, (await ApiServer.PostSiteEndpointAsync(client)).StatusCode);

            _api.Config.Model.UserKeyApps.Single(x => x.Key == ApiServer.SiteKey).Permissions = ApiAppPermission.Info;

            Assert.Equal(HttpStatusCode.Unauthorized, (await ApiServer.PostSiteEndpointAsync(client)).StatusCode);
        }

        [Fact]
        public void SiteAppCheck_NeedsMatchingAppWithSite()
        {
            var config = new FakeConfig();
            config.Model.UserKeyApps.Add(new ApiApp { Key = "a", Bearer = "site", Permissions = ApiAppPermission.Site });
            config.Model.UserKeyApps.Add(new ApiApp { Key = "b", Bearer = "info", Permissions = ApiAppPermission.Info });

            Assert.True(TokenChecks.IsSiteAppActive(config, "site"));
            Assert.False(TokenChecks.IsSiteAppActive(config, "info"));
            Assert.False(TokenChecks.IsSiteAppActive(config, "gone"));
            Assert.False(TokenChecks.IsSiteAppActive(config, null));
        }
    }

    public class ApiSqliteHardeningTests : IDisposable
    {
        private const ulong DiscordId = 95001;
        private const ulong ShindenId = 96001;
        private const ulong BlacklistedId = 95002;
        private const ulong BlacklistedShindenId = 96002;

        private readonly SqliteConnection _connection = new SqliteConnection("Data Source=:memory:");
        private readonly FakeExecutor _executor = new FakeExecutor { RunImmediately = true };
        private readonly FixedTime _time = new FixedTime();

        private sealed class TestDatabase : DatabaseContext
        {
            private readonly SqliteConnection _connection;
            public TestDatabase(SqliteConnection connection) : base(new FakeConfig()) => _connection = connection;
            protected override void OnConfiguring(DbContextOptionsBuilder builder) => builder.UseSqlite(_connection);
        }

        private sealed class TestUserController : UserController
        {
            private readonly Func<DatabaseContext> _factory;
            public TestUserController(IExecutor executor, ISystemTime time, Func<DatabaseContext> factory)
                : base(null, null, new ListLogger(), executor, new FakeConfig(), time, null, null, null) => _factory = factory;
            protected override DatabaseContext CreateDatabaseContext() => _factory();
        }

        private sealed class TestPlayerController : PlayerController
        {
            private readonly Func<DatabaseContext> _factory;
            public TestPlayerController(IExecutor executor, ISystemTime time, SessionManager sessions, Func<DatabaseContext> factory)
                : base(new FakeConfig(), time, executor, null, null, sessions) => _factory = factory;
            protected override DatabaseContext CreateDatabaseContext() => _factory();
        }

        public ApiSqliteHardeningTests()
        {
            _connection.Open();
            Sanakan.Database.SqliteText.RegisterPow(_connection);
            using var db = Open();
            db.Database.EnsureCreated();
            foreach (var (discord, shinden, blacklisted) in new[] { (DiscordId, ShindenId, false), (BlacklistedId, BlacklistedShindenId, true) })
            {
                var user = new User().Default(discord, _time.Now());
                user.Shinden = shinden;
                user.IsBlacklisted = blacklisted;
                user.GameDeck.Cards.Add(new Card { Character = 77, Name = "Waifu", Rarity = Rarity.E, Dere = Dere.Deredere, Tags = new List<Tag>() });
                db.Users.Add(user);
            }
            db.SaveChanges();
        }

        public void Dispose() => _connection.Dispose();

        private TestDatabase Open() => new TestDatabase(_connection);

        private TestUserController SiteUserController()
        {
            var controller = ControllerHarness.Attach(new TestUserController(_executor, _time, Open));
            controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Webpage, "site") }, "test"));
            return controller;
        }

        [Fact]
        public async Task UserByShinden_GivesTokenOnlyToUsersNotOnBlacklist()
        {
            var normal = (await SiteUserController().GetUserByShindenIdAsync(ShindenId)).Value;
            var blacklisted = (await SiteUserController().GetUserByShindenIdAsync(BlacklistedShindenId)).Value;

            Assert.NotNull(normal.Token);
            Assert.Null(blacklisted.Token);
            Assert.Null(blacklisted.Expire);
            Assert.Equal(BlacklistedId, blacklisted.User.Id);
        }

        [Fact]
        public async Task SimpleUserByShinden_GivesTokenOnlyToUsersNotOnBlacklist()
        {
            Assert.NotNull((await SiteUserController().GetUserByShindenIdSimpleAsync(ShindenId)).Value.Token);
            Assert.Null((await SiteUserController().GetUserByShindenIdSimpleAsync(BlacklistedShindenId)).Value.Token);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task SetWaifu_IsRefusedDuringExchange_ForBothSides(bool owner)
        {
            using var client = new Discord.WebSocket.DiscordSocketClient();
            using var sessions = new SessionManager(client, new FakeExecutor(), new ListLogger());
            sessions.Initialize(new EmptyServiceProvider());
            var first = SocketTestObjects.User(owner ? DiscordId : 99);
            var second = SocketTestObjects.User(owner ? 99 : DiscordId);
            Assert.True(await sessions.TryAddSession(new ExchangeSession(first, second, new FakeConfig(), new FixedTime(), null)));

            var controller = ControllerHarness.Attach(new TestPlayerController(_executor, _time, sessions, Open));
            controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("DiscordId", DiscordId.ToString()), new Claim("Player", "waifu_player") }, "test"));
            var wid = Open().Cards.AsQueryable().Single(x => x.GameDeckId == DiscordId).Id;

            var res = await controller.SetWaifuAsync(wid);

            Assert.Equal(409, res.StatusOf());
            Assert.Equal(0ul, Open().GameDecks.AsQueryable().Single(x => x.Id == DiscordId).Waifu);
        }

        [Fact]
        public async Task SetWaifu_WorksWithoutExchange()
        {
            using var client = new Discord.WebSocket.DiscordSocketClient();
            using var sessions = new SessionManager(client, new FakeExecutor(), new ListLogger());
            sessions.Initialize(new EmptyServiceProvider());

            var controller = ControllerHarness.Attach(new TestPlayerController(_executor, _time, sessions, Open));
            controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("DiscordId", DiscordId.ToString()), new Claim("Player", "waifu_player") }, "test"));
            var wid = Open().Cards.AsQueryable().Single(x => x.GameDeckId == DiscordId).Id;

            Assert.Equal(200, (await controller.SetWaifuAsync(wid)).StatusOf());
            Assert.Equal(77ul, Open().GameDecks.AsQueryable().Single(x => x.Id == DiscordId).Waifu);
        }

        // only the chosen card and the cards of the previous favorite character are loaded
        [Fact]
        public async Task SetWaifu_LowersTheCardsOfThePreviousFavorite()
        {
            using (var db = Open())
            {
                db.GameDecks.AsQueryable().Single(x => x.Id == DiscordId).Waifu = 55;
                db.Cards.Add(new Card { GameDeckId = DiscordId, Character = 55, Name = "Stara", Affection = 20, Rarity = Rarity.E, Dere = Dere.Deredere, Tags = new List<Tag>() });
                db.Cards.Add(new Card { GameDeckId = DiscordId, Character = 66, Name = "Inna", Affection = 20, Rarity = Rarity.E, Dere = Dere.Deredere, Tags = new List<Tag>() });
                db.SaveChanges();
            }
            using var client = new Discord.WebSocket.DiscordSocketClient();
            using var sessions = new SessionManager(client, new FakeExecutor(), new ListLogger());
            sessions.Initialize(new EmptyServiceProvider());
            var controller = ControllerHarness.Attach(new TestPlayerController(_executor, _time, sessions, Open));
            controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("DiscordId", DiscordId.ToString()), new Claim("Player", "waifu_player") }, "test"));
            var wid = Open().Cards.AsQueryable().Single(x => x.GameDeckId == DiscordId && x.Character == 77).Id;

            Assert.Equal(200, (await controller.SetWaifuAsync(wid)).StatusOf());

            var cards = Open().Cards.AsQueryable().Where(x => x.GameDeckId == DiscordId).ToList();
            Assert.Equal(15, cards.Single(x => x.Character == 55).Affection, 6);
            Assert.Equal(20, cards.Single(x => x.Character == 66).Affection, 6);
        }
    }

    public class BoosterPackValidationTests
    {
        private static WaifuController Controller() => ControllerHarness.Attach(new WaifuController(null, null, new FakeExecutor(), null, new FakeConfig(),
            new FixedTime(), new Sanakan.Services.Shinden(null, null, null, new ListLogger(), new FixedTime()), null, null, null));

        private static Waifu NewWaifu() => new Waifu(null, null, null, new ListLogger(), null, null, null, new FixedTime(), null, null, new FakeConfig());

        public static IEnumerable<object[]> InvalidPacks()
        {
            yield return new object[] { new List<CardBoosterPack> { new CardBoosterPack { Count = 1, RarityExcluded = Waifu.DrawableRarities.ToList() } } };
            yield return new object[] { new List<CardBoosterPack> { new CardBoosterPack { Count = 1, Rarity = (Rarity)42 } } };
            yield return new object[] { new List<CardBoosterPack> { new CardBoosterPack { Count = 1, RarityExcluded = new List<Rarity> { (Rarity)42 } } } };
            yield return new object[] { Enumerable.Range(0, 101).Select(_ => new CardBoosterPack { Count = 1 }).ToList() };
        }

        [Theory]
        [MemberData(nameof(InvalidPacks))]
        public async Task InvalidPacks_AreRejectedBeforeAnythingIsSaved(List<CardBoosterPack> packs)
        {
            var controller = Controller();

            Assert.Equal(500, (await controller.GiveShindenUserAPacksAsync(5, packs)).StatusOf());
            Assert.Equal(500, (await controller.GiveShindenUserAPacksAndOpenAsync(5, packs)).StatusOf());
            Assert.Equal(500, StatusOf(await controller.GiveUserAPacksAsync(5, packs)));
        }

        private static int? StatusOf(Microsoft.AspNetCore.Mvc.IActionResult result) => (result as Microsoft.AspNetCore.Mvc.ObjectResult)?.StatusCode;

        [Fact]
        public void RandomizeRarity_WithEveryRarityExcluded_StillDraws()
        {
            var waifu = NewWaifu();

            for (int i = 0; i < 200; i++)
                Assert.Contains(waifu.RandomizeRarity(Waifu.DrawableRarities.ToList()), Waifu.DrawableRarities);
        }

        [Fact]
        public void RandomizeRarity_WithOneRarityLeft_AlwaysDrawsIt()
        {
            var waifu = NewWaifu();
            var excluded = Waifu.DrawableRarities.Where(x => x != Rarity.E).ToList();

            for (int i = 0; i < 200; i++)
                Assert.Equal(Rarity.E, waifu.RandomizeRarity(excluded));
        }

        [Fact]
        public void PoolWithoutSeason_SerializesAsBefore()
        {
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(new BoosterPackPool { Type = CardsPoolType.Title, TitleId = 5 });

            Assert.DoesNotContain("Season", json);
            Assert.Contains("\"Season\":\"2026-autumn\"", Newtonsoft.Json.JsonConvert.SerializeObject(new BoosterPackPool { Season = "2026-autumn" }));
        }
    }

    public class SessionByIdTests
    {
        [Fact]
        public async Task SessionExist_ById_MatchesEveryParticipantAndOnlyThatType()
        {
            using var client = new Discord.WebSocket.DiscordSocketClient();
            using var sessions = new SessionManager(client, new FakeExecutor(), new ListLogger());
            sessions.Initialize(new EmptyServiceProvider());
            var first = SocketTestObjects.User(81);
            var second = SocketTestObjects.User(82);
            await sessions.TryAddSession(new Session(SocketTestObjects.User(83)));
            Assert.True(await sessions.TryAddSession(new ExchangeSession(first, second, new FakeConfig(), new FixedTime(), null)));

            Assert.True(sessions.SessionExist(81, typeof(ExchangeSession)));
            Assert.True(sessions.SessionExist(82, typeof(ExchangeSession)));
            Assert.False(sessions.SessionExist(83, typeof(ExchangeSession)));
            Assert.False(sessions.SessionExist(84, typeof(ExchangeSession)));
        }
    }
}
