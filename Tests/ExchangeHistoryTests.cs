using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sanakan.Api.Controllers;
using Sanakan.Api.Models;
using Sanakan.Database;
using Sanakan.Database.Models;
using Sanakan.Database.Models.Analytics;
using Sanakan.Extensions;
using Xunit;

namespace Artifacts
{
    public class ExchangeHistoryTests : IDisposable
    {
        private const ulong Me = 7001, Partner = 7002, Other = 7003, Stranger = 7004;
        private static readonly DateTime Day = new DateTime(2026, 11, 10, 12, 0, 0);

        private readonly SqliteConnection _connection = new SqliteConnection("Data Source=:memory:");

        private sealed class TestDatabase : DatabaseContext
        {
            private readonly SqliteConnection _connection;
            public TestDatabase(SqliteConnection connection) : base(new FakeConfig()) => _connection = connection;
            protected override void OnConfiguring(DbContextOptionsBuilder builder) => builder.UseSqlite(_connection);
        }

        private sealed class TestController : ExchangeHistoryController
        {
            private readonly Func<DatabaseContext> _factory;
            public List<ulong> ShindenLookups { get; } = new List<ulong>();
            public TestController(Func<DatabaseContext> factory) : base(new FakeConfig(), null, null) => _factory = factory;
            protected override DatabaseContext CreateDatabaseContext() => _factory();
            protected override string GetNick(ulong id) => id == Other ? null : $"discord{id}";
            protected override Task<string> GetShindenNickAsync(ulong shindenId)
            {
                lock (ShindenLookups) ShindenLookups.Add(shindenId);
                return Task.FromResult($"shinden{shindenId}");
            }
        }

        public ExchangeHistoryTests()
        {
            _connection.Open();
            SqliteText.RegisterPow(_connection);
            using var db = Open();
            db.Database.EnsureCreated();
            foreach (var (id, shinden) in new[] { (Me, 500ul), (Partner, 600ul), (Other, 0ul), (Stranger, 800ul) })
            {
                var user = new User().Default(id, Day);
                user.Shinden = shinden;
                db.Users.Add(user);
            }
            foreach (var (id, owner) in new[] { (101ul, Partner), (103ul, Partner), (201ul, Me), (202ul, Me), (301ul, Me) })
                db.Cards.Add(new Card { Id = id, GameDeckId = owner, Character = id, Name = $"postać {id}", Rarity = Rarity.A, Dere = Dere.Deredere, Tags = new List<Tag>() });

            Exchange(db, Day, Me, Partner, (101, Me), (102, Me), (201, Partner));
            Exchange(db, Day.AddDays(1), Other, Me, (301, Other));
            Exchange(db, Day.AddDays(2), Partner, Me, (202, Partner), (103, Me));
            Exchange(db, Day.AddDays(3), Partner, Stranger, (999, Stranger));
            db.SaveChanges();
        }

        private static void Exchange(DatabaseContext db, DateTime date, ulong user1, ulong user2, params (ulong Card, ulong From)[] cards)
        {
            var record = new CardExchange
            {
                Date = date, User1Id = user1, User2Id = user2,
                User1Gave = cards.Count(x => x.From == user1), User2Gave = cards.Count(x => x.From == user2),
            };
            foreach (var (card, from) in cards)
                record.Cards.Add(new CardExchangeCard { CardId = card, FromUserId = from, ToUserId = from == user1 ? user2 : user1 });
            db.CardExchanges.Add(record);
        }

        public void Dispose() => _connection.Dispose();

        private TestDatabase Open() => new TestDatabase(_connection);

        private async Task<(UserExchanges History, TestController Controller)> GetAsync(uint offset, uint count, ulong user = Me)
        {
            var controller = ControllerHarness.Attach(new TestController(Open));
            controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("DiscordId", user.ToString()), new Claim("Player", "waifu_player") }, "test"));
            var result = Assert.IsType<OkObjectResult>(await controller.GetExchangesAsync(offset, count));
            return (Assert.IsType<UserExchanges>(result.Value), controller);
        }

        [Fact]
        public async Task History_IsCountedFromTheUsersSide_WithAllPartners()
        {
            var (history, controller) = await GetAsync(0, 2);

            Assert.Equal(Me.ToString(), history.User.DiscordId);
            Assert.Equal(("discord7001", "500", "shinden500"), (history.User.Nick, history.User.ShindenId, history.User.ShindenNick));
            Assert.Equal((3, 3L, 3L), (history.TotalExchanges, history.CardsGiven, history.CardsReceived));

            Assert.Equal(new[] { (Partner.ToString(), 2, 3L, 2L), (Other.ToString(), 1, 0L, 1L) },
                history.Partners.Select(x => (x.Player.DiscordId, x.Exchanges, x.CardsGiven, x.CardsReceived)));
            Assert.Equal(Day.AddDays(2), history.Partners[0].LastExchange);
            var other = history.Partners[1].Player;
            Assert.Equal(((string)null, (string)null, (string)null), (other.Nick, other.ShindenId, other.ShindenNick));
            Assert.Equal(new[] { 500ul, 600ul }, controller.ShindenLookups.OrderBy(x => x));
        }

        [Fact]
        public async Task Page_IsNewestFirst_WithCardsGivenAndReceived()
        {
            var (history, _) = await GetAsync(0, 2);

            Assert.Equal(new[] { Day.AddDays(2), Day.AddDays(1) }, history.Exchanges.Select(x => x.Date));
            var newest = history.Exchanges[0];
            Assert.False(newest.Proposed);
            Assert.Equal(("discord7002", "600", "shinden600"), (newest.Partner.Nick, newest.Partner.ShindenId, newest.Partner.ShindenNick));
            Assert.Equal((1, 1), (newest.GivenCount, newest.ReceivedCount));
            var given = Assert.Single(newest.Given);
            Assert.Equal((103ul, true, "postać 103", (Rarity?)Rarity.A, Partner.ToString()), (given.Id, given.Exists, given.Name, given.Rarity, given.OwnerId));
            Assert.Equal(202ul, Assert.Single(newest.Received).Id);
            Assert.Empty(history.Exchanges[1].Given);
        }

        [Fact]
        public async Task OlderPage_ShowsCardsThatAreGone()
        {
            var (history, _) = await GetAsync(2, 50);

            var oldest = Assert.Single(history.Exchanges);
            Assert.True(oldest.Proposed);
            Assert.Equal(new[] { (101ul, true), (102ul, false) }, oldest.Given.Select(x => (x.Id, x.Exists)));
            Assert.Null(oldest.Given[1].Name);
            Assert.Equal(201ul, Assert.Single(oldest.Received).Id);
            Assert.Equal(3, history.TotalExchanges);
        }

        [Fact]
        public async Task PlayerTheBotDoesNotSee_GetsTheRememberedName()
        {
            using (var db = Open())
            {
                db.PlayerNames.Add(new PlayerName { UserId = Other, Name = "dawny nick", SeenAt = Day });
                db.PlayerNames.Add(new PlayerName { UserId = Partner, Name = "stary", SeenAt = Day });
                db.SaveChanges();
            }

            var (history, _) = await GetAsync(0, 50);

            Assert.Equal("dawny nick", history.Partners.Single(x => x.Player.DiscordId == Other.ToString()).Player.Nick);
            Assert.Equal("discord7002", history.Partners.Single(x => x.Player.DiscordId == Partner.ToString()).Player.Nick);
        }

        [Fact]
        public async Task PlayerWithoutExchanges_GetsAnEmptyHistory()
        {
            using (var db = Open())
            {
                db.Users.Add(new User().Default(7005, Day));
                db.SaveChanges();
            }

            var (history, _) = await GetAsync(0, 50, 7005);

            Assert.Equal((0, 0L, 0L), (history.TotalExchanges, history.CardsGiven, history.CardsReceived));
            Assert.Empty(history.Partners);
            Assert.Empty(history.Exchanges);
        }

        [Fact]
        public async Task MissingClaim_IsRefused()
        {
            var controller = ControllerHarness.Attach(new TestController(Open));
            controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity());

            var result = Assert.IsAssignableFrom<ObjectResult>(await controller.GetExchangesAsync());
            Assert.Equal(403, result.StatusCode);
        }
    }
}
