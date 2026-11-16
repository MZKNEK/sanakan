using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Sanakan.Config.Model;
using Sanakan.Database;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services.Cache;
using Sanakan.Services.PocketWaifu;
using Sanakan.ShindenCompat.Models;
using Xunit;

namespace Artifacts
{
    public class SeasonShopPackTests : IDisposable
    {
        private const ulong PlayerId = 97001;
        private const int ShopNumber = 14;

        private readonly string _path = Path.Combine(Path.GetTempPath(), $"sanakan-season-shop-{Guid.NewGuid():N}.db");
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "sanakan-season-shop-" + Guid.NewGuid().ToString("N"));
        private readonly FakeConfig _config = new FakeConfig();
        private readonly FixedTime _time = new FixedTime { Value = new DateTime(2026, 11, 16, 12, 0, 0) };

        public SeasonShopPackTests()
        {
            _config.Model.DatabaseProvider = DatabaseProvider.Sqlite;
            _config.Model.ConnectionString = $"Data Source={_path}";

            using var db = new DatabaseContext(_config);
            db.EnsureReady();
            var user = new User().Default(PlayerId, _time.Now());
            user.TcCnt = 1000;
            db.Users.Add(user);
            db.SaveChanges();
        }

        public void Dispose()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(_path); } catch (IOException) { }
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, true);
        }

        private Waifu NewWaifu(bool withSeason) => new Waifu(null, null, null, new ListLogger(), null, null, null, _time,
            new Sanakan.Services.Shinden(null, null, null, new ListLogger(), _time, withSeason ? _dir : null), null, _config);

        private Task SaveSeasonAsync() => CacheFile.WriteAsync(
            Path.Combine(_dir, "seasons", $"{SeasonSnapshot.KeyOf(2026, AnimeSeason.Autumn)}.json"),
            new SeasonSnapshot { Year = 2026, Season = AnimeSeason.Autumn, UpdatedAt = _time.Now(), Titles = new List<ulong> { 1 }, Characters = new List<ulong> { 15 } });

        private User Player()
        {
            using var db = new DatabaseContext(_config);
            return db.Users.AsNoTracking().Include(x => x.GameDeck).ThenInclude(x => x.BoosterPacks).First(x => x.Id == PlayerId);
        }

        [Fact]
        public void TheShopSellsTheSeasonPackInPlaceOfTheColouredOnes()
        {
            var items = NewWaifu(false).GetItemsWithCost();

            Assert.Equal(ItemType.SeasonBoosterPack, items[ShopNumber - 1].Item.Type);
            Assert.Equal(404, items[ShopNumber - 1].Cost);
            Assert.DoesNotContain(items, x => x.Item.Type is ItemType.RandomNormalBoosterPackB or ItemType.RandomNormalBoosterPackA
                or ItemType.RandomNormalBoosterPackS or ItemType.RandomNormalBoosterPackSS);
        }

        [Fact]
        public async Task BuyingIt_GivesTwoTradableCardsOfTheCurrentSeason()
        {
            await SaveSeasonAsync();

            var result = await NewWaifu(true).ExecuteShopAsync(ShopType.Normal, _config, Users.Discord(PlayerId), ShopNumber, "2");

            Assert.Contains("Pakiet kart z sezonu (jesień 2026) x2", result.Description);
            var player = Player();
            Assert.Equal(1000 - 2 * 404, player.TcCnt);
            var bought = player.GameDeck.BoosterPacks.Where(x => x.CardSourceFromPack == CardSource.Shop).ToList();
            Assert.Equal(2, bought.Count);
            Assert.All(bought, pack =>
            {
                Assert.Equal("Pakiet kart z sezonu (jesień 2026)", pack.Name);
                Assert.Equal("2026-autumn", pack.Season);
                Assert.Equal(2, pack.CardCnt);
                Assert.True(pack.IsCardFromPackTradable);
                Assert.Equal(Rarity.E, pack.MinRarity);
                });
        }

        [Fact]
        public async Task WithoutASeasonFromShinden_NothingIsSoldNorCharged()
        {
            var result = await NewWaifu(false).ExecuteShopAsync(ShopType.Normal, _config, Users.Discord(PlayerId), ShopNumber, "1");

            Assert.Contains("sezonu", result.Description);
            var player = Player();
            Assert.Equal(1000, player.TcCnt);
            Assert.DoesNotContain(player.GameDeck.BoosterPacks, x => x.CardSourceFromPack == CardSource.Shop);
        }
    }
}
