using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Sanakan.Api.Controllers;
using Sanakan.Api.Models;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services.PocketWaifu;
using Sanakan.ShindenCompat;
using Xunit;

namespace Artifacts
{
    // the player's read endpoints and the pack opening load only the parts they use and answer as with the whole profile
    public class ApiPlayerLoadingTests : IDisposable
    {
        private const ulong Player = 10;
        private const ulong Packed = CustomCharacters.IdStart + 1;

        private readonly WaifuCommandHarness _h = new WaifuCommandHarness();
        private readonly FakeExecutor _executor = new FakeExecutor { RunImmediately = true };

        public ApiPlayerLoadingTests()
        {
            _h.Shinden.CustomCharacterInfo = id => Task.FromResult<Sanakan.ShindenCompat.Models.ICharacterInfo>(
                new Sanakan.ShindenCompat.Models.Entities.CharacterInfo { Id = id, FirstName = "Postać", LastName = $"{id % 1000}" });
        }

        public void Dispose() => _h.Dispose();

        private Card NewCard(ulong id, bool active = false, bool ultimate = false) => new Card
        {
            Id = id, Character = id, Name = $"postać {id}", Rarity = ultimate ? Rarity.SSS : Rarity.A, Active = active, FromFigure = ultimate,
            Quality = ultimate ? Quality.Gamma : Quality.Broken, Attack = 50, Defence = 20, Health = 300, MarketValue = 1, IsTradable = true,
            Dere = Dere.Deredere, Tags = new List<Tag>(), CreationDate = _h.Time.Now()
        };

        private static BoosterPack NewPack(string name, int cards) => new BoosterPack
        {
            Name = name, CardCnt = cards, MinRarity = Rarity.E, IsCardFromPackTradable = true, CardSourceFromPack = CardSource.Api,
            Characters = new List<BoosterPackCharacter> { new BoosterPackCharacter { Character = Packed } },
            RarityExcludedFromPack = new List<RarityExcluded>(),
        };

        private async Task SeedAsync(long maxCards = 1000)
        {
            using var db = await _h.CreateAsync();
            var user = new User().Default(Player, _h.Time.Now());
            var deck = user.GameDeck;
            deck.MaxNumberOfCards = maxCards;
            deck.PVPDailyGamesPlayed = 3;
            deck.BoosterPacks.Clear();
            deck.BoosterPacks.Add(NewPack("Pierwszy", 2));
            deck.BoosterPacks.Add(NewPack("Drugi", 1));
            deck.BoosterPacks.Add(NewPack("Pierwszy", 1));
            deck.Items.Add(ItemType.CardFragment.ToItem(10));
            deck.Items.Add(ItemType.AffectionRecoveryBig.ToItem(2));
            deck.Wishes.Add(new WishlistObject { Type = WishlistObjectType.Character, ObjectId = 77, ObjectName = "x", Entry = WishlistEntryType.Persistent });
            deck.Figures.Add(new Figure { Character = 1009, Name = "figurka", IsFocus = true, SkeletonQuality = Quality.Alpha });
            user.TimeStatuses.Add(new TimeStatus { Type = StatusType.Pvp, EndsAt = _h.Time.Now().AddHours(5) });
            user.TimeStatuses.Add(new TimeStatus { Type = StatusType.Card, EndsAt = _h.Time.Now().AddHours(20) });
            user.TimeStatuses.Add(new TimeStatus { Type = StatusType.DPacket, IValue = 1, EndsAt = _h.Time.Now().AddHours(5) });

            var own = new Tag { Name = "konie", GameDeck = deck };
            deck.Tags.Add(own);
            deck.Cards.Add(NewCard(901, active: true));
            deck.Cards.Add(NewCard(902, active: true));
            deck.Cards.Add(NewCard(903, ultimate: true));
            deck.Cards.Add(NewCard(904, ultimate: true));
            deck.Cards.Add(NewCard(905));
            db.Users.Add(user);
            await db.SaveChangesAsync();

            var gallery = await db.Tags.FindAsync(_h.Tags.GetTagId(TagType.Gallery));
            var cards = db.Cards.Where(x => x.GameDeckId == Player).ToList();
            foreach (var card in cards)
                db.Entry(card).Collection(x => x.Tags).Load();
            cards.Single(x => x.Id == 901).Tags.Add(gallery);
            cards.Single(x => x.Id == 902).Tags.Add(gallery);
            cards.Single(x => x.Id == 905).Tags.Add(db.Tags.Single(x => x.Name == "konie"));
            await db.SaveChangesAsync();
        }

        private T AsPlayer<T>(T controller) where T : ControllerBase
        {
            ControllerHarness.Attach(controller);
            controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("DiscordId", Player.ToString()) }, "test"));
            return controller;
        }

        private PlayerController NewPlayerController() => AsPlayer(new PlayerController(_h.Config, _h.Time, _executor, _h.Tags, null, null));

        private WaifuController NewWaifuController() => AsPlayer(new WaifuController(null, NewWaifu(), _executor, _h.Tags, _h.Config, _h.Time,
            _h.Shinden, null, null, null));

        private Sanakan.Services.PocketWaifu.Waifu NewWaifu()
            => new Sanakan.Services.PocketWaifu.Waifu(null, null, null, new ListLogger(), null, null, null, _h.Time, _h.Shinden, _h.Tags, _h.Config);

        private User Full()
        {
            using var db = _h.Open();
            return db.GetCachedFullUserAsync(Player).GetAwaiter().GetResult();
        }

        private static string Json(IActionResult result) => JsonConvert.SerializeObject(Assert.IsType<OkObjectResult>(result).Value);

        private static string Json(object value) => JsonConvert.SerializeObject(value);

        [Fact]
        public async Task ReadEndpoints_AnswerAsWithTheWholeProfile()
        {
            await SeedAsync();
            var full = Full();
            var now = _h.Time.Now();
            var controller = NewPlayerController();

            Assert.Equal(Json(Waifu.OrderPacksForNumbering(full.GameDeck.BoosterPacks).Select((x, i) => UserBoosterPack.From(x, i + 1)).ToList()),
                Json(await controller.GetUserBoosterPacksAsync()));
            Assert.Equal(Json(full.GetAllItems().Select((x, i) => UserItem.From(x, i + 1)).ToList()), Json(await controller.GetUserItemsAsync()));
            Assert.Equal(Json(UserMissions.From(full, now)), Json(await controller.GetMissionsAsync()));
            Assert.Equal(Json(UserLimits.From(full, now, _h.Config.Get().PacksPerDay)), Json(await controller.GetLimitsAsync()));
            Assert.Equal(Json(UserDeck.From(full, now)), Json(await controller.GetDeckAsync()));
            Assert.Equal(Json(UserWishlist.From(full.GameDeck)), Json(await controller.GetWishlistAsync()));
            Assert.Equal(Json(full.GameDeck.Figures.Select(UserFigure.From).ToList()), Json(await controller.GetFiguresAsync()));

            var deck = (UserDeck)((OkObjectResult)await controller.GetDeckAsync()).Value;
            Assert.Equal(new ulong[] { 901, 902 }, deck.Cards.Select(x => x.Id).OrderBy(x => x));
            Assert.Equal(2, ((OkObjectResult)await controller.GetUserItemsAsync()).Value is List<UserItem> items ? items.Count : 0);
        }

        [Fact]
        public async Task Tags_CountTheMarkedCards()
        {
            await SeedAsync();

            var tags = (UserTags)((OkObjectResult)await NewPlayerController().GetTagsAsync()).Value;

            Assert.Equal(1, Assert.Single(tags.Custom).CardCount);
            Assert.Equal(2, tags.Default.Single(x => x.Id == _h.Tags.GetTagId(TagType.Gallery)).CardCount);
            Assert.All(tags.Default.Where(x => x.Id != _h.Tags.GetTagId(TagType.Gallery)), x => Assert.Equal(0, x.CardCount));
        }

        [Fact]
        public async Task ReadEndpoints_OfAMissingPlayer_Return404()
        {
            await SeedAsync();
            var controller = NewPlayerController();
            controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("DiscordId", "12345") }, "test"));

            Assert.Equal(404, (await controller.GetDeckAsync()).StatusOf());
            Assert.Equal(404, (await controller.GetTagsAsync()).StatusOf());
        }

        [Fact]
        public async Task OpeningAPack_TakesItByItsNumber()
        {
            await SeedAsync();

            // numbering groups the packs by name: 1 and 2 are both "Pierwszy", 3 is "Drugi"
            var result = await NewWaifuController().OpenAPackAsync(1, "open-1");

            Assert.Equal(2, result.Value.Count);
            using var db = _h.Open();
            Assert.Equal(7, db.Cards.Count(x => x.GameDeckId == Player));
            Assert.Equal(new[] { "Drugi", "Pierwszy" }, db.BoosterPacks.Where(x => x.GameDeckId == Player).Select(x => x.Name).ToList().OrderBy(x => x));
        }

        // after the first opening the numbers moved, a retry with the same key must not open the next pack
        [Fact]
        public async Task OpeningAPack_RetryWithTheSameKey_ReturnsTheSameCards_WithoutOpeningAnother()
        {
            await SeedAsync();
            var first = await NewWaifuController().OpenAPackAsync(1, "open-1");

            var controller = NewWaifuController();
            var retry = await controller.OpenAPackAsync(1, "open-1");

            Assert.Equal(first.Value.Select(x => x.Id).OrderBy(x => x), retry.Value.Select(x => x.Id).OrderBy(x => x));
            Assert.Equal("true", controller.Response.Headers[Sanakan.Services.Idempotency.ReplayedHeader].ToString());
            using var db = _h.Open();
            Assert.Equal(7, db.Cards.Count(x => x.GameDeckId == Player));
            Assert.Equal(2, db.BoosterPacks.Count(x => x.GameDeckId == Player));
        }

        [Fact]
        public async Task OpeningAPack_WithoutKeyOrWithAKeyOfOtherRequest_IsRejected()
        {
            await SeedAsync();
            Assert.Equal(400, (await NewWaifuController().OpenAPackAsync(1)).StatusOf());

            await NewWaifuController().OpenAPackAsync(1, "open-1");
            Assert.Equal(422, (await NewWaifuController().OpenAPackAsync(2, "open-1")).StatusOf());

            using var db = _h.Open();
            Assert.Equal(2, db.BoosterPacks.Count(x => x.GameDeckId == Player));
        }

        [Fact]
        public async Task OpeningAPack_ChecksTheNumberAndTheSpace()
        {
            await SeedAsync(maxCards: 6);
            var controller = NewWaifuController();

            Assert.Equal(404, (await controller.OpenAPackAsync(4, "open-1")).StatusOf());
            var noSpace = await controller.OpenAPackAsync(1, "open-2");
            Assert.Equal(409, noSpace.StatusOf());
            Assert.Equal(WaifuController.NoSpaceCode, ApiBody.Code(noSpace.Result));
            Assert.Single((await controller.OpenAPackAsync(3, "open-3")).Value);
        }

        [Fact]
        public async Task GivingPacks_ToAMissingPlayer_Returns404()
        {
            await SeedAsync();
            var packs = new List<CardBoosterPack> { new CardBoosterPack { Name = "x", Count = 1, Pool = new BoosterPackPool { Type = CardsPoolType.Random } } };

            Assert.Equal(404, (await NewWaifuController().GiveUserAPacksAsync(12345, packs, "give-1")).StatusOf());
            Assert.Equal(200, (await NewWaifuController().GiveUserAPacksAsync(Player, packs, "give-1")).StatusOf());
        }

        [Fact]
        public async Task GivingPacks_RetryWithTheSameKey_AddsThemOnce()
        {
            await SeedAsync();
            var packs = new List<CardBoosterPack> { new CardBoosterPack { Name = "x", Count = 1, Pool = new BoosterPackPool { Type = CardsPoolType.Random } } };
            await NewWaifuController().GiveUserAPacksAsync(Player, packs, "give-1");

            var controller = NewWaifuController();
            Assert.Equal(200, (await controller.GiveUserAPacksAsync(Player, packs, "give-1")).StatusOf());
            Assert.Equal("true", controller.Response.Headers[Sanakan.Services.Idempotency.ReplayedHeader].ToString());
            Assert.Equal(400, (await NewWaifuController().GiveUserAPacksAsync(Player, packs)).StatusOf());

            using var db = _h.Open();
            Assert.Equal(1, db.BoosterPacks.Count(x => x.GameDeckId == Player && x.Name == "x"));
        }

        private DeckState Deck()
        {
            using var db = _h.Open();
            var deck = db.GameDecks.Single(x => x.Id == Player);
            return new DeckState(deck.CardsInDeck, deck.DeckPower, db.Cards.Where(x => x.GameDeckId == Player && x.Active).Select(x => x.Id).ToList().OrderBy(x => x).ToList());
        }

        private record DeckState(long CardsInDeck, double DeckPower, List<ulong> Active);

        [Fact]
        public async Task SettingCardActive_SetsTheState_AndARetryChangesNothing()
        {
            await SeedAsync();

            var on = await NewWaifuController().SetCardActiveAsync(905, true);
            Assert.Equal("Card activated", ApiBody.Message(on));
            var afterOn = Deck();
            Assert.Equal(new ulong[] { 901, 902, 905 }, afterOn.Active);
            Assert.Equal(3, afterOn.CardsInDeck);

            var again = await NewWaifuController().SetCardActiveAsync(905, true);
            Assert.Equal(200, again.StatusOf());
            Assert.Equal("Card unchanged", ApiBody.Message(again));
            var afterAgain = Deck();
            Assert.Equal(afterOn.Active, afterAgain.Active);
            Assert.Equal(afterOn.CardsInDeck, afterAgain.CardsInDeck);
            Assert.Equal(afterOn.DeckPower, afterAgain.DeckPower);

            Assert.Equal("Card deactivated", ApiBody.Message(await NewWaifuController().SetCardActiveAsync(905, false)));
            Assert.Equal("Card unchanged", ApiBody.Message(await NewWaifuController().SetCardActiveAsync(905, false)));
            var afterOff = Deck();
            Assert.Equal(new ulong[] { 901, 902 }, afterOff.Active);
            Assert.Equal(2, afterOff.CardsInDeck);
            Assert.Equal(404, (await NewWaifuController().SetCardActiveAsync(12345, true)).StatusOf());
        }

        [Fact]
        public async Task SettingCardActive_InCageOrOnExpedition_Returns409WithCode()
        {
            await SeedAsync();
            using (var db = _h.Open())
            {
                db.Cards.Single(x => x.Id == 904).InCage = true;
                db.Cards.Single(x => x.Id == 905).Expedition = CardExpedition.NormalItemWithExp;
                db.SaveChanges();
            }

            var caged = await NewWaifuController().SetCardActiveAsync(904, true);
            var away = await NewWaifuController().SetCardActiveAsync(905, true);

            Assert.Equal(409, caged.StatusOf());
            Assert.Equal(WaifuController.CardInCageCode, ApiBody.Code(caged));
            Assert.Equal(409, away.StatusOf());
            Assert.Equal(WaifuController.CardOnExpeditionCode, ApiBody.Code(away));
            Assert.Equal(new ulong[] { 901, 902 }, Deck().Active);
        }

        [Fact]
        public async Task SettingCardActive_FullQueue_Returns503WithRetryAfter()
        {
            await SeedAsync();
            _executor.Accept = false;
            var controller = NewWaifuController();

            Assert.Equal(503, (await controller.SetCardActiveAsync(905, true)).StatusOf());
            Assert.Equal(Sanakan.Api.ApiResponses.QueueFullRetryAfter.ToString(), controller.Response.Headers["Retry-After"].ToString());
        }

        [Fact]
        public async Task SettingTheSameWaifuAgain_Returns200_WithoutLoweringAffectionTwice()
        {
            await SeedAsync();
            using var client = new Discord.WebSocket.DiscordSocketClient();
            using var sessions = new Sanakan.Services.Session.SessionManager(client, new FakeExecutor(), new ListLogger());
            sessions.Initialize(new EmptyServiceProvider());
            PlayerController Controller() => AsPlayer(new PlayerController(_h.Config, _h.Time, _executor, _h.Tags, null, sessions));

            Assert.Equal("Waifu changed!", ApiBody.Message(await Controller().SetWaifuAsync(905)));
            double Affection()
            {
                using var db = _h.Open();
                return db.Cards.Single(x => x.Id == 905).Affection;
            }
            var before = Affection();

            var again = await Controller().SetWaifuAsync(905);

            Assert.Equal(200, again.StatusOf());
            Assert.Equal("Waifu unchanged!", ApiBody.Message(again));
            Assert.Equal(before, Affection());
        }

        [Fact]
        public async Task CreatingATag_Conflicts_HaveCodes_AndARetryPointsAtTheCreatedTag()
        {
            await SeedAsync();
            var created = await NewPlayerController().CreateTagAsync("nowy");
            var tag = Assert.IsType<UserTag>(Assert.IsType<ObjectResult>(created).Value);

            var retry = await NewPlayerController().CreateTagAsync("NOWY");
            Assert.Equal(409, retry.StatusOf());
            Assert.Equal(PlayerController.TagExistsCode, ApiBody.Code(retry));
            Assert.Equal(tag.Id, (ulong)ApiBody.Detail(retry, "tagId"));

            var similar = await NewPlayerController().CreateTagAsync(_h.Tags.GetTag(TagType.Gallery).Name);
            Assert.Equal(PlayerController.TagTooSimilarCode, ApiBody.Code(similar));

            using (var db = _h.Open())
            {
                db.GameDecks.Single(x => x.Id == Player).MaxNumberOfTags = 2;
                db.SaveChanges();
            }
            Assert.Equal(PlayerController.TagLimitCode, ApiBody.Code(await NewPlayerController().CreateTagAsync("trzeci")));
            // at the limit a retry of a tag that was created still says it exists
            Assert.Equal(PlayerController.TagExistsCode, ApiBody.Code(await NewPlayerController().CreateTagAsync("nowy")));

            var rename = await NewPlayerController().RenameTagAsync(tag.Id, "konie");
            Assert.Equal(PlayerController.TagExistsCode, ApiBody.Code(rename));
            Assert.Equal(200, (await NewPlayerController().RenameTagAsync(tag.Id, "nowy")).StatusOf());
        }
    }
}
