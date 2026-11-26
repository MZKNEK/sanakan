using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.ShindenCompat;
using Xunit;

namespace Artifacts
{
    public class PackStorageTests : IDisposable
    {
        private const ulong Player = 10;
        private const ulong Character = CustomCharacters.IdStart + 1;

        private readonly WaifuCommandHarness _h = new WaifuCommandHarness();

        public PackStorageTests()
        {
            _h.Shinden.CustomCharacterInfo = id => Task.FromResult<Sanakan.ShindenCompat.Models.ICharacterInfo>(
                new Sanakan.ShindenCompat.Models.Entities.CharacterInfo { Id = id, FirstName = "Postać", LastName = $"{id % 1000}" });
        }

        public void Dispose() => _h.Dispose();

        private async Task SeedAsync(params BoosterPack[] packs)
        {
            using var db = await _h.CreateAsync();
            var user = new User().Default(Player, _h.Time.Now());
            user.GameDeck.BoosterPacks.Clear();
            foreach (var pack in packs)
                user.GameDeck.BoosterPacks.Add(pack);
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        private List<BoosterPack> ReadPacks(bool tracked)
        {
            using var db = _h.Open();
            var query = db.BoosterPacks.Where(x => x.GameDeckId == Player);
            return (tracked ? query : query.AsNoTracking()).AsEnumerable().OrderBy(x => x.Id).ToList();
        }

        [Fact]
        public async Task NewPack_GetsItsGrantDate_AndALoadedPackKeepsItsOwn()
        {
            var before = DateTime.Now;
            await SeedAsync(new BoosterPack { Name = "nowy", CardCnt = 1 }, new BoosterPack { Name = "stary", CardCnt = 1 });
            using (var db = _h.Open())
                db.Database.ExecuteSqlRaw("UPDATE BoosterPacks SET CreationDate = '2026-01-02 03:04:05' WHERE Name = 'stary'");

            foreach (var tracked in new[] { true, false })
            {
                var packs = ReadPacks(tracked);
                Assert.InRange(packs.Single(x => x.Name == "nowy").CreationDate, before, DateTime.Now);
                Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 5), packs.Single(x => x.Name == "stary").CreationDate);
            }
        }

        [Fact]
        public async Task ShopPack_KeepsItsExcludedRarities()
        {
            await SeedAsync(ItemType.RandomBoosterPackSingleE.ToBoosterPack(), ItemType.RandomNormalBoosterPackB.ToBoosterPack(),
                ItemType.SeasonBoosterPack.ToBoosterPack());

            var packs = ReadPacks(false);
            Assert.Equal(new[] { Rarity.SS, Rarity.S, Rarity.A }, packs[0].GetExcludedRarities());
            Assert.Equal(new[] { Rarity.SS }, packs[1].GetExcludedRarities());
            Assert.Empty(packs[2].GetExcludedRarities());
        }

        [Fact]
        public async Task PackCharacters_AreStoredWithThePack()
        {
            await SeedAsync(new BoosterPack { Name = "lista", CardCnt = 1, Characters = new List<ulong> { 18446744073709551615, 5, 5 } },
                new BoosterPack { Name = "losowy", CardCnt = 1 });

            var packs = ReadPacks(false);
            Assert.Equal(new ulong[] { 18446744073709551615, 5, 5 }, packs[0].Characters);
            Assert.Empty(packs[1].Characters);
        }

        [Fact]
        public void ApiPack_StoresItsExcludedRarities()
        {
            var pack = new Sanakan.Api.Models.CardBoosterPack { Count = 1, RarityExcluded = new List<Rarity> { Rarity.SSS, Rarity.A, Rarity.A } }.ToRealPack();

            Assert.Equal(0b1001, pack.ExcludedRarities);
            Assert.Equal(new[] { Rarity.SSS, Rarity.A }, pack.GetExcludedRarities());
            Assert.Equal(0, new Sanakan.Api.Models.CardBoosterPack { Count = 1 }.ToRealPack().ExcludedRarities);
        }

        [Fact]
        public async Task OpeningPack_NeverDrawsAnExcludedRarity()
        {
            var excluded = new[] { Rarity.SS, Rarity.S, Rarity.A, Rarity.B, Rarity.C, Rarity.D };
            await SeedAsync(new BoosterPack
            {
                Name = "same E", CardCnt = 20, MinRarity = Rarity.E, IsCardFromPackTradable = true, CardSourceFromPack = CardSource.Api,
                ExcludedRarities = BoosterPack.RarityMask(excluded),
                Characters = new List<ulong> { Character },
            });

            var reply = await _h.RunAsync(Player, x => x.OpenPacketAsync(1));

            Assert.Contains("wypadło", reply);
            using var db = _h.Open();
            var rarities = db.Cards.AsNoTracking().Where(x => x.GameDeckId == Player).Select(x => x.Rarity).ToList();
            Assert.Equal(20, rarities.Count);
            Assert.All(rarities, x => Assert.Equal(Rarity.E, x));
        }
    }
}
