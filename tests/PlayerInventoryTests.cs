using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Sanakan.Api.Controllers;
using Sanakan.Api.Models;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Xunit;

namespace Artifacts
{
    public class PlayerInventoryTests
    {
        [Fact]
        public void BoosterPack_MapsAllFieldsAndNumber()
        {
            var pack = new BoosterPack
            {
                Id = 77, Name = "Pakiet", CardCnt = 3, MinRarity = Rarity.A, IsCardFromPackTradable = true,
                CardSourceFromPack = CardSource.Activity, Title = 1234,
                Characters = new List<BoosterPackCharacter> { new BoosterPackCharacter(), new BoosterPackCharacter() },
            };

            var dto = UserBoosterPack.From(pack, 5);

            Assert.Equal(5, dto.Number);
            Assert.Equal(77ul, dto.Id);
            Assert.Equal("Pakiet", dto.Name);
            Assert.Equal(3, dto.CardCount);
            Assert.Equal(Rarity.A, dto.MinRarity);
            Assert.True(dto.Tradable);
            Assert.Equal(CardSource.Activity, dto.Source);
            Assert.Equal(CardsPoolType.List, dto.Pool);
            Assert.Equal(2, dto.CharacterCount);
        }

        [Fact]
        public void BoosterPack_WithoutCharacters_HasZeroCount()
        {
            Assert.Equal(0, UserBoosterPack.From(new BoosterPack { Characters = null }, 1).CharacterCount);
        }

        [Fact]
        public void BoosterPack_PoolType_FollowsOpeningOrder()
        {
            var list = new List<BoosterPackCharacter> { new BoosterPackCharacter { Character = 5 } };

            Assert.Equal(CardsPoolType.List, UserBoosterPack.From(new BoosterPack { Title = 10, Characters = list }, 1).Pool);
            Assert.Equal(CardsPoolType.Title, UserBoosterPack.From(new BoosterPack { Title = 10, Characters = new List<BoosterPackCharacter>() }, 1).Pool);
            Assert.Equal(CardsPoolType.Random, UserBoosterPack.From(new BoosterPack { Characters = new List<BoosterPackCharacter>() }, 1).Pool);
        }

        [Fact]
        public void BoosterPack_DoesNotLeakTitleOrCharacterIds()
        {
            var pack = new BoosterPack
            {
                Id = 1, Name = "Pakiet", Title = 987654321,
                Characters = new List<BoosterPackCharacter>
                {
                    new BoosterPackCharacter { Id = 11, Character = 123456789 },
                    new BoosterPackCharacter { Id = 12, Character = 555444333 },
                },
            };

            var json = Newtonsoft.Json.JsonConvert.SerializeObject(UserBoosterPack.From(pack, 1));

            Assert.DoesNotContain("987654321", json);
            Assert.DoesNotContain("123456789", json);
            Assert.DoesNotContain("555444333", json);
            Assert.DoesNotContain("Title", json);
            Assert.DoesNotContain("\"Character\"", json);
        }

        [Fact]
        public void BoosterPack_DoesNotExposeGameDeck()
        {
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(UserBoosterPack.From(new BoosterPack
            {
                GameDeck = new GameDeck { Id = 999 }, Characters = new List<BoosterPackCharacter>()
            }, 1));

            Assert.DoesNotContain("GameDeck", json);
        }

        [Fact]
        public void Item_WithQuality_ExposesQuality()
        {
            var type = System.Enum.GetValues<ItemType>().First(x => x.HasDifferentQualities());
            var dto = UserItem.From(new Item { Id = 1, Name = "x", Type = type, Quality = Quality.Gamma, Count = 4 }, 2);

            Assert.Equal(2, dto.Number);
            Assert.Equal(Quality.Gamma, dto.Quality);
            Assert.Equal(4, dto.Count);
        }

        [Fact]
        public void Item_WithoutQuality_HasNullQualityAndDescription()
        {
            var dto = UserItem.From(new Item { Type = ItemType.AffectionRecoverySmall, Quality = Quality.Gamma, Count = 1 }, 1);

            Assert.Null(dto.Quality);
            Assert.False(string.IsNullOrWhiteSpace(dto.Description));
        }

        [Fact]
        public void ItemNumbers_FollowBotOrdering()
        {
            var user = new User { GameDeck = new GameDeck { Items = new List<Item>
            {
                new Item { Id = 1, Type = ItemType.AffectionRecoveryGreat },
                new Item { Id = 2, Type = ItemType.AffectionRecoverySmall },
                new Item { Id = 3, Type = ItemType.AffectionRecoveryNormal },
            } } };

            var expected = user.GetAllItems().Select(x => x.Id).ToList();
            var dtos = user.GetAllItems().Select((x, i) => UserItem.From(x, i + 1)).ToList();

            Assert.Equal(expected, dtos.Select(x => x.Id));
            Assert.Equal(new[] { 1, 2, 3 }, dtos.Select(x => x.Number));
        }

        [Theory]
        [InlineData(nameof(PlayerController.GetUserBoosterPacksAsync), "boosterpacks")]
        [InlineData(nameof(PlayerController.GetUserItemsAsync), "items")]
        public void Endpoints_KeepTheirRoutes(string method, string route)
        {
            var get = typeof(PlayerController).GetMethod(method).GetCustomAttributes(typeof(HttpGetAttribute), true).Cast<HttpGetAttribute>().Single();
            Assert.Equal(route, get.Template);
        }

        [Fact]
        public void PlayerController_RequiresPlayerPolicyEverywhere()
        {
            var type = typeof(PlayerController);

            var auth = type.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single();
            Assert.Equal("Player", auth.Policy);

            var route = type.GetCustomAttributes(typeof(RouteAttribute), true).Cast<RouteAttribute>().Single();
            Assert.Equal("api/waifu", route.Template);

            var actions = type.GetMethods().Where(x => x.GetCustomAttributes(typeof(HttpMethodAttribute), true).Any()).ToList();
            Assert.NotEmpty(actions);
            Assert.All(actions, x => Assert.Empty(x.GetCustomAttributes(typeof(AllowAnonymousAttribute), true)));
        }
    }
}
