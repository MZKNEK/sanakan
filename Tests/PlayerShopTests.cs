using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Sanakan.Api;
using Sanakan.Api.Controllers;
using Sanakan.Api.Models;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services.PocketWaifu;
using Sanakan.Services.Time;
using Xunit;

namespace Artifacts
{
    public class PlayerShopTests : IDisposable
    {
        private readonly ApiServer _api = new ApiServer();
        public void Dispose() => _api.Dispose();

        private void AsPlayer()
        {
            var token = UserTokenBuilder.BuildUserToken(_api.Config, new User { Id = 42 }, new SystemTime());
            _api.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        }

        private static Waifu Waifu() => new Waifu(null, null, null, new ListLogger(), null, null, null,
            new FixedTime(), null, null, new FakeConfig());

        [Fact]
        public async Task ShopList_ReturnsAllShopsAndTheirOffers()
        {
            AsPlayer();
            var response = await _api.Client.GetAsync("/api/waifu/shops");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var shops = JArray.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(new[] { "normal", "pvp", "activity" }, shops.Select(x => x.Value<string>("type")));
            Assert.Equal(new[] { "Sklepik", "Koszary", "Kiosk" }, shops.Select(x => x.Value<string>("name")));
            Assert.Equal(new[] { "TC", "PC", "AC" }, shops.Select(x => x.Value<string>("currency")));
            Assert.All(shops, shop =>
            {
                Assert.False(string.IsNullOrWhiteSpace(shop.Value<string>("description")));
                Assert.NotEmpty((JArray)shop["items"]);
            });
        }

        [Theory]
        [InlineData("normal", ShopType.Normal, "TC")]
        [InlineData("pvp", ShopType.Pvp, "PC")]
        [InlineData("activity", ShopType.Activity, "AC")]
        [InlineData("0", ShopType.Normal, "TC")]
        [InlineData("1", ShopType.Pvp, "PC")]
        [InlineData("2", ShopType.Activity, "AC")]
        public async Task Shop_UsesTheSameItemsPricesDescriptionsAndNumbersAsBot(string parameter, ShopType type, string currency)
        {
            AsPlayer();
            var response = await _api.Client.GetAsync($"/api/waifu/shops/{parameter}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var shop = JObject.Parse(await response.Content.ReadAsStringAsync());
            var waifu = Waifu();
            Assert.Equal(waifu.GetShopName(type), shop.Value<string>("name"));
            Assert.Equal(currency, shop.Value<string>("currency"));
            var items = (JArray)shop["items"];
            var expected = waifu.GetItemsWithCostForShop(type);
            Assert.Equal(expected.Length, items.Count);
            for (var i = 0; i < expected.Length; i++)
            {
                Assert.Equal(i + 1, items[i].Value<int>("number"));
                Assert.Equal(expected[i].Item.Name, items[i].Value<string>("name"));
                Assert.Equal(expected[i].Cost, items[i].Value<long>("price"));
                Assert.Equal(expected[i].Item.Type.Desc(), items[i].Value<string>("description"));
                Assert.False(string.IsNullOrWhiteSpace(items[i].Value<string>("description")));
                Assert.NotNull(items[i]["type"]);
            }
            Assert.Empty(_api.Executor.Added);
        }

        [Theory]
        [InlineData("unknown")]
        [InlineData("-1")]
        [InlineData("999")]
        public async Task InvalidShopType_Returns400(string parameter)
        {
            AsPlayer();

            Assert.Equal(HttpStatusCode.BadRequest, (await _api.Client.GetAsync($"/api/waifu/shops/{parameter}")).StatusCode);
        }

        [Theory]
        [InlineData("/api/waifu/shops")]
        [InlineData("/api/waifu/shops/normal")]
        public async Task Shops_WithoutPlayerAuthentication_Return401(string route)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await _api.Client.GetAsync(route)).StatusCode);
        }

        [Theory]
        [InlineData("/api/waifu/shops")]
        [InlineData("/api/waifu/shops/normal")]
        public async Task Shops_SiteTokenDoesNotGrantPlayerAccess(string route)
        {
            await _api.AsSiteAsync();

            Assert.Equal(HttpStatusCode.Forbidden, (await _api.Client.GetAsync(route)).StatusCode);
        }

        [Fact]
        public void PlayerIdentityWithoutDiscordId_Returns403()
        {
            var controller = ControllerHarness.Attach(new PlayerController(new FakeConfig(), new FixedTime(),
                new FakeExecutor(), null, Waifu()));
            controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("Player", "waifu_player") }, "test"));

            Assert.Equal(403, controller.GetShopList().StatusOf());
            Assert.Equal(403, controller.GetShop(ShopType.Normal).StatusOf());
        }
    }
}
