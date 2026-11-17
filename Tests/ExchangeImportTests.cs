using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Sanakan.Database;
using Sanakan.Database.Models;
using Sanakan.Database.Models.Analytics;
using Sanakan.Extensions;
using Sanakan.Services;
using Sanakan.Services.PocketWaifu;
using Sanakan.Services.Session.Models;
using Xunit;

namespace Artifacts
{
    public class ExchangeImportTests : IDisposable
    {
        private const ulong Bot = 1000, First = 81, Second = 82;
        private readonly SqliteConnection _connection = new SqliteConnection("Data Source=:memory:");

        private sealed class TestDatabase : DatabaseContext
        {
            private readonly SqliteConnection _connection;
            public TestDatabase(SqliteConnection connection) : base(new FakeConfig()) => _connection = connection;
            protected override void OnConfiguring(DbContextOptionsBuilder builder) => builder.UseSqlite(_connection);
        }

        public ExchangeImportTests()
        {
            _connection.Open();
            using var db = Open();
            db.Database.EnsureCreated();
        }

        public void Dispose() => _connection.Dispose();

        private TestDatabase Open() => new TestDatabase(_connection);

        private static Card NewCard(ulong id) => new Card { Id = id, Character = id, Name = $"Postać {id}", Rarity = Rarity.A, Dere = Dere.Deredere, Tags = new List<Tag>() };

        // the message the current code leaves after a finished exchange
        private static Embed NewFormat(ExchangeSession.ExchangeOutcome outcome, IEnumerable<ulong> cards1, IEnumerable<ulong> cards2)
        {
            var session = new ExchangeSession(SocketTestObjects.User(First), SocketTestObjects.User(Second), new FakeConfig(), new FixedTime(), null)
            {
                P1 = new PlayerInfo { User = SocketTestObjects.User(First), Cards = cards1.Select(NewCard).ToList() },
                P2 = new PlayerInfo { User = SocketTestObjects.User(Second), Cards = cards2.Select(NewCard).ToList() },
            };
            var type = typeof(ExchangeSession);
            var state = type.GetField("State", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            state.SetValue(session, Enum.ToObject(state.FieldType, 3));
            type.GetField("Outcome", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(session, outcome);
            return session.BuildEmbed();
        }

        // the format before 2026-11-08, card lines as GetString wrote them until 2023 and after
        private static string OldFormat(string offer1, string offer2, string tips = "Wymiana zakończona!")
            => $"🔄 **Wymiana:**\n\n<@!{First}> oferuje:\n{offer1}\n\n<@!{Second}> oferuje:\n{offer2}\n\n{tips}";

        private static string OldLine2019(ulong id) => $"**[{id}]**  Postać {id} **A** ❤300 🔥50 🛡30 ";
        private static string OldLine2023(ulong id) => $"**[[{id}](https://waifu.sanakan.pl/#/card/{id})]** [Postać {id}](https://shinden.pl/character/{id}) **SSS** ❤300 🔥50 🛡30";

        [Fact]
        public void NewFormat_GivesBothSidesAndTheirCards()
        {
            var embed = NewFormat(ExchangeSession.ExchangeOutcome.Done, new ulong[] { 101, 102 }, new ulong[] { 201 });

            var parsed = ExchangeImport.Parse(embed.Title, embed.Description);

            Assert.Equal((First, 2, Second, 1), (parsed.User1.UserId, parsed.User1.Gave, parsed.User2.UserId, parsed.User2.Gave));
            Assert.Equal(new ulong[] { 101, 102 }, parsed.User1.Cards);
            Assert.Equal(new ulong[] { 201 }, parsed.User2.Cards);
            Assert.False(parsed.Truncated);
        }

        [Fact]
        public void NewFormat_GiftAndBigOffer()
        {
            var embed = NewFormat(ExchangeSession.ExchangeOutcome.Done, Enumerable.Range(1, 15).Select(x => (ulong)x), Array.Empty<ulong>());

            var parsed = ExchangeImport.Parse(embed.Title, embed.Description);

            Assert.Equal((15, 0), (parsed.User1.Gave, parsed.User2.Gave));
            Assert.Empty(parsed.User1.Cards);
            Assert.Empty(parsed.User2.Cards);
        }

        [Theory]
        [InlineData(ExchangeSession.ExchangeOutcome.Declined)]
        [InlineData(ExchangeSession.ExchangeOutcome.Cancelled)]
        [InlineData(ExchangeSession.ExchangeOutcome.Expired)]
        [InlineData(ExchangeSession.ExchangeOutcome.None)]
        public void NewFormat_UnfinishedExchangesAreSkipped(ExchangeSession.ExchangeOutcome outcome)
        {
            var embed = NewFormat(outcome, new ulong[] { 101 }, new ulong[] { 201 });
            Assert.Null(ExchangeImport.Parse(embed.Title, embed.Description));
        }

        [Fact]
        public void OldFormat_ReadsBothCardStyles()
        {
            var parsed = ExchangeImport.Parse(null, OldFormat($"{OldLine2019(11)}\n{OldLine2023(12)}", OldLine2023(21)));

            Assert.Equal((First, Second), (parsed.User1.UserId, parsed.User2.UserId));
            Assert.Equal(new ulong[] { 11, 12 }, parsed.User1.Cards);
            Assert.Equal(new ulong[] { 21 }, parsed.User2.Cards);
        }

        [Fact]
        public void OldFormat_BigOfferAndEmptyOffer()
        {
            var parsed = ExchangeImport.Parse(null, OldFormat("\n**[15]** kart", ""));

            Assert.Equal((15, 0), (parsed.User1.Gave, parsed.User2.Gave));
            Assert.Empty(parsed.User1.Cards);
        }

        [Theory]
        [InlineData("Wymiana anulowana: jeden z graczy znajduje się na czarnej liście.")]
        [InlineData("<@!82> odrzucił propozycję wymiany!")]
        [InlineData("<@!82> daj ✅ aby zaakceptować, lub ❌ aby odrzucić.")]
        public void OldFormat_UnfinishedExchangesAreSkipped(string tips)
            => Assert.Null(ExchangeImport.Parse(null, OldFormat(OldLine2019(11), OldLine2019(21), tips)));

        [Fact]
        public void CutDescription_IsMarked()
        {
            var description = $"<@{First}> oferuje:\n{OldLine2023(11)}\n\n<@{Second}> oferuje:\n{OldLine2023(21)}\n";
            description = description.PadRight(2997, 'x') + "...";

            var parsed = ExchangeImport.Parse(ExchangeImport.FinishedTitle, description);

            Assert.True(parsed.Truncated);
            Assert.Equal(new ulong[] { 21 }, parsed.User2.Cards);
        }

        private static IMessage Message(ulong id, ulong author, DateTime edited, string title, string description, DateTime? created = null)
        {
            var message = new Mock<IUserMessage>();
            message.Setup(x => x.Id).Returns(id);
            message.Setup(x => x.Author).Returns(Mock.Of<IUser>(u => u.Id == author));
            message.Setup(x => x.Timestamp).Returns(new DateTimeOffset(created ?? edited.AddMinutes(-1)));
            message.Setup(x => x.EditedTimestamp).Returns(new DateTimeOffset(edited));
            var embed = new EmbedBuilder { Title = title, Description = description }.Build();
            message.Setup(x => x.Embeds).Returns(new List<IEmbed> { embed });
            return message.Object;
        }

        private static ReactionMetadata Mine()
        {
            object metadata = new ReactionMetadata();
            typeof(ReactionMetadata).GetProperty(nameof(ReactionMetadata.IsMe)).SetValue(metadata, true);
            return (ReactionMetadata)metadata;
        }

        // a player's message, with the bot's reactions to it
        private static IMessage Said(ulong id, ulong author, DateTime time, string content, string name = null, ulong[] mentions = null, params string[] reactions)
        {
            var message = new Mock<IUserMessage>();
            message.Setup(x => x.Id).Returns(id);
            message.Setup(x => x.Author).Returns(Mock.Of<IUser>(u => u.Id == author && u.Username == (name ?? $"user{author}") && u.GlobalName == null));
            message.Setup(x => x.Timestamp).Returns(new DateTimeOffset(time));
            message.Setup(x => x.Content).Returns(content);
            message.Setup(x => x.Embeds).Returns(new List<IEmbed>());
            message.Setup(x => x.MentionedUserIds).Returns(mentions ?? Array.Empty<ulong>());
            message.Setup(x => x.Reactions).Returns(reactions.ToDictionary(x => (IEmote)new Emoji(x), _ => Mine()));
            return message.Object;
        }

        private static Func<ulong?, Task<IReadOnlyCollection<IMessage>>> Channel(params IMessage[] messages)
            => before => Task.FromResult<IReadOnlyCollection<IMessage>>(messages.Where(x => before == null || x.Id < before)
                .OrderByDescending(x => x.Id).Take(3).ToList());

        [Fact]
        public async Task Import_ReadsTheWholeChannel_AndCanBeRepeated()
        {
            var day = new DateTime(2024, 5, 1, 12, 0, 0);
            var newer = NewFormat(ExchangeSession.ExchangeOutcome.Done, new ulong[] { 101 }, new ulong[] { 201, 202 });
            var channel = Channel(
                Message(10, Bot, day, null, OldFormat(OldLine2019(11), "")),
                Message(11, Second, day, null, OldFormat(OldLine2019(12), "")),
                Message(12, Bot, day.AddDays(1), null, OldFormat(OldLine2019(13), OldLine2019(23), "Wymiana anulowana: brak miejsca.")),
                Message(13, Bot, day.AddDays(2), newer.Title, newer.Description),
                Message(14, Bot, day.AddDays(3), null, OldFormat("\n**[20]** kart", OldLine2023(24))));

            var result = await ExchangeImport.ImportAsync(Open, channel, Bot);

            // the 20 cards of the last one cannot be told, so it is left out
            Assert.Equal((5, 2, 0, 1), (result.Messages, result.Imported, result.AlreadyKnown, result.SkippedMissingCards));
            Assert.Equal(10ul, result.OldestMessageId);
            using (var db = Open())
            {
                var all = db.CardExchanges.Include(x => x.Cards).ToList().OrderBy(x => x.MessageId).ToList();
                Assert.All(all, x => Assert.True(x.Imported));
                Assert.Equal(new ulong?[] { 10, 13 }, all.Select(x => x.MessageId));
                Assert.Equal((1, 0, day), (all[0].User1Gave, all[0].User2Gave, all[0].Date));
                Assert.Equal(new[] { (101ul, First, Second), (201ul, Second, First), (202ul, Second, First) },
                    all[1].Cards.OrderBy(x => x.CardId).Select(x => (x.CardId, x.FromUserId, x.ToUserId)));
            }

            var again = await ExchangeImport.ImportAsync(Open, channel, Bot);
            Assert.Equal((0, 2), (again.Imported, again.AlreadyKnown));
            using var check = Open();
            Assert.Equal(2, check.CardExchanges.Count());
        }

        [Fact]
        public async Task Import_SkipsMessagesFromWhenExchangesWereRecordedWithoutThem()
        {
            var day = new DateTime(2026, 11, 17, 12, 0, 0);
            using (var db = Open())
            {
                db.CardExchanges.Add(new CardExchange { Date = day, User1Id = First, User2Id = Second, User1Gave = 1 });
                db.SaveChanges();
            }

            var result = await ExchangeImport.ImportAsync(Open, Channel(
                Message(20, Bot, day.AddHours(-1), null, OldFormat(OldLine2019(11), "")),
                Message(21, Bot, day.AddMinutes(1), ExchangeImport.FinishedTitle, $"<@{First}> oferuje:\n{OldLine2023(11)}\n\n<@{Second}> oferuje:\n*nic*")), Bot);

            Assert.Equal(1, result.Imported);
            using var check = Open();
            Assert.Equal(new ulong?[] { null, 20 }, check.CardExchanges.Select(x => x.MessageId).ToList().OrderBy(x => x));
        }

        [Fact]
        public async Task Import_CanStartBeforeAGivenMessage()
        {
            var day = new DateTime(2024, 5, 1, 12, 0, 0);
            var result = await ExchangeImport.ImportAsync(Open, Channel(
                Message(30, Bot, day, null, OldFormat(OldLine2019(11), "")),
                Message(31, Bot, day, null, OldFormat(OldLine2019(12), ""))), Bot, startBefore: 31);

            Assert.Equal((1, 1), (result.Messages, result.Imported));
        }

        [Fact]
        public async Task MissingCards_AreTakenFromThePlayersCommands_AndNamesAreRemembered()
        {
            var day = new DateTime(2024, 5, 1, 12, 0, 0);
            var result = await ExchangeImport.ImportAsync(Open, Channel(
                Said(99, First, day.AddSeconds(-10), $".wymiana <@{Second}>", mentions: new[] { Second }),
                Message(100, Bot, day.AddMinutes(5), null, OldFormat("\n**[13]** kart", OldLine2019(21)), created: day),
                Said(101, First, day.AddMinutes(1), "dodaj 1 2 3 4 5 6 7 8 9 10", null, null, "📥"),
                Said(102, First, day.AddMinutes(2), "dodaj 11 12 13 14", null, null, "📥"),
                Said(103, 90, day.AddMinutes(2), "dodaj 50", null, null, "📥"),
                Said(104, First, day.AddMinutes(3), "usuń 12", null, null, "📤"),
                Said(105, First, day.AddMinutes(3.5), "dodaj 99", null, null, "❌"),
                Said(106, First, day.AddMinutes(10), "dodaj 77", "nowy nick", null, "📥")), Bot);

            Assert.Equal((1, 1, 0), (result.Imported, result.CardsFromCommands, result.SkippedMissingCards));
            using var db = Open();
            var exchange = db.CardExchanges.Include(x => x.Cards).Single();
            Assert.Equal((13, 1), (exchange.User1Gave, exchange.User2Gave));
            Assert.Equal(new ulong[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 14 },
                exchange.Cards.Where(x => x.FromUserId == First).Select(x => x.CardId).OrderBy(x => x));
            Assert.Equal(21ul, exchange.Cards.Single(x => x.FromUserId == Second).CardId);
            Assert.Equal(("nowy nick", First), db.PlayerNames.Select(x => new { x.Name, x.UserId }).AsEnumerable().Select(x => (x.Name, x.UserId)).Single());
        }

        [Theory]
        [InlineData("dodaj 1 2 3", "📥", "❌")]
        [InlineData("dodaj 1 2", "📥")]
        public async Task UnclearOrNotMatchingCommands_LeaveTheExchangeOut(string add, params string[] reactions)
        {
            var day = new DateTime(2024, 5, 1, 12, 0, 0);
            var result = await ExchangeImport.ImportAsync(Open, Channel(
                Message(100, Bot, day.AddMinutes(5), null, OldFormat("\n**[13]** kart", OldLine2019(21)), created: day),
                Said(101, First, day.AddMinutes(1), add, null, null, reactions)), Bot);

            Assert.Equal((0, 0, 1), (result.Imported, result.CardsFromCommands, result.SkippedMissingCards));
            using var db = Open();
            Assert.Empty(db.CardExchanges);
            Assert.Empty(db.CardExchangeCards);
            Assert.Empty(db.PlayerNames);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task CutMessage_IsLeftOut_EvenWhenTheCommandsWouldFillIt(bool bothOffersVisible)
        {
            var day = new DateTime(2026, 11, 9, 12, 0, 0);
            var shown = bothOffersVisible ? $"<@{First}> oferuje:\n{OldLine2023(11)}\n\n<@{Second}> oferuje:\n{OldLine2023(31)}\n" : $"<@{First}> oferuje:\n{OldLine2023(11)}\n";
            var result = await ExchangeImport.ImportAsync(Open, Channel(
                Said(99, First, day.AddSeconds(-20), $".exchange <@{Second}> Karna", mentions: new[] { Second }),
                Message(100, Bot, day.AddMinutes(5), ExchangeImport.FinishedTitle, shown.PadRight(2997, 'x') + "...", created: day),
                Said(101, First, day.AddMinutes(1), "dodaj 11", null, null, "📥"),
                Said(102, Second, day.AddMinutes(2), "dodaj 31 32", null, null, "📥")), Bot);

            Assert.Equal((0, 1), (result.Imported, result.SkippedCut));
            using var db = Open();
            Assert.Empty(db.CardExchanges);
        }

        [Fact]
        public async Task StartCommandWithAnotherPlayer_LeavesTheExchangeOut()
        {
            var day = new DateTime(2024, 5, 1, 12, 0, 0);
            var result = await ExchangeImport.ImportAsync(Open, Channel(
                Said(99, First, day.AddSeconds(-10), ".wymiana <@90>", mentions: new ulong[] { 90 }),
                Message(100, Bot, day.AddMinutes(5), null, OldFormat(OldLine2019(11), OldLine2019(21)), created: day)), Bot);

            Assert.Equal((0, 1), (result.Imported, result.SkippedOtherPartner));
        }

        [Fact]
        public async Task StartCommandLongBefore_IsNotTakenForThisExchange()
        {
            var day = new DateTime(2024, 5, 1, 12, 0, 0);
            var result = await ExchangeImport.ImportAsync(Open, Channel(
                Said(99, First, day.AddMinutes(-30), ".wymiana <@90>", mentions: new ulong[] { 90 }),
                Message(100, Bot, day.AddMinutes(5), null, OldFormat(OldLine2019(11), OldLine2019(21)), created: day)), Bot);

            Assert.Equal((1, 0), (result.Imported, result.SkippedOtherPartner));
        }

        [Fact]
        public async Task NewerNameIsKept()
        {
            using (var db = Open())
            {
                await ExchangeImport.RememberNamesAsync(db, new[] { (First, "nowszy", new DateTime(2025, 1, 1)) });
                await db.SaveChangesAsync();
            }
            using (var db = Open())
            {
                await ExchangeImport.RememberNamesAsync(db, new[] { (First, "starszy", new DateTime(2024, 1, 1)), (Second, "drugi", new DateTime(2024, 1, 1)) });
                await db.SaveChangesAsync();
            }

            using var check = Open();
            Assert.Equal(new[] { (First, "nowszy"), (Second, "drugi") }, check.PlayerNames.AsEnumerable().OrderBy(x => x.UserId).Select(x => (x.UserId, x.Name)));
        }
    }
}
