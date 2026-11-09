using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sanakan.Database;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services.PocketWaifu;
using Xunit;

namespace Artifacts
{
    public class CardPowerTests : IDisposable
    {
        private readonly SqliteConnection _connection = new SqliteConnection("Data Source=:memory:");

        private sealed class TestDatabase : DatabaseContext
        {
            private readonly SqliteConnection _connection;
            public TestDatabase(SqliteConnection connection) : base(new FakeConfig()) => _connection = connection;
            protected override void OnConfiguring(DbContextOptionsBuilder builder) => builder.UseSqlite(_connection);
        }

        public CardPowerTests()
        {
            _connection.Open();
            using var db = Open();
            db.Database.EnsureCreated();
        }

        public void Dispose() => _connection.Dispose();

        private DatabaseContext Open() => new TestDatabase(_connection);

        private static Card Card(bool ultimate, int health, int attack, int defence, Dere dere) => new Card
        {
            FromFigure = ultimate,
            Rarity = Rarity.SSS,
            Health = health,
            Attack = attack,
            Defence = defence,
            Dere = dere,
            Image = "https://example.com/a.png",
            Tags = new List<Tag>(),
        };

        // expected values come from the duel simulator the formula was fitted on
        [Theory]
        [InlineData(false, 999, 999, 99, Dere.Deredere, 315.64)]
        [InlineData(false, 120, 115, 92, Dere.Tsundere, 8.91)]
        [InlineData(true, 10000, 3000, 3000, Dere.Kamidere, 2992.43)]
        [InlineData(true, 99999, 9999, 9999, Dere.Raito, 66896.71)]
        [InlineData(true, 99999, 9999, 9999, Dere.Yato, 77720.50)]
        public void Power_MatchesTheSimulator(bool ultimate, int health, int attack, int defence, Dere dere, double expected)
        {
            var card = Card(ultimate, health, attack, defence, dere);

            Assert.Equal(expected, card.CalculateCardPower(), 1);
            Assert.Equal(card.CardPower, card.CalculateCardPower());
        }

        [Fact]
        public void Power_ValuesDefenceUpTo9999_AndPenalisesAMissingPicture()
        {
            var fullDefence = Card(true, 42000, 9999, 9999, Dere.Deredere);
            var halfDefence = Card(true, 46999, 9999, 5000, Dere.Deredere);
            var noPicture = Card(false, 999, 999, 99, Dere.Deredere);
            noPicture.Image = null;

            Assert.True(fullDefence.CalculateCardPower() > halfDefence.CalculateCardPower() * 1.4);
            Assert.True(noPicture.CalculateCardPower() < 100);
        }

        [Fact]
        public void Power_RanksUltimateDefenceAndTsundereWeakness()
        {
            var tank = Card(true, 10000, 3000, 3000, Dere.Kamidere).CalculateCardPower();
            var glass = Card(true, 10000, 3000, 300, Dere.Kamidere).CalculateCardPower();
            var tsundere = Card(true, 10000, 3000, 3000, Dere.Tsundere).CalculateCardPower();

            Assert.True(tank > glass * 1.2);
            Assert.True(tsundere < tank);
        }

        private static GameDeck Deck(ulong id, double power, double mmr) => new GameDeck { Id = id, UserId = id, DeckPower = power, MatachMakingRatio = mmr };

        [Fact]
        public void Opponents_ComeFromTheClosestPowerRangeWithEnoughPlayers()
        {
            var player = Deck(1, 1000, 1);
            var pool = Enumerable.Range(0, 12).Select(i => Deck(10UL + (ulong)i, 900 + i * 20, 1))
                .Concat(Enumerable.Range(0, 30).Select(i => Deck(100UL + (ulong)i, 50000, 1))).ToList();

            var candidates = player.GetPvpOpponentCandidates(pool);

            Assert.True(candidates.Count >= 10);
            Assert.All(candidates, x => Assert.InRange(x.DeckPower, 800, 1250));
        }

        [Fact]
        public void Opponents_WidenThePowerRangeWhenFewPlayersAreClose()
        {
            var player = Deck(1, 1000, 1);
            var pool = Enumerable.Range(0, 10).Select(i => Deck(10UL + (ulong)i, 2500, 1))
                .Concat(Enumerable.Range(0, 10).Select(i => Deck(100UL + (ulong)i, 90000, 1))).ToList();

            var candidates = player.GetPvpOpponentCandidates(pool);

            Assert.Equal(10, candidates.Count);
            Assert.All(candidates, x => Assert.Equal(2500, x.DeckPower));
        }

        [Fact]
        public void Opponents_ForAFarStrongerDeckAreTheClosestOnes()
        {
            var player = Deck(1, 43600, 1);
            var pool = Enumerable.Range(0, 30).Select(i => Deck(10UL + (ulong)i, 500 + i * 70, 1)).ToList();

            var candidates = player.GetPvpOpponentCandidates(pool);

            Assert.Equal(10, candidates.Count);
            Assert.All(candidates, x => Assert.True(x.DeckPower >= 500 + 20 * 70));
        }

        [Fact]
        public void Opponents_StillUseMmrInsideThePowerRange()
        {
            var player = Deck(1, 1000, 5);
            var pool = Enumerable.Range(0, 10).Select(i => Deck(10UL + (ulong)i, 1000, 5))
                .Concat(Enumerable.Range(0, 10).Select(i => Deck(100UL + (ulong)i, 1000, 50))).ToList();

            var candidates = player.GetPvpOpponentCandidates(pool);

            Assert.All(candidates, x => Assert.Equal(5, x.MatachMakingRatio));
        }

        [Fact]
        public async Task Recalculation_UpdatesStoredCardAndDeckPower()
        {
            using (var db = Open())
            {
                var user = new User().Default(5, DateTime.Now);
                var active = Card(true, 10000, 3000, 3000, Dere.Kamidere);
                active.Active = true;
                active.CardPower = 1;
                var inactive = Card(false, 999, 999, 99, Dere.Deredere);
                inactive.CardPower = 1;
                user.GameDeck.Cards.Add(active);
                user.GameDeck.Cards.Add(inactive);
                user.GameDeck.DeckPower = 2;
                db.Users.Add(user);
                await db.SaveChangesAsync();
            }

            var first = await PowerRecalculation.RecalculateAllAsync(Open, batchSize: 1, pause: TimeSpan.Zero);
            var second = await PowerRecalculation.RecalculateAllAsync(Open, batchSize: 1, pause: TimeSpan.Zero);

            Assert.Equal(new PowerRecalculationResult(2, 2, 1, 1), first);
            Assert.Equal(new PowerRecalculationResult(2, 0, 1, 0), second);

            using var read = Open();
            var stored = (await read.Cards.AsQueryable().ToListAsync()).OrderBy(x => x.Id).ToList();
            Assert.Equal(2992.43, stored[0].CardPower, 1);
            Assert.Equal(315.64, stored[1].CardPower, 1);
            Assert.Equal(stored[0].CardPower, (await read.GameDecks.AsQueryable().SingleAsync()).DeckPower, 3);
        }

        [Fact]
        public async Task Recalculation_KeepsTheSetDeckAgainMarker()
        {
            using (var db = Open())
            {
                var user = new User().Default(6, DateTime.Now);
                var card = Card(false, 999, 999, 99, Dere.Deredere);
                card.Active = true;
                user.GameDeck.Cards.Add(card);
                user.GameDeck.DeckPower = -1;
                db.Users.Add(user);
                await db.SaveChangesAsync();
            }

            await PowerRecalculation.RecalculateAllAsync(Open, pause: TimeSpan.Zero);

            using var read = Open();
            Assert.Equal(-1, (await read.GameDecks.AsQueryable().SingleAsync()).DeckPower);
        }
    }
}
