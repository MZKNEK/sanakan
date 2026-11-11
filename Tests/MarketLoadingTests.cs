using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Xunit;

namespace Artifacts
{
    // rynek and czarny rynek load only the chosen card; items go to the ones the player already owns
    public class MarketLoadingTests : IDisposable
    {
        private const ulong Player = 10;

        private readonly WaifuCommandHarness _h = new WaifuCommandHarness();

        public void Dispose() => _h.Dispose();

        private async Task SeedAsync(double karma)
        {
            using var db = await _h.CreateAsync();

            var user = new User().Default(Player, _h.Time.Now());
            user.GameDeck.Karma = karma;
            foreach (var id in new ulong[] { 501, 502 })
                user.GameDeck.Cards.Add(new Card { Id = id, Character = id, Name = $"postać {id}", Rarity = Rarity.A, Affection = 30, Attack = 50, Defence = 20,
                    Health = 100, Tags = new List<Tag>(), CreationDate = new DateTime(2026, 1, 1) });
            // every item a market can give is already owned once, so a duplicate row would mean the items were not loaded
            foreach (var type in Enum.GetValues<ItemType>())
                user.GameDeck.Items.Add(type.ToItem(1));
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        private Task<string> GoAsync(bool black, ulong wid)
            => _h.RunAsync(Player, x => black ? x.GoToBlackMarketAsync(wid) : x.GoToMarketAsync(wid));

        private GameDeck Read()
        {
            using var db = _h.Open();
            return db.GameDecks.AsNoTracking().Include(x => x.Cards).Include(x => x.Items).Include(x => x.User).ThenInclude(x => x.TimeStatuses)
                .AsSplitQuery().Single(x => x.Id == Player);
        }

        [Theory]
        [InlineData(false, 0)]
        [InlineData(true, -500)]
        public async Task Market_AddsToTheOwnedItems_AndChangesOnlyTheChosenCard(bool black, double karma)
        {
            await SeedAsync(karma);
            var itemsBefore = Read().Items.Sum(x => x.Count);

            var reply = await GoAsync(black, 501);

            Assert.Contains("udało Ci się zdobyć", reply);
            var deck = Read();
            Assert.Empty(deck.Items.GroupBy(x => (x.Type, x.Quality)).Where(x => x.Count() > 1));
            // 1 + affection 30 / 15, plus one for the ring or the blood
            Assert.True(deck.Items.Sum(x => x.Count) >= itemsBefore + 4);
            var cards = deck.Cards.ToDictionary(x => x.Id);
            Assert.Equal(black ? 29.8 : 30.1, cards[501].Affection, 6);
            Assert.Equal(30, cards[502].Affection);
            Assert.Equal(_h.Time.Now().AddHours(black ? 15 : 20), deck.User.TimeStatuses.Single(x => x.Type == StatusType.Market).EndsAt);
            Assert.Equal(1, deck.User.TimeStatuses.Single(x => x.Type == StatusType.DMarket).IValue);
        }

        [Theory]
        [InlineData(false, 0)]
        [InlineData(true, -500)]
        public async Task CardOfAnotherPlayer_IsRefused(bool black, double karma)
        {
            await SeedAsync(karma);

            Assert.Contains("nie posiadasz takiej karty", await GoAsync(black, 999));
            Assert.DoesNotContain(Read().User.TimeStatuses, x => x.Type == StatusType.Market);
        }

        [Theory]
        [InlineData(false, -500, "rozbiegli")]
        [InlineData(true, 0, "nie miejsce dla Ciebie")]
        public async Task WrongKarma_IsRefused(bool black, double karma, string expected)
        {
            await SeedAsync(karma);

            Assert.Contains(expected, await GoAsync(black, 501));
        }
    }
}
