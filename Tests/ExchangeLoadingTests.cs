using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Discord;
using Moq;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services.PocketWaifu;
using Sanakan.Services.Session.Models;
using Xunit;

namespace Artifacts
{
    // the exchange starts with the players' deck rows only and reads the offered cards from the database when they are added
    public class ExchangeLoadingTests : IDisposable
    {
        private const ulong First = 81;
        private const ulong Second = 82;

        private readonly WaifuCommandHarness _h = new WaifuCommandHarness();

        public void Dispose() => _h.Dispose();

        private Card NewCard(ulong id, Dere dere = Dere.Deredere) => new Card
        {
            Id = id, Character = id, Name = $"postać {id}", Rarity = Rarity.E, Dere = dere, Affection = 10, Health = 100, Attack = 20, Defence = 10,
            MarketValue = 1, IsTradable = true, Tags = new List<Tag>(), CreationDate = _h.Time.Now()
        };

        private async Task SeedAsync()
        {
            using var db = await _h.CreateAsync();
            var first = new User().Default(First, _h.Time.Now());
            var second = new User().Default(Second, _h.Time.Now());
            second.GameDeck.Karma = 200;
            var tag = new Tag { Name = "konie", GameDeck = first.GameDeck };
            first.GameDeck.Tags.Add(tag);
            first.GameDeck.Cards.Add(NewCard(101));
            first.GameDeck.Cards.Last().Tags.Add(tag);
            first.GameDeck.Cards.Add(NewCard(102));
            first.GameDeck.Cards.Last().InCage = true;
            first.GameDeck.Cards.Add(NewCard(103, Dere.Yami));
            first.GameDeck.Cards.Add(NewCard(104));
            second.GameDeck.Cards.Add(NewCard(201));
            db.Users.AddRange(first, second);
            await db.SaveChangesAsync();
        }

        private async Task<(ExchangeSession Session, PlayerInfo P1, PlayerInfo P2)> StartAsync()
        {
            using var db = _h.Open();
            var u1 = SocketTestObjects.User(First);
            var u2 = SocketTestObjects.User(Second);
            var channel = new Mock<IMessageChannel>();
            channel.Setup(x => x.GetMessageAsync(It.IsAny<ulong>(), It.IsAny<CacheMode>(), It.IsAny<RequestOptions>())).ReturnsAsync((IMessage)null);
            var message = new Mock<IMessage>();
            message.SetupGet(x => x.Channel).Returns(channel.Object);
            var p1 = new PlayerInfo { User = u1, Dbuser = await db.GetUserWithDeckAsync(First), Cards = new List<Card>(), Tag = "" };
            var p2 = new PlayerInfo { User = u2, Dbuser = await db.GetUserWithDeckAsync(Second), Cards = new List<Card>(), Tag = "" };
            var session = new ExchangeSession(u1, u2, _h.Config, _h.Time, _h.Tags) { P1 = p1, P2 = p2, Message = message.Object };
            return (session, p1, p2);
        }

        private static async Task<List<string>> AddAsync(ExchangeSession session, PlayerInfo player, PlayerInfo target, params ulong[] ids)
        {
            var reply = new Mock<IUserMessage>();
            await (Task)typeof(ExchangeSession).GetMethod("HandleAddAsync", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(session, new object[] { player, ids.ToList(), reply.Object, target });
            return reply.Invocations.Where(x => x.Method.Name == "AddReactionAsync").Select(x => x.Arguments[0].ToString()).ToList();
        }

        [Fact]
        public async Task Start_LoadsNoCollection()
        {
            await SeedAsync();

            var (_, p1, p2) = await StartAsync();

            Assert.Null(p1.Dbuser.GameDeck.Cards);
            Assert.Null(p2.Dbuser.GameDeck.PvPStats);
            Assert.Equal(200, p2.Dbuser.GameDeck.Karma, 6);
        }

        [Fact]
        public async Task Add_TakesOnlyOwnExchangeableCards_WithTheirTags()
        {
            await SeedAsync();
            var (session, p1, p2) = await StartAsync();

            // caged, yami for a good partner, someone else's, missing
            var reactions = await AddAsync(session, p1, p2, 101, 102, 103, 201, 999);

            Assert.Equal(new ulong[] { 101 }, p1.Cards.Select(x => x.Id));
            Assert.Equal("konie", Assert.Single(p1.Cards[0].Tags).Name);
            Assert.Equal(new[] { "📥", "❌" }, reactions);
        }

        [Fact]
        public async Task Add_SeesChangesMadeAfterTheStart_AndSkipsDuplicates()
        {
            await SeedAsync();
            var (session, p1, p2) = await StartAsync();
            using (var db = _h.Open())
            {
                (await db.Cards.FindAsync(104ul)).InCage = true;
                (await db.Cards.FindAsync(102ul)).InCage = false;
                await db.SaveChangesAsync();
            }

            await AddAsync(session, p1, p2, 102, 104);
            Assert.Equal(new[] { "📥" }, await AddAsync(session, p2, p1, 201));
            Assert.Empty(await AddAsync(session, p1, p2, 102));

            Assert.Equal(new ulong[] { 102 }, p1.Cards.Select(x => x.Id));
            Assert.Equal(new ulong[] { 201 }, p2.Cards.Select(x => x.Id));
        }
    }
}
