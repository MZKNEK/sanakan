using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Moq;
using Sanakan.Database;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services.Executor;
using Sanakan.Services.PocketWaifu;
using Sanakan.Services.Session;
using Sanakan.Services.Session.Models;
using Xunit;
using WaifuModule = Sanakan.Modules.PocketWaifu;

namespace Artifacts
{
    public class ExchangeGuardTests
    {
        public static IEnumerable<object[]> MutatingCommands()
        {
            var names = new[] { "deck", "destroy", "release", "reset", "upgrade", "update", "sacrifice", "chest",
                "exp", "item", "market", "black-market", "unleash", "lottery", "premium", "waifu", "transform", "expedition", "return" };
            foreach (var name in names)
                foreach (var participant in new[] { false, true })
                    yield return new object[] { name, participant };
        }

        private static Task Run(WaifuModule module, string name) => name switch
        {
            "deck" => module.ChangeDeckCardStatusAsync(101),
            "destroy" => module.DestroyCardAsync(101),
            "release" => module.ReleaseCardAsync(101),
            "reset" => module.ResetCardAsync(101),
            "upgrade" => module.UpgradeCardAsync(101),
            "update" => module.UpdateCardAsync(101),
            "sacrifice" => module.SacraficeCardMultiAsync(101, 102),
            "chest" => module.CreateChestAsync(101),
            "exp" => module.TransferExpFromChestAsync(101, 1),
            "item" => module.UseItemOnCardAsync(1, 101),
            "market" => module.GoToMarketAsync(101),
            "black-market" => module.GoToBlackMarketAsync(101),
            "unleash" => module.UnleashCardAsync(101),
            "lottery" => module.GoToLotteryAsync(),
            "premium" => module.SetCardAsPremiumWaifuAsync(101),
            "waifu" => module.SetProfileWaifuAsync(101),
            "transform" => module.ChangeCardAsync(101),
            "expedition" => module.SendCardToExpeditionAsync(CardExpedition.NormalItemWithExp, 101),
            "return" => module.EndCardExpeditionAsync(101),
            _ => throw new ArgumentOutOfRangeException(nameof(name))
        };

        private static WaifuModule Module(SessionManager manager, DiscordSocketClient client, SocketGuildUser user, ISocketMessageChannel channel)
        {
            var module = new WaifuModule(null, null, new ListLogger(), null, manager,
                new FakeConfig(), null, null, new FixedTime(), null, null, null, null);
            var context = new SocketCommandContext(client, SocketTestObjects.Message(user, ".test", channel));
            typeof(ModuleBase<SocketCommandContext>).GetProperty("Context").SetValue(module, context);
            return module;
        }

        [Theory]
        [MemberData(nameof(MutatingCommands))]
        public async Task CardMutation_IsBlockedForBothExchangeParticipants(string command, bool participant)
        {
            using var client = new DiscordSocketClient();
            using var manager = new SessionManager(client, Mock.Of<IExecutor>(), new ListLogger());
            manager.Initialize(new EmptyServiceProvider());
            var first = SocketTestObjects.User(71);
            var second = SocketTestObjects.User(72);
            var exchange = new ExchangeSession(first, second, new FakeConfig(), new FixedTime(), null);
            Assert.True(await manager.TryAddSession(exchange));
            var channel = new Mock<ISocketMessageChannel>();
            channel.SetReturnsDefault(Task.FromResult(Mock.Of<IUserMessage>()));
            var module = Module(manager, client, participant ? second : first, channel.Object);

            await Run(module, command);

            var reply = Assert.Single(channel.Invocations.Where(x => x.Method.Name == "SendMessageAsync"));
            Assert.Contains("w trakcie wymiany", ((Embed)reply.Arguments[2]).Description);
        }

        [Fact]
        public async Task OtherSessionDoesNotBlockCards_AndExchangeBlockEndsAfterRemoval()
        {
            using var client = new DiscordSocketClient();
            using var manager = new SessionManager(client, Mock.Of<IExecutor>(), new ListLogger());
            manager.Initialize(new EmptyServiceProvider());
            var first = SocketTestObjects.User(73);
            var second = SocketTestObjects.User(74);
            var channel = new Mock<ISocketMessageChannel>();
            channel.SetReturnsDefault(Task.FromResult(Mock.Of<IUserMessage>()));
            var module = Module(manager, client, first, channel.Object);
            var guard = typeof(WaifuModule).GetMethod("BlockDuringExchangeAsync", BindingFlags.Instance | BindingFlags.NonPublic);
            await manager.TryAddSession(new Session(first));
            Assert.False(await (Task<bool>)guard.Invoke(module, null));

            var exchange = new ExchangeSession(first, second, new FakeConfig(), new FixedTime(), null);
            await manager.TryAddSession(exchange);
            Assert.True(await (Task<bool>)guard.Invoke(module, null));
            await manager.KillSessionIfExistAsync(exchange);
            Assert.False(await (Task<bool>)guard.Invoke(module, null));
        }
    }

    public class ExchangeFinalizationTests
    {
        private class AuditDatabase : DatabaseContext
        {
            public AuditDatabase() : base(new FakeConfig()) { }
            protected override void OnConfiguring(DbContextOptionsBuilder builder)
                => builder.UseSqlite("Data Source=:memory:");
        }

        private static Card NewCard(ulong id, ulong owner) => new Card
        {
            Id = id, GameDeckId = owner, IsTradable = true, Affection = 10,
            Rarity = Rarity.E, Dere = Dere.Deredere, Tags = new List<Tag>(),
            MarketValue = 1, Character = id, Health = 100, Attack = 20, Defence = 10
        };

        private static async Task<(AuditDatabase Db, ExchangeSession Session, User First, User Second)> CreateExchange()
        {
            var db = new AuditDatabase();
            await db.Database.OpenConnectionAsync();
            await db.Database.EnsureCreatedAsync();
            var time = new FixedTime();
            var first = new User().Default(81, time.Now());
            var second = new User().Default(82, time.Now());
            first.GameDeck.Cards.Add(NewCard(101, first.Id));
            second.GameDeck.Cards.Add(NewCard(102, second.Id));
            db.Users.AddRange(first, second);
            await db.SaveChangesAsync();
            var p1 = SocketTestObjects.User(first.Id);
            var p2 = SocketTestObjects.User(second.Id);
            var session = new ExchangeSession(p1, p2, new FakeConfig(), time, null)
            {
                P1 = new PlayerInfo { User = p1, Cards = first.GameDeck.Cards.ToList() },
                P2 = new PlayerInfo { User = p2, Cards = second.GameDeck.Cards.ToList() }
            };
            return (db, session, first, second);
        }

        private static Task<string> Finalize(ExchangeSession session, DatabaseContext db)
            => (Task<string>)typeof(ExchangeSession).GetMethod("FinalizeExchangeAsync", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(session, new object[] { db });

        [Theory]
        [InlineData("active")]
        [InlineData("missing")]
        [InlineData("cage")]
        [InlineData("untradable")]
        [InlineData("karma")]
        public async Task InvalidatedOffer_AbortsBothTransfers(string change)
        {
            var (db, session, first, second) = await CreateExchange();
            using (db)
            {
                var card = session.P1.Cards.Single();
                switch (change)
                {
                    case "active": card.Active = true; break;
                    case "missing": db.Cards.Remove(card); break;
                    case "cage": card.InCage = true; break;
                    case "untradable": card.IsTradable = false; break;
                    case "karma": card.Dere = Dere.Yami; second.GameDeck.Karma = 200; break;
                }
                await db.SaveChangesAsync();
                db.ChangeTracker.Clear();
                var before = await db.Cards.Select(x => new { x.Id, x.GameDeckId }).ToListAsync();

                Assert.Contains("anulowana", await Finalize(session, db));

                db.ChangeTracker.Clear();
                var after = await db.Cards.Select(x => new { x.Id, x.GameDeckId }).ToListAsync();
                Assert.Equal(before.OrderBy(x => x.Id), after.OrderBy(x => x.Id));
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task CapacityChangedDuringSession_AbortsBothTransfers(bool secondRecipient)
        {
            var (db, session, first, second) = await CreateExchange();
            using (db)
            {
                var recipient = secondRecipient ? second : first;
                var donor = secondRecipient ? first : second;
                recipient.GameDeck.MaxNumberOfCards = 2;
                var extra = NewCard(103, donor.Id);
                donor.GameDeck.Cards.Add(extra);
                db.Cards.Add(extra);
                (secondRecipient ? session.P1 : session.P2).Cards.Add(extra);
                var acquired = NewCard(104, recipient.Id);
                recipient.GameDeck.Cards.Add(acquired);
                db.Cards.Add(acquired);
                await db.SaveChangesAsync();
                db.ChangeTracker.Clear();

                Assert.Contains("miejsca", await Finalize(session, db));

                db.ChangeTracker.Clear();
                Assert.Equal(first.Id, (await db.Cards.FindAsync(101UL)).GameDeckId);
                Assert.Equal(second.Id, (await db.Cards.FindAsync(102UL)).GameDeckId);
                Assert.Equal(donor.Id, (await db.Cards.FindAsync(103UL)).GameDeckId);
            }
        }

        [Fact]
        public async Task FullDecks_CanSwapEqualNumbersOfCards()
        {
            var (db, session, first, second) = await CreateExchange();
            using (db)
            {
                first.GameDeck.MaxNumberOfCards = 1;
                second.GameDeck.MaxNumberOfCards = 1;
                await db.SaveChangesAsync();
                db.ChangeTracker.Clear();

                Assert.Null(await Finalize(session, db));

                db.ChangeTracker.Clear();
                Assert.Equal(second.Id, (await db.Cards.FindAsync(101UL)).GameDeckId);
                Assert.Equal(first.Id, (await db.Cards.FindAsync(102UL)).GameDeckId);
            }
        }

        [Fact]
        public async Task Gift_CanFillRecipientsLastFreeSlot()
        {
            var (db, session, first, second) = await CreateExchange();
            using (db)
            {
                first.GameDeck.MaxNumberOfCards = 2;
                session.P1.Cards.Clear();
                await db.SaveChangesAsync();
                db.ChangeTracker.Clear();

                Assert.Null(await Finalize(session, db));

                Assert.Equal(2, await db.Cards.CountAsync(x => x.GameDeckId == first.Id));
                Assert.Equal(0, await db.Cards.CountAsync(x => x.GameDeckId == second.Id));
            }
        }
    }
}
