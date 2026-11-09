using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sanakan.Database;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Xunit;

namespace Artifacts
{
    public class PowerActivityTests : IDisposable
    {
        private readonly SqliteConnection _connection = new SqliteConnection("Data Source=:memory:");

        private sealed class TestDatabase : DatabaseContext
        {
            private readonly SqliteConnection _connection;
            public TestDatabase(SqliteConnection connection) : base(new FakeConfig()) => _connection = connection;
            protected override void OnConfiguring(DbContextOptionsBuilder builder)
                => builder.UseSqlite(_connection).AddInterceptors(PowerActivity.Interceptor);
        }

        public PowerActivityTests()
        {
            _connection.Open();
            using var db = Open();
            db.Database.EnsureCreated();
            foreach (ulong id in new ulong[] { 5, 6 })
            {
                var user = new User().Default(id, DateTime.Now);
                user.GameDeck.Cards.Add(Card(id * 10, active: true));
                user.GameDeck.Cards.Add(Card(id * 10 + 1, active: false));
                db.Users.Add(user);
            }
            db.SaveChanges();
        }

        public void Dispose() => _connection.Dispose();

        private DatabaseContext Open() => new TestDatabase(_connection);

        private static Card Card(ulong id, bool active) => new Card
        {
            Id = id, Rarity = Rarity.SSS, Health = 500, Attack = 300, Defence = 90, Dere = Dere.Deredere,
            Active = active, Image = "https://example.com/a.png", Tags = new List<Tag>(),
        };

        private async Task<(double Power, long Cards)> DeckAsync(ulong id)
        {
            using var db = Open();
            var deck = await db.GameDecks.AsQueryable().SingleAsync(x => x.Id == id);
            return (deck.DeckPower, deck.CardsInDeck);
        }

        private async Task<double> CardPowerAsync(ulong id)
        {
            using var db = Open();
            return (await db.Cards.AsQueryable().SingleAsync(x => x.Id == id)).CardPower;
        }

        [Fact]
        public async Task NewCards_GetTheirPower_AndTheDeckSumsTheActiveOnes()
        {
            var deck = await DeckAsync(5);

            Assert.True(await CardPowerAsync(50) > 0);
            Assert.Equal(await CardPowerAsync(50), deck.Power, 6);
            Assert.Equal(1, deck.Cards);
        }

        [Fact]
        public async Task ChangingAnActiveCard_RefreshesItsPowerAndTheDeck()
        {
            var before = await CardPowerAsync(50);
            using (var db = Open())
            {
                (await db.Cards.AsQueryable().SingleAsync(x => x.Id == 50)).Affection = 40;
                await db.SaveChangesAsync();
            }

            var after = await CardPowerAsync(50);
            Assert.True(after > before);
            Assert.Equal(after, (await DeckAsync(5)).Power, 6);
        }

        [Fact]
        public async Task ChangingAnInactiveCard_DoesNotChangeTheDeck()
        {
            var deckBefore = await DeckAsync(5);
            using (var db = Open())
            {
                (await db.Cards.AsQueryable().SingleAsync(x => x.Id == 51)).Affection = 40;
                await db.SaveChangesAsync();
            }

            Assert.Equal(deckBefore, await DeckAsync(5));
        }

        [Fact]
        public async Task ActivatingACard_AddsItToTheDeck()
        {
            using (var db = Open())
            {
                (await db.Cards.AsQueryable().SingleAsync(x => x.Id == 51)).Active = true;
                await db.SaveChangesAsync();
            }

            var deck = await DeckAsync(5);
            Assert.Equal(2, deck.Cards);
            Assert.Equal(await CardPowerAsync(50) + await CardPowerAsync(51), deck.Power, 6);
        }

        [Fact]
        public async Task MovingAnActiveCard_RefreshesBothOwners()
        {
            using (var db = Open())
            {
                (await db.Cards.AsQueryable().SingleAsync(x => x.Id == 50)).GameDeckId = 6;
                await db.SaveChangesAsync();
            }

            Assert.Equal((0d, 0L), await DeckAsync(5));
            Assert.Equal(2, (await DeckAsync(6)).Cards);
        }

        [Fact]
        public async Task TheSetDeckAgainMarker_IsKept()
        {
            using (var db = Open())
            {
                (await db.GameDecks.AsQueryable().SingleAsync(x => x.Id == 5)).DeckPower = -1;
                await db.SaveChangesAsync();
            }
            using (var db = Open())
            {
                (await db.Cards.AsQueryable().SingleAsync(x => x.Id == 50)).Affection = 40;
                await db.SaveChangesAsync();
            }

            Assert.Equal(-1, (await DeckAsync(5)).Power);
        }
    }
}
