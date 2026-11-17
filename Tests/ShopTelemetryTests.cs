using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services;
using Sanakan.Services.PocketWaifu;
using Xunit;

namespace Artifacts
{
    // tests that flush GameTelemetry.Default run one at a time, a parallel flush would take the other's counters
    [CollectionDefinition(Name)]
    public class SharedTelemetry
    {
        public const string Name = "GameTelemetry.Default";
    }

    [Collection(SharedTelemetry.Name)]
    public class ShopTelemetryTests
    {
        [Fact]
        public async Task Purchase_IsLabelledWithTheShopItem()
        {
            using var harness = new WaifuCommandHarness();
            using (var db = await harness.CreateAsync())
            {
                var user = new User().Default(95, harness.Time.Now());
                user.AcCnt = 100000;
                db.Users.Add(user);
                await db.SaveChangesAsync();
            }
            var waifu = new Waifu(null, null, null, new ListLogger(), null, null, null, harness.Time, harness.Shinden, harness.Tags, harness.Config);
            var item = waifu.GetItemsWithCostForShop(ShopType.Activity)[0];
            var label = waifu.GetShopItemLabel(ShopType.Activity, 1, item);
            Assert.Equal($"Kiosk:1:{item.Item.Name}", label);
            GameTelemetry.Reason = "kiosk";

            var reply = await harness.RunAsync(95, m => m.BuyItemActivityAsync(1, "2"), waifu);

            Assert.Contains("zakupił", reply);
            Assert.Equal("kiosk", GameTelemetry.Reason);
            await GameTelemetry.Default.FlushAsync(harness.Open);
            using var check = harness.Open();
            var stats = await check.GameStats.AsNoTracking().ToListAsync();
            Assert.Equal(Waifu.GetShopCost(2, item.Cost), stats.Single(x => x.Metric == "currency_out" && x.Label == $"AC:{label}").Value);
            Assert.Equal(2, stats.Single(x => x.Metric == "item_in" && x.Label == $"{item.Item.Type}:{label}").Value);
        }
    }
}
