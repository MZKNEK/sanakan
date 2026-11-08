using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Sanakan.Api.Controllers;
using Sanakan.Api.Models;
using Sanakan.Config.Model;
using Sanakan.Database;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services.PocketWaifu;
using Xunit;

namespace Artifacts
{
    // The site profile loads narrow card rows and only the shown cards in full;
    // these tests compare it field by field with the previous full-graph profile.
    public class SiteProfileEquivalenceTests : IDisposable
    {
        private const ulong DiscordId = 97001;
        private const ulong ShindenId = 97777;

        private readonly string _path = Path.Combine(Path.GetTempPath(), $"sanakan-profile-eq-{Guid.NewGuid():N}.db");
        private readonly FakeConfig _config = new FakeConfig();
        // cache entries expire relative to the real clock
        private readonly FixedTime _time = new FixedTime { Value = DateTime.Now };
        private TagHelper _tags;

        public SiteProfileEquivalenceTests()
        {
            _config.Model.DatabaseProvider = DatabaseProvider.Sqlite;
            _config.Model.ConnectionString = $"Data Source={_path}";
            using var db = new DatabaseContext(_config);
            db.EnsureReady();
        }

        public void Dispose()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(_path); } catch (IOException) { }
        }

        private async Task<TagHelper> TagsAsync()
        {
            if (_tags != null) return _tags;
            using var db = new DatabaseContext(_config);
            return _tags = await TagHelper.CreateAsync(db);
        }

        private Waifu NewWaifu() => new Waifu(null, null, null, new ListLogger(), null, null, null, _time, null, null, _config);

        private async Task SeedAsync(int cards, bool blacklisted = false, bool premiumWaifu = false)
        {
            var tags = await TagsAsync();
            using var db = new DatabaseContext(_config);

            var user = new User().Default(DiscordId, _time.Now());
            user.Shinden = ShindenId;
            user.IsBlacklisted = blacklisted;
            user.TcCnt = 321;
            user.Stats.UpgaredCards = 4;
            var deck = user.GameDeck;
            deck.CardsInGallery = 3;
            deck.Waifu = 1003;
            deck.Karma = 2500;
            deck.TagsOrder = TagsOrder.Alphabetically;
            deck.GalleryOrderedIds = "5 3 9";
            deck.PVPSeasonBeginDate = _time.Now();
            deck.SeasonalPVPRank = 77;
            deck.PvPStats = new List<CardPvPStats>
            {
                new CardPvPStats { Type = FightType.NewVersus, Result = FightResult.Win },
                new CardPvPStats { Type = FightType.NewVersus, Result = FightResult.Lose },
                new CardPvPStats { Type = FightType.NewVersus, Result = FightResult.Win },
                new CardPvPStats { Type = FightType.Versus, Result = FightResult.Win },
            };
            db.Users.Add(user);
            db.SaveChanges();

            var ownB = new Tag { Name = "b-tag", GameDeckId = DiscordId };
            var ownA = new Tag { Name = "a-tag", GameDeckId = DiscordId };
            db.Tags.AddRange(ownB, ownA);
            db.SaveChanges();

            var gallery = db.Tags.Single(x => x.Id == tags.GetTagId(TagType.Gallery));
            var favourite = db.Tags.Single(x => x.Id == tags.GetTagId(TagType.Favorite));
            var qualities = new[] { Quality.Alpha, Quality.Beta, Quality.Gamma, Quality.Lambda, Quality.Omega };

            for (int i = 0; i < cards; i++)
            {
                var cardTags = new List<Tag>();
                if (i % 3 == 0) cardTags.Add(gallery);
                if (i % 4 == 1) cardTags.Add(ownA);
                if (i % 5 == 2) cardTags.Add(favourite);
                if (i % 7 == 3) cardTags.Add(ownB);

                db.Cards.Add(new Card
                {
                    GameDeckId = DiscordId,
                    Name = $"Card {i}",
                    Title = $"Title {i % 6}",
                    Image = $"https://img/{i}.jpg",
                    Character = (ulong)(1000 + i % 15),
                    Rarity = (Rarity)(i % 8),
                    Quality = i % 7 == 0 ? qualities[i % 5] : Quality.Broken,
                    FromFigure = i % 7 == 0,
                    Unique = i % 9 == 0,
                    BorderOverflow = i % 3,
                    BorderVariant = i % 2,
                    RestartCnt = i % 5,
                    CardPower = (i % 7) * 10.5,
                    WhoWantsCount = i % 4,
                    AWhoWantsCount = i % 3,
                    CustomImage = i % 4 == 0 ? $"https://custom/{i}.png" : i % 10 == 5 ? "" : null,
                    IsAnimatedImage = i % 8 == 0,
                    CustomBorder = i % 6 == 0 ? $"https://border/{i}.png" : i % 11 == 3 ? "" : null,
                    Expedition = i % 13 == 1 ? CardExpedition.NormalItemWithExp : CardExpedition.None,
                    ExpeditionDate = _time.Now().AddHours(-3),
                    Affection = 10 + i,
                    Health = 50 + i, Attack = 20 + i, Defence = 10 + i,
                    Dere = (Dere)(i % 4),
                    Active = i % 6 == 1,
                    CreationDate = _time.Now().AddDays(-i),
                    Source = CardSource.Activity,
                    Tags = cardTags,
                });
            }
            db.SaveChanges();

            if (premiumWaifu)
            {
                var stored = db.GameDecks.Single(x => x.Id == DiscordId);
                stored.PremiumWaifu = db.Cards.Where(x => x.GameDeckId == DiscordId).Select(x => x.Id).AsEnumerable().OrderBy(x => x).Skip(7).First();
                db.SaveChanges();
            }
        }

        private async Task<ActionResult<UserSiteProfile>> CurrentAsync()
        {
            var controller = ControllerHarness.Attach(new WaifuController(null, NewWaifu(), new FakeExecutor(), await TagsAsync(), _config, _time,
                null, null, new Expedition(_time), null));
            return await controller.GetUserWaifuProfileAsync(ShindenId);
        }

        // the profile as built before narrow loading: full graph with every card and its tags
        private async Task<UserSiteProfile> LegacyAsync()
        {
            var tagsHelper = await TagsAsync();
            var waifu = NewWaifu();
            var expedition = new Expedition(_time);
            using var db = new DatabaseContext(_config);
            ulong id = ShindenId;
            var user = (await db.Users.AsQueryable().AsSplitQuery().Where(x => id != 0 && x.Shinden == id).Include(x => x.GameDeck).ThenInclude(x => x.Tags).Include(x => x.GameDeck)
                .ThenInclude(x => x.PvPStats).Include(x => x.GameDeck).ThenInclude(x => x.Cards).ThenInclude(x => x.Tags).Include(x => x.Stats).AsNoTracking().ToListAsync()).First();

            var cardDetails = waifu.GetCardsDetails(user.GameDeck.Cards);
            var cardCount = cardDetails.CardsByRarityAndQuality;
            cardCount.Add("max", user.GameDeck.MaxNumberOfCards);
            cardCount.Add("total", cardDetails.TotalCardsCount);
            cardCount.Add("ultimate", cardDetails.UltimateCardsCount);

            var wallet = new Dictionary<string, long>
            {
                {"PC", user.GameDeck.PVPCoins},
                {"CT", user.GameDeck.CTCnt},
                {"AC", user.AcCnt},
                {"TC", user.TcCnt},
                {"SC", user.ScCnt},
            };

            var newPvp = user.GameDeck.PvPStats.Where(x => x.Type == FightType.NewVersus).ToList();
            var misc = new Dictionary<string, long>
            {
                {"upgraded",        user.Stats.UpgaredCards},
                {"upgradedsss",     user.Stats.UpgradedToSSS},
                {"sacrafice",       user.Stats.SacraficeCards},
                {"destroyed",       user.Stats.DestroyedCards},
                {"unleashed",       user.Stats.UnleashedCards},
                {"released",        user.Stats.ReleasedCards},
                {"yami",            user.Stats.YamiUpgrades},
                {"raito",           user.Stats.RaitoUpgrades},
                {"yato",            user.Stats.YatoUpgrades},
                {"packs",           user.Stats.OpenedBoosterPacks},
                {"packsactiv",      user.Stats.OpenedBoosterPacksActivity},
                {"premiumwaifu",    user.GameDeck.PremiumWaifu != 0 ? 1 : 0},
                {"lottery",         user.Stats.LotteryTicketsUsed},
                {"reversedkarma",   user.Stats.ReversedKarmaCnt},
                {"pvpplayed",       newPvp.Count},
                {"pvpwon",          newPvp.Count(x => x.Result == FightResult.Win)},
                {"pvpglobal",       user.GameDeck.GlobalPVPRank},
                {"pvpseason",       user.IsPVPSeasonalRankActive(_time.Now()) ? user.GameDeck.SeasonalPVPRank : 0},
                {"kccnt",           cardDetails.TotalKCount},
                {"activekccnt",     cardDetails.TotalActiveKCCount},
                {"gallerylimit",    user.GameDeck.CardsInGallery},
            };

            var galleryOrder = string.IsNullOrEmpty(user.GameDeck.GalleryOrderedIds) ? new List<ulong>()
                : user.GameDeck.GalleryOrderedIds.Split(" ").Select(x => UserSiteProfile.TryParseIds(x)).ToList();

            var galleryTag = tagsHelper.GetTag(TagType.Gallery);
            var tags = user.GameDeck.GetOrderedTags().Select(x => new TagIdPair { Name = x.Name, Id = x.Id }).ToList();

            tags.Add(galleryTag.ToView());
            tags.Add(tagsHelper.GetTag(TagType.Favorite).ToView());
            tags.Add(tagsHelper.GetTag(TagType.Exchange).ToView());
            tags.Add(tagsHelper.GetTag(TagType.Reservation).ToView());
            tags.Add(tagsHelper.GetTag(TagType.TrashBin).ToView());

            var allGalleryCards = user.GameDeck.GetOrderedGalleryCardsUnlimited(galleryTag.Id);
            var username = "";
            return new UserSiteProfile
            {
                DiscordId = user.Id.ToString(),
                TagList = tags,
                Wallet = wallet,
                TotalUltimateCardPower = cardDetails.TotalUltimateCardPower,
                TotalCardPower = cardDetails.TotalNormalCardPower,
                CameraCount = cardDetails.CameraCount,
                ScissorsCount = cardDetails.ScissorsCount,
                ScalpelCount = cardDetails.ScalpelCount,
                UniqueCardsCount = cardDetails.UniqueCardsCount,
                RestartsCount = cardDetails.RestartCount,
                TotalOverflowCount = cardDetails.OverflowCount,
                CardWithMostRestarts = cardDetails.CardWithMostRestarts.ToView(username, 0, _time),
                MostPowerfulCard = cardDetails.MostPowerfulCard.ToView(username, 0, _time),
                Expeditions = cardDetails.CardsOnExpeditions.ToExpeditionView(user, expedition, username),
                CardsCount = cardCount,
                Karma = user.GameDeck.Karma,
                GalleryOrder = galleryOrder,
                MiscStats = misc,
                UserTitle = user.GameDeck.GetUserNameStatus(),
                LastActivity = user.GameDeck.LastSignificantActivity,
                Waifu = user.GameDeck.GetWaifuCard().ToView(username, 0, _time),
                ForegroundColor = user.GameDeck.ForegroundColor,
                ForegroundPosition = user.GameDeck.ForegroundPosition,
                BackgroundPosition = user.GameDeck.BackgroundPosition,
                ExchangeConditions = user.GameDeck.ExchangeConditions,
                BackgroundImageUrl = user.GameDeck.BackgroundImageUrl,
                ForegroundImageUrl = user.GameDeck.ForegroundImageUrl,
                CardsTaggedAsGalleryCount = allGalleryCards.Count,
                Gallery = allGalleryCards.Take(user.GameDeck.CardsInGallery).ToView(username, 0, _time),
                DiagnosticMs = 0
            };
        }

        private async Task AssertSameAsLegacyAsync()
        {
            var expected = await LegacyAsync();
            var actual = (await CurrentAsync()).Value;
            Assert.NotNull(actual);
            actual.DiagnosticMs = 0;

            Assert.Equal(JsonConvert.SerializeObject(expected, Formatting.Indented), JsonConvert.SerializeObject(actual, Formatting.Indented));
        }

        [Fact]
        public async Task RichDeck_MatchesLegacyProfile()
        {
            await SeedAsync(60);

            await AssertSameAsLegacyAsync();
            // second call is served from the cache and must not differ either
            await AssertSameAsLegacyAsync();
        }

        [Fact]
        public async Task PremiumWaifu_MatchesLegacyProfile()
        {
            await SeedAsync(30, premiumWaifu: true);
            await AssertSameAsLegacyAsync();
        }

        [Fact]
        public async Task EmptyDeck_MatchesLegacyProfile()
        {
            await SeedAsync(0);
            await AssertSameAsLegacyAsync();
        }

        [Fact]
        public async Task RichProfile_ShowsRealCardData()
        {
            await SeedAsync(60);
            var actual = (await CurrentAsync()).Value;

            Assert.Equal(3, actual.Gallery.Count);
            Assert.NotNull(actual.Waifu);
            Assert.NotEmpty(actual.Expeditions);
            Assert.DoesNotContain("\"set\"", JsonConvert.SerializeObject(actual));
        }

        [Fact]
        public async Task BlacklistedAndUnknown_KeepStatusCodes()
        {
            await SeedAsync(5, blacklisted: true);
            Assert.Equal(401, (await CurrentAsync()).Result.StatusOf());

            var controller = ControllerHarness.Attach(new WaifuController(null, NewWaifu(), new FakeExecutor(), await TagsAsync(), _config, _time,
                null, null, new Expedition(_time), null));
            Assert.Equal(404, (await controller.GetUserWaifuProfileAsync(12345)).Result.StatusOf());
        }
    }
}
