using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Sanakan.Database.Models;
using Sanakan.Database.Models.Analytics;
using Sanakan.Extensions;
using Sanakan.ShindenCompat;
using Xunit;

namespace Artifacts
{
    // pakiet and druciarstwo do not load the collection: the cards are counted and only the drawn characters' wishes are read
    public class PackLoadingTests : IDisposable
    {
        private const ulong Player = 10;
        private const ulong Wished = CustomCharacters.IdStart + 1, Other = CustomCharacters.IdStart + 2, Kept = CustomCharacters.IdStart + 3;

        private readonly WaifuCommandHarness _h = new WaifuCommandHarness();

        public PackLoadingTests()
        {
            _h.Shinden.CustomCharacterInfo = id => Task.FromResult<Sanakan.ShindenCompat.Models.ICharacterInfo>(
                new Sanakan.ShindenCompat.Models.Entities.CharacterInfo { Id = id, FirstName = "Postać", LastName = $"{id % 1000}" });
        }

        public void Dispose() => _h.Dispose();

        private async Task SeedAsync(long maxCards = 1000)
        {
            using var db = await _h.CreateAsync();

            var user = new User().Default(Player, _h.Time.Now());
            var deck = user.GameDeck;
            deck.MaxNumberOfCards = maxCards;
            deck.BoosterPacks.Clear();
            deck.BoosterPacks.Add(NewPack("Pierwszy", 2, Wished));
            deck.BoosterPacks.Add(NewPack("Drugi", 1, Other));
            deck.BoosterPacks.Add(NewPack("Trzeci", 1, Kept));
            foreach (var id in new ulong[] { 501, 502 })
                deck.Cards.Add(new Card { Id = id, Character = id, Name = "stara", Rarity = Rarity.A, Tags = new List<Tag>(), CreationDate = new DateTime(2026, 1, 1) });
            foreach (var character in new[] { Wished, Kept })
                deck.Wishes.Add(new WishlistObject { Type = WishlistObjectType.Character, ObjectId = character, ObjectName = "x", Entry = WishlistEntryType.Normal });
            deck.Items.Add(ItemType.CardFragment.ToItem(10000));
            db.Users.Add(user);
            db.WishlistCountData.Add(new WishlistCount { Id = Wished, Name = "x", Count = 5, ACount = 5 });
            await db.SaveChangesAsync();
        }

        private static BoosterPack NewPack(string name, int cards, ulong character) => new BoosterPack
        {
            Name = name, CardCnt = cards, MinRarity = Rarity.E, IsCardFromPackTradable = true, CardSourceFromPack = CardSource.Api,
            Characters = new List<BoosterPackCharacter> { new BoosterPackCharacter { Character = character } },
            RarityExcludedFromPack = new List<RarityExcluded>(),
        };

        private (List<Card> Cards, List<WishlistObject> Wishes, List<BoosterPack> Packs, WishlistCount Count, UserStats Stats) Read()
        {
            using var db = _h.Open();
            var deck = db.GameDecks.AsNoTracking().Include(x => x.Cards).Include(x => x.Wishes).Include(x => x.BoosterPacks).Include(x => x.User).ThenInclude(x => x.Stats)
                .AsSplitQuery().Single(x => x.Id == Player);
            return (deck.Cards.ToList(), deck.Wishes.ToList(), deck.BoosterPacks.ToList(), db.WishlistCountData.AsNoTracking().Single(x => x.Id == Wished), deck.User.Stats);
        }

        [Fact]
        public async Task OpeningPacks_AddsTheCards_AndRemovesTheWishOnce()
        {
            await SeedAsync();

            var reply = await _h.RunAsync(Player, x => x.OpenPacketAsync(1, 2));

            Assert.Contains("wypadło", reply);
            var (cards, wishes, packs, count, stats) = Read();
            Assert.Equal(5, cards.Count);
            Assert.Equal(2, cards.Count(x => x.Character == Wished));
            Assert.Equal(new[] { Kept }, wishes.Select(x => x.ObjectId));
            Assert.Equal(new[] { "Trzeci" }, packs.Select(x => x.Name));
            Assert.Equal(4, count.Count);
            Assert.Equal(2, stats.OpenedBoosterPacks);
        }

        [Fact]
        public async Task OpeningPacks_CountsTheWholeCollection()
        {
            await SeedAsync(maxCards: 3);

            Assert.Contains("nie masz w kolekcji miejsca", await _h.RunAsync(Player, x => x.OpenPacketAsync(1, 1)));
            Assert.Equal(3, Read().Packs.Count);
        }

        [Fact]
        public async Task PackList_ShowsEveryPack()
        {
            await SeedAsync();

            var reply = await _h.RunAsync(Player, x => x.OpenPacketAsync());

            Assert.Contains("Pierwszy", reply);
            Assert.Contains("Trzeci", reply);
        }

        [Fact]
        public async Task Tinkering_TheSameWishedCharacterTwice_RemovesTheWishOnce()
        {
            await SeedAsync();
            _h.Pool.Add((Wished, "Postać"));

            var reply = await _h.RunAsync(Player, x => x.MakeCardsFromFragmentsAsync(3));

            Assert.DoesNotContain("niestety", reply);
            var (cards, wishes, _, count, stats) = Read();
            Assert.Equal(3, cards.Count(x => x.Character == Wished));
            Assert.Equal(new[] { Kept }, wishes.Select(x => x.ObjectId));
            Assert.Equal(4, count.Count);
            Assert.Equal(3, stats.CreatedCardsFromItems);
        }

        [Fact]
        public async Task Tinkering_CountsTheWholeCollection()
        {
            await SeedAsync(maxCards: 3);
            _h.Pool.Add((Other, "Postać"));

            Assert.Contains("nie masz miejsca na tyle kart", await _h.RunAsync(Player, x => x.MakeCardsFromFragmentsAsync(2)));
            Assert.Equal(2, Read().Cards.Count);
        }
    }
}
