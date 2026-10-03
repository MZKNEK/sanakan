#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Sanakan.Api.Models;
using Sanakan.Config;
using Discord.WebSocket;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services.Executor;
using Sanakan.Services.PocketWaifu;
using Sanakan.Services.Time;
using Shinden;
using Z.EntityFramework.Plus;

namespace Sanakan.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WaifuController : ControllerBase
    {
        private const uint MaxCardsPerRequest = 4000;
        private const uint MaxActivitiesPerRequest = 4000;

        private readonly Waifu _waifu;
        private readonly TagHelper _tags;
        private readonly IConfig _config;
        private readonly ISystemTime _time;
        private readonly IExecutor _executor;
        private readonly Expedition _expedition;
        private readonly ShindenClient _shClient;
        private readonly IMemoryCache _nameCache;
        private readonly DiscordSocketClient _client;

        public WaifuController(ShindenClient shClient, Waifu waifu, IExecutor executor, TagHelper tags,
            IConfig config, ISystemTime time, IMemoryCache cache, DiscordSocketClient client, Expedition expedition)
        {
            _time = time;
            _tags = tags;
            _waifu = waifu;
            _client = client;
            _config = config;
            _nameCache = cache;
            _executor = executor;
            _shClient = shClient;
            _expedition = expedition;
        }

        /// <summary>
        /// Pobiera użytkowników posiadających karte postaci
        /// </summary>
        /// <param name="id">id postaci z bazy shindena</param>
        /// <returns>lista id</returns>
        /// <response code="404">Users not found</response>
        [HttpGet("users/owning/character/{id}"), Authorize(Policy = "Site")]
        public async Task<ActionResult<IEnumerable<ulong>>> GetUsersOwningCharacterCardAsync(ulong id)
        {
            using (var db = new Database.DatabaseContext(_config))
            {
                var shindenIds = await db.Cards.Include(x => x.GameDeck).ThenInclude(x => x.User)
                    .Where(x => x.Character == id && x.GameDeck.User.Shinden != 0).AsNoTracking().Select(x => x.GameDeck.User.Shinden).Distinct().ToListAsync();

                if (shindenIds.Count > 0)
                    return shindenIds;

                return "Users not found".ToResponse(404);
            }
        }

        /// <summary>
        /// Pobiera liste kart użytkownika
        /// </summary>
        /// <param name="id">id użytkownika shindena</param>
        /// <returns>lista kart</returns>
        /// <response code="404">User not found</response>
        [HttpGet("user/{id}/cards"), Authorize(Policy = "Site")]
        public async Task<ActionResult<IEnumerable<Database.Models.Card>>> GetUserCardsAsync(ulong id)
        {
            using (var db = new Database.DatabaseContext(_config))
            {
                var user = await db.Users.AsQueryable().Where(x => x.Shinden == id)
                    .Include(x => x.GameDeck).ThenInclude(x => x.Cards).ThenInclude(x => x.Tags).AsNoTracking().AsSplitQuery().FirstOrDefaultAsync();

                if (user == null)
                {
                    return "User not found".ToResponse(404);
                }

                return Ok(user.GameDeck.Cards);
            }
        }

        /// <summary>
        /// Pobiera listę aktywności
        /// </summary>
        /// <param name="count">liczba wpisów (0 lub więcej niż limit oznacza limit - 4000)</param>
        /// <param name="users">id użytkowników shinden</param>
        /// <returns>lista aktywności</returns>
        [HttpPost("user/activity/{count}")]
        public async Task<ActionResult<IEnumerable<UserActivity>>> GetUsersActivitiesAsync(uint count, [FromBody]List<ulong> users)
        {
            if (count == 0 || count > MaxActivitiesPerRequest)
                count = MaxActivitiesPerRequest;

            using (var db = new Database.DatabaseContext(_config))
            {
                var query = db.UserActivities.AsQueryable().AsSplitQuery().AsNoTracking();
                if (!users.IsNullOrEmpty())
                {
                    query = query.Where(x => users.Any(c => c == x.ShindenId));
                }
                return await query.OrderByDescending(x => x.Id).Take((int)count).ToListAsync();
            }
        }

        /// <summary>
        /// Pobiera listę aktywności od konkretnego id (maksymalnie 4000 najnowszych)
        /// </summary>
        /// <param name="lastId">id aktywności od której zacząć nową liste</param>
        /// <returns>lista aktywności</returns>
        [HttpGet("user/activity/{lastId}")]
        public async Task<ActionResult<IEnumerable<UserActivity>>> GetUsersActivitiesFromIdAsync(ulong lastId)
        {
            using (var db = new Database.DatabaseContext(_config))
            {
                return await db.UserActivities.AsQueryable().Where(x => x.Id > lastId).AsNoTracking().OrderByDescending(x => x.Id).Take((int)MaxActivitiesPerRequest).ToListAsync();
            }
        }

        /// <summary>
        /// Pobiera x kart z przefiltrowanej listy wszystkich kart
        /// </summary>
        /// <param name="offset">przesunięcie</param>
        /// <param name="count">liczba kart</param>
        /// <param name="filter">filtry listy</param>
        /// <returns>lista kart</returns>
        [HttpPost("total/cards/{offset}/{count}")]
        public async Task<ActionResult<FilteredCards>> GetCardsWithOffsetAndFilterAsync(uint offset, uint count, [FromBody]CardsQueryFilter filter)
        {
            using (var db = new Database.DatabaseContext(_config))
            {
                var query = db.Cards.AsQueryable().AsSplitQuery().Include(x => x.GameDeck).ThenInclude(x => x.User).Include(x => x.Tags).AsNoTrackingWithIdentityResolution();
                if (!string.IsNullOrEmpty(filter.SearchText))
                {
                    query = query.Where(x => x.Name.Contains(filter.SearchText) || x.Title.Contains(filter.SearchText));
                }

                query = CardsQueryFilter.Use(filter.OrderBy, query);
                query = FilterCardsByIds(query, filter);
                query = FilterCardsByTags(query, filter);
                var cards = await query.Skip((int)offset).Take((int)Math.Min(count, MaxCardsPerRequest)).ToListAsync();

                return new FilteredCards{TotalCards = await query.CountAsync(), Cards = await ToViewWithUsernamesAsync(cards)};
            }
        }

        /// <summary>
        /// Pobiera x kart z przefiltowanej listy kart ultimate
        /// </summary>
        /// <param name="offset">przesunięcie</param>
        /// <param name="count">liczba kart</param>
        /// <param name="filter">filtry listy</param>
        /// <returns>lista kart</returns>
        [HttpPost("ultimate/cards/{offset}/{count}")]
        public async Task<ActionResult<FilteredCards>> GetUltimateCardsWithOffsetAndFilterAsync(uint offset, uint count, [FromBody]CardsQueryFilter filter)
        {
            using (var db = new Database.DatabaseContext(_config))
            {
                var query = db.Cards.AsQueryable().AsSplitQuery().Where(x => x.FromFigure).Include(x => x.GameDeck).ThenInclude(x => x.User).Include(x => x.Tags).AsNoTrackingWithIdentityResolution();
                if (!string.IsNullOrEmpty(filter.SearchText))
                {
                    query = query.Where(x => x.Name.Contains(filter.SearchText) || x.Title.Contains(filter.SearchText));
                }

                query = CardsQueryFilter.Use(filter.OrderBy, query);
                query = FilterCardsByIds(query, filter);

                var expireTime = new MemoryCacheEntryOptions().SetAbsoluteExpiration(_time.Now().AddHours(4));
                var cached = await FilterCardsByTags(query, filter).FromCacheAsync(expireTime, $"ultimate-cards");
                var cards = cached.ToList();
                var page = cards.Skip((int)offset).Take((int)Math.Min(count, MaxCardsPerRequest)).ToList();

                return new FilteredCards{TotalCards = cards.Count, Cards = await ToViewWithUsernamesAsync(page)};
            }
        }

        /// <summary>
        /// Pobiera x kart z przefiltowanej listy unikatowych kart
        /// </summary>
        /// <param name="offset">przesunięcie</param>
        /// <param name="count">liczba kart</param>
        /// <param name="filter">filtry listy</param>
        /// <returns>lista kart</returns>
        [HttpPost("unique/cards/{offset}/{count}")]
        public async Task<ActionResult<FilteredCards>> GetUniqueCardsWithOffsetAndFilterAsync(uint offset, uint count, [FromBody]CardsQueryFilter filter)
        {
            using (var db = new Database.DatabaseContext(_config))
            {
                var query = db.Cards.AsQueryable().AsSplitQuery().Where(x => x.Unique).Include(x => x.GameDeck).ThenInclude(x => x.User).Include(x => x.Tags).AsNoTrackingWithIdentityResolution();
                if (!string.IsNullOrEmpty(filter.SearchText))
                {
                    query = query.Where(x => x.Name.Contains(filter.SearchText) || x.Title.Contains(filter.SearchText));
                }

                query = CardsQueryFilter.Use(filter.OrderBy, query);
                query = FilterCardsByIds(query, filter);

                var expireTime = new MemoryCacheEntryOptions().SetAbsoluteExpiration(_time.Now().AddHours(8));
                var cached = await FilterCardsByTags(query, filter).FromCacheAsync(expireTime, $"unique-cards");
                var cards = cached.ToList();
                var page = cards.Skip((int)offset).Take((int)Math.Min(count, MaxCardsPerRequest)).ToList();

                return new FilteredCards{TotalCards = cards.Count, Cards = await ToViewWithUsernamesAsync(page)};
            }
        }

        /// <summary>
        /// Pobiera x kart z przefiltrowanej listy użytkownika
        /// </summary>
        /// <param name="id">id użytkownika shindena</param>
        /// <param name="offset">przesunięcie</param>
        /// <param name="count">liczba kart</param>
        /// <param name="filter">filtry listy</param>
        /// <returns>lista kart</returns>
        /// <response code="404">User not found</response>
        [HttpPost("user/{id}/cards/{offset}/{count}")]
        public async Task<ActionResult<FilteredCards>> GetUsersCardsByShindenIdWithOffsetAndFilterAsync(ulong id, uint offset, uint count, [FromBody]CardsQueryFilter filter)
        {
            using (var db = new Database.DatabaseContext(_config))
            {
                var user = await db.Users.AsQueryable().Where(x => x.Shinden == id).Include(x => x.GameDeck).AsNoTracking().AsSplitQuery().FirstOrDefaultAsync();

                if (user == null)
                {
                    return "User not found".ToResponse(404);
                }

                if (user.IsBlacklisted)
                {
                    return "User on blacklist".ToResponse(401);
                }

                var query = db.Cards.AsQueryable().AsSplitQuery().Where(x => x.GameDeckId == user.GameDeck.Id).Include(x => x.Tags).AsNoTrackingWithIdentityResolution();
                if (!string.IsNullOrEmpty(filter.SearchText))
                {
                    query = query.Where(x => x.Name.Contains(filter.SearchText) || x.Title.Contains(filter.SearchText));
                }

                query = CardsQueryFilter.Use(filter.OrderBy, query);
                query = FilterCardsByIds(query, filter);
                query = FilterCardsByTags(query, filter);

                var username = await GetUsernameAsync(user.Shinden);
                var cards = await query.Skip((int)offset).Take((int)count).ToListAsync();

                return new FilteredCards{TotalCards = query.Count(), Cards = cards.ToView(username, id, _time)};
            }
        }

        /// <summary>
        /// Pobiera x kart z listy użytkownika
        /// </summary>
        /// <param name="id">id użytkownika shindena</param>
        /// <param name="offset">przesunięcie</param>
        /// <param name="count">liczba kart</param>
        /// <returns>lista kart</returns>
        /// <response code="404">User not found</response>
        [HttpGet("user/{id}/cards/{offset}/{count}")]
        public async Task<ActionResult<IEnumerable<CardFinalView>>> GetUsersCardsByShindenIdWithOffsetAsync(ulong id, uint offset, uint count)
        {
            using (var db = new Database.DatabaseContext(_config))
            {
                var user = await db.Users.AsQueryable().AsSplitQuery().Where(x => x.Shinden == id).Include(x => x.GameDeck).AsNoTracking().FirstOrDefaultAsync();

                if (user == null)
                {
                    return "User not found".ToResponse(404);
                }

                if (user.IsBlacklisted)
                {
                    return "User on blacklist".ToResponse(401);
                }

                var cards = await db.Cards.AsQueryable().AsSplitQuery().Where(x => x.GameDeckId == user.GameDeck.Id).Include(x => x.Tags).Skip((int)offset).Take((int)count).AsNoTrackingWithIdentityResolution().ToListAsync();
                var username = await GetUsernameAsync(user.Shinden);
                return cards.ToView(username, 0, _time);
            }
        }

        /// <summary>
        /// Pobiera kartę
        /// </summary>
        /// <param name="id">id karty</param>
        /// <returns>karta</returns>
        /// <response code="404">Card not found</response>
        [HttpGet("card/{id}/view")]
        public async Task<ActionResult<CardFinalView>> GetCardViewAsync(ulong id)
        {
            using (var db = new Database.DatabaseContext(_config))
            {
                var card = await db.Cards.AsQueryable().Where(x => x.Id == id)
                    .Include(x => x.Tags).Include(x => x.GameDeck).ThenInclude(x => x.User)
                    .AsNoTracking().FirstOrDefaultAsync();

                if (card == null)
                {
                    return "Card not found".ToResponse(404);
                }

                var username = await GetUsernameAsync(card.GameDeck.User.Shinden);
                return card.ToView(username, 0, _time);
            }
        }

        /// <summary>
        /// Pobiera surową listę życzeń użtykownika
        /// </summary>
        /// <param name="id">id użytkownika shindena</param>
        /// <returns>lista życzeń</returns>
        /// <response code="404">User not found</response>
        /// <response code="401">User wishlist is private</response>
        [HttpGet("user/shinden/{id}/wishlist/raw")]
        public async Task<ActionResult<IEnumerable<WishlistObject>>> GetUsersRawWishlistByShindenIdAsync(ulong id)
        {
            using (var db = new Database.DatabaseContext(_config))
            {
                var user = await db.Users.AsQueryable().AsSplitQuery().Where(x => x.Shinden == id).Include(x => x.GameDeck).ThenInclude(x => x.Wishes).AsNoTracking().FirstOrDefaultAsync();

                if (user == null)
                {
                    return "User not found".ToResponse(404);
                }

                if (user.IsBlacklisted)
                {
                    return "User on blacklist".ToResponse(401);
                }

                if (user.GameDeck.WishlistIsPrivate)
                {
                    return "User wishlist is private".ToResponse(401);
                }

                return Ok(user.GameDeck.Wishes);
            }
        }

        /// <summary>
        /// Pobiera topke życzeń użytkowników
        /// </summary>
        /// <param name="count">jak dużo wpisów</param>
        /// <returns>topka życzeń</returns>
        /// <response code="404">Not found</response>
        [HttpGet("top/characters/{count}")]
        public async Task<ActionResult<IEnumerable<Database.Models.Analytics.WishlistCount>>> GetTopCharactersAsync(int count)
        {
            using (var db = new Database.DatabaseContext(_config))
            {
                var top = await db.WishlistCountData.AsQueryable().OrderByDescending(x => x.Count).ToListAsync();
                if (top == null)
                {
                    return "Not found".ToResponse(404);
                }
                return Ok(top.Take(count));
            }
        }

        /// <summary>
        /// Pobiera profil użytkownika
        /// </summary>
        /// <param name="id">id użytkownika shindena</param>
        /// <returns>profil</returns>
        /// <response code="404">User not found</response>
        [HttpGet("user/{id}/profile")]
        public async Task<ActionResult<UserSiteProfile>> GetUserWaifuProfileAsync(ulong id)
        {
            using (var db = new Database.DatabaseContext(_config))
            {
                var countingTimer = System.Diagnostics.Stopwatch.StartNew();
                var expireTime = new MemoryCacheEntryOptions().SetAbsoluteExpiration(_time.Now().AddMinutes(15));
                var cached = await db.Users.AsQueryable().AsSplitQuery().Where(x => x.Shinden == id).Include(x => x.GameDeck).ThenInclude(x => x.Tags).Include(x => x.GameDeck)
                    .ThenInclude(x => x.PvPStats).Include(x => x.GameDeck).ThenInclude(x => x.Cards).ThenInclude(x => x.Tags).Include(x => x.Stats).AsNoTracking()
                    .FromCacheAsync(expireTime, $"user-profile-{id}");
                var user = cached.FirstOrDefault();

                if (user == null)
                {
                    return "User not found".ToResponse(404);
                }

                if (user.IsBlacklisted)
                {
                    return "User on blacklist".ToResponse(401);
                }

                var cardDetails = _waifu.GetCardsDetails(user.GameDeck.Cards);
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

                var galleryTag = _tags.GetTag(TagType.Gallery);
                var tags = user.GameDeck.GetOrderedTags().Select(x => new TagIdPair { Name = x.Name, Id = x.Id }).ToList();

                tags.Add(galleryTag.ToView());
                tags.Add(_tags.GetTag(TagType.Favorite).ToView());
                tags.Add(_tags.GetTag(TagType.Exchange).ToView());
                tags.Add(_tags.GetTag(TagType.Reservation).ToView());
                tags.Add(_tags.GetTag(TagType.TrashBin).ToView());

                var allGalleryCards = user.GameDeck.GetOrderedGalleryCardsUnlimited(galleryTag.Id);
                var username = await GetUsernameAsync(user.Shinden);
                var profile = new UserSiteProfile
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
                    Expeditions = cardDetails.CardsOnExpeditions.ToExpeditionView(user, _expedition, username),
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
                    DiagnosticMs = countingTimer.ElapsedMilliseconds
                };

                return profile;
            }
        }

        /// <summary>
        /// Zastępuje id postaci w kartach
        /// </summary>
        /// <param name="oldId">id postaci z bazy shindena, która została usunięta</param>
        /// <param name="newId">id nowej postaci z bazy shindena</param>
        /// <response code="500">New character ID is invalid!</response>
        [HttpPost("character/repair/{oldId}/{newId}"), Authorize(Policy = "Site")]
        public async Task<IActionResult> RepairCardsAsync(ulong oldId, ulong newId)
        {
            var response = await _shClient.GetCharacterInfoAsync(newId);
            if (!response.IsSuccessStatusCode())
            {
                return "New character ID is invalid!".ToResponse(500);
            }

            var exe = new Executable($"api-repair oc{oldId} c{newId}", new Func<Task>(async () =>
            {
                using (var db = new Database.DatabaseContext(_config))
                {
                    var userRelease = new List<string>() { "users" };
                    var cards = db.Cards.AsQueryable().AsSplitQuery().Where(x => x.Character == oldId);

                    foreach (var card in cards)
                    {
                        card.Character = newId;
                        userRelease.Add($"user-{card.GameDeckId}");
                    }

                    await db.SaveChangesAsync();

                    QueryCacheManager.ExpireTag(userRelease.ToArray());
                }
            }), 0, Priority.High);

            if (!await _executor.TryAdd(exe, TimeSpan.FromSeconds(1)))
            {
                return "Command queue is full".ToResponse(503);
            }

            return "Success".ToResponse(200);
        }

        /// <summary>
        /// Podmienia dane na karcie danej postaci
        /// </summary>
        /// <param name="id">id postaci z bazy shindena</param>
        /// <param name="newData">nowe dane karty</param>
        [HttpPost("cards/character/{id}/update"), Authorize(Policy = "Site")]
        public async Task<IActionResult> UpdateCardInfoAsync(ulong id, [FromBody]Models.CharacterCardInfoUpdate newData)
        {
            var exe = new Executable($"update cards-{id} img", new Func<Task>(async () =>
            {
                using (var db = new Database.DatabaseContext(_config))
                {
                    var userRelease = new List<string>() { "users" };
                    var cards = db.Cards.AsQueryable().AsSplitQuery().Where(x => x.Character == id);

                    foreach (var card in cards)
                    {
                        if (newData?.ImageUrl != null)
                            card.Image = newData.ImageUrl;

                        if (newData?.CharacterName != null)
                            card.Name = newData.CharacterName;

                        try
                        {
                            _waifu.DeleteCardImageIfExist(card);
                        }
                        catch (Exception) { }

                        userRelease.Add($"user-{card.GameDeckId}");
                    }

                    await db.SaveChangesAsync();

                    QueryCacheManager.ExpireTag(userRelease.ToArray());
                }
            }), 0, Priority.High);

            if (!await _executor.TryAdd(exe, TimeSpan.FromSeconds(1)))
            {
                return "Command queue is full".ToResponse(503);
            }

            return "Started!".ToResponse(200);
        }

        /// <summary>
        /// Generuje na nowo karty danej postaci
        /// </summary>
        /// <param name="id">id postaci z bazy shindena</param>
        /// <response code="404">Character not found</response>
        /// <response code="405">Image in character date not found</response>
        [HttpPost("users/make/character/{id}"), Authorize(Policy = "Site")]
        public async Task<IActionResult> GenerateCharacterCardAsync(ulong id)
        {
            var response = await _shClient.GetCharacterInfoAsync(id);
            if (!response.IsSuccessStatusCode())
            {
                return "Character not found!".ToResponse(404);
            }

            if (!response.Body.HasImage)
            {
                return "There is no character image!".ToResponse(405);
            }

            var exe = new Executable($"update cards-{id}", new Func<Task>(async () =>
                {
                    using (var db = new Database.DatabaseContext(_config))
                    {
                        var userRelease = new List<string>() { "users" };
                        var cards = db.Cards.AsQueryable().AsSplitQuery().Where(x => x.Character == id);

                        foreach (var card in cards)
                        {
                            card.Image = response.Body.PictureUrl;

                            try
                            {
                                _waifu.DeleteCardImageIfExist(card);
                            }
                            catch (Exception) { }

                            userRelease.Add($"user-{card.GameDeckId}");
                        }

                        await db.SaveChangesAsync();

                        QueryCacheManager.ExpireTag(userRelease.ToArray());
                    }
                }));

                if (!await _executor.TryAdd(exe, TimeSpan.FromSeconds(1)))
                {
                    return "Command queue is full".ToResponse(503);
                }

                return "Started!".ToResponse(200);
        }

        /// <summary>
        /// Pobiera listę życzeń użytkownika
        /// </summary>
        /// <param name="id">id użytkownika discorda</param>
        /// <response code="404">User not found</response>
        [HttpGet("user/discord/{id}/wishlist"), Authorize(Policy = "Site")]
        public async Task<ActionResult<IEnumerable<Database.Models.Card>>> GetUserWishlistAsync(ulong id)
        {
            using (var db = new Database.DatabaseContext(_config))
            {
                var user = await db.GetCachedFullUserAsync(id);
                return await GetCardsFormWishlistAsync(user, db);
            }
        }

        /// <summary>
        /// Pobiera listę życzeń użytkownika
        /// </summary>
        /// <param name="id">id użytkownika shindena</param>
        /// <response code="404">User not found</response>
        [HttpGet("user/shinden/{id}/wishlist"), Authorize(Policy = "Site")]
        public async Task<ActionResult<IEnumerable<Database.Models.Card>>> GetShindenUserWishlistAsync(ulong id)
        {
            using (var db = new Database.DatabaseContext(_config))
            {
                var user = await db.GetCachedFullUserByShindenIdAsync(id);
                return await GetCardsFormWishlistAsync(user, db);
            }
        }

        /// <summary>
        /// Pobiera liste kart z danym tagiem
        /// </summary>
        /// <param name="tag">tag na karcie</param>
        [HttpGet("cards/tag/{tag}"), Authorize(Policy = "Site")]
        public async Task<ActionResult<IEnumerable<Database.Models.Card>>> GetCardsWithTagAsync(string tag)
        {
            using (var db = new Database.DatabaseContext(_config))
            {
                return Ok(await db.Cards.Include(x => x.Tags).Where(x => x.Tags.Any(c => c.Name.Equals(tag))).AsNoTracking().ToListAsync());
            }
        }

        /// <summary>
        /// Wymusza na bocie wygenerowanie obrazka jeśli nie istnieje
        /// </summary>
        /// <param name="id">id karty (wid)</param>
        /// <response code="403">Card already exist</response>
        /// <response code="404">Card not found</response>
        /// <response code="500">Card not generated</response>
        [HttpGet("card/{id}")]
        public async Task<IActionResult> GetCardAsync(ulong id)
        {
            bool miniature = System.IO.File.Exists($"{Services.Dir.CardsMiniatures}/{id}.webp") || System.IO.File.Exists($"{Services.Dir.CardsMiniatures}/{id}.gif");
            bool normal = System.IO.File.Exists($"{Services.Dir.Cards}/{id}.webp") || System.IO.File.Exists($"{Services.Dir.Cards}/{id}.gif");
            bool profile = System.IO.File.Exists($"{Services.Dir.CardsInProfiles}/{id}.webp") || System.IO.File.Exists($"{Services.Dir.CardsInProfiles}/{id}.gif");

            if (miniature && normal && profile)
            {
                return "Card already exist!".ToResponse(403);
            }

            using (var db = new Database.DatabaseContext(_config))
            {
                var card = await db.Cards.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
                if (card == null)
                {
                    return "Card not found!".ToResponse(404);
                }

                _waifu.DeleteCardImageIfExist(card);
                var cardImage = await _waifu.GenerateAndSaveCardAsync(card, CardImageType.Normal, true);
                if (!System.IO.File.Exists(cardImage))
                {
                    return "Card not generated!".ToResponse(500);
                }

                return CardImageFile(cardImage);
            }
        }

        /// <summary>
        /// Daje użytkownikowi pakiety kart
        /// </summary>
        /// <param name="id">id użytkownika discorda</param>
        /// <param name="boosterPacks">model pakietu</param>
        /// <returns>użytkownik bota</returns>
        /// <response code="404">User not found</response>
        /// <response code="500">Model is Invalid</response>
        [HttpPost("discord/{id}/boosterpack"), Authorize(Policy = "Site")]
        public async Task<IActionResult> GiveUserAPacksAsync(ulong id, [FromBody]List<Models.CardBoosterPack> boosterPacks)
        {
            var invalid = TryGetBoosterPacks(boosterPacks, out var packs);
            if (invalid != null) return invalid;

            using (var db = new Database.DatabaseContext(_config))
            {
                var user = await db.GetCachedFullUserAsync(id);
                if (user == null)
                {
                    return "User not found!".ToResponse(404);
                }

                var exe = new Executable($"api-packet u{id}", new Func<Task>(async () =>
                {
                    using (var dbs = new Database.DatabaseContext(_config))
                    {
                        var botUser = await dbs.GetUserOrCreateAsync(id);

                        foreach (var pack in packs)
                            botUser.GameDeck.BoosterPacks.Add(pack);

                        await dbs.SaveChangesAsync();

                        QueryCacheManager.ExpireTag(new string[] { $"user-{botUser.Id}", "users" });
                    }
                }), id);

                if (!await _executor.TryAdd(exe, TimeSpan.FromSeconds(1)))
                {
                    return "Command queue is full".ToResponse(503);
                }

                return "Boosterpack added!".ToResponse(200);
            }
        }

        /// <summary>
        /// Daje użytkownikowi pakiety kart
        /// </summary>
        /// <param name="id">id użytkownika shindena</param>
        /// <param name="boosterPacks">model pakietu</param>
        /// <returns>użytkownik bota</returns>
        /// <response code="404">User not found</response>
        /// <response code="500">Model is Invalid</response>
        [HttpPost("shinden/{id}/boosterpack"), Authorize(Policy = "Site")]
        public async Task<ActionResult<UserWithToken>> GiveShindenUserAPacksAsync(ulong id, [FromBody]List<Models.CardBoosterPack> boosterPacks)
        {
            var invalid = TryGetBoosterPacks(boosterPacks, out var packs);
            if (invalid != null) return invalid;

            using (var db = new Database.DatabaseContext(_config))
            {
                var user = await db.GetCachedFullUserByShindenIdAsync(id);
                if (user == null)
                {
                    return "User not found!".ToResponse(404);
                }

                var discordId = user.Id;
                var exe = new Executable($"api-packet u{discordId}", new Func<Task>(async () =>
                {
                    using (var dbs = new Database.DatabaseContext(_config))
                    {
                        var botUser = await dbs.GetUserOrCreateAsync(discordId);

                        foreach (var pack in packs)
                            botUser.GameDeck.BoosterPacks.Add(pack);

                        await dbs.SaveChangesAsync();

                        QueryCacheManager.ExpireTag(new string[] { $"user-{botUser.Id}", "users" });
                    }
                }), user.Id);

                if (!await _executor.TryAdd(exe, TimeSpan.FromSeconds(1)))
                {
                    return "Command queue is full".ToResponse(503);
                }

                TokenData tokenData = null;
                var currUser = ControllerContext.HttpContext.User;
                if (currUser.HasClaim(x => x.Type == ClaimTypes.Webpage))
                {
                    tokenData = UserTokenBuilder.BuildUserToken(_config, user, _time);
                }

                return new UserWithToken()
                {
                    Expire = tokenData?.Expire,
                    Token = tokenData?.Token,
                    User = user,
                };
            }
        }

        /// <summary>
        /// Otwiera pakiety i dodaje użytkownikowi karty wylosowane z nich
        /// </summary>
        /// <param name="id">id użytkownika shindena</param>
        /// <param name="boosterPacks">model pakietu</param>
        /// <returns>karty</returns>
        /// <response code="404">User not found</response>
        /// <response code="406">User has no space</response>
        /// <response code="500">Model/Data is Invalid</response>
        /// <response code="503">Command queue is full</response>
        [HttpPost("shinden/{id}/boosterpack/open"), Authorize(Policy = "Site")]
        public async Task<ActionResult<List<Card>>> GiveShindenUserAPacksAndOpenAsync(ulong id, [FromBody]List<Models.CardBoosterPack> boosterPacks)
        {
            var invalid = TryGetBoosterPacks(boosterPacks, out var packs);
            if (invalid != null) return invalid;

            ulong discordId = 0;
            CharacterPoolType poolType = CharacterPoolType.Anime;
            using (var db = new Database.DatabaseContext(_config))
            {
                var bUser = await db.Users.AsQueryable().Where(x => x.Shinden == id).Include(x => x.GameDeck).ThenInclude(x => x.Cards).AsNoTracking().AsSplitQuery().FirstOrDefaultAsync();
                if (bUser == null)
                {
                    return "User not found".ToResponse(404);
                }
                if (bUser.GameDeck.Cards.Count + packs.Sum(x => x.CardCnt) > bUser.GameDeck.MaxNumberOfCards)
                {
                    return "User has no space left in deck".ToResponse(406);
                }
                discordId = bUser.Id;
                poolType = bUser.PoolType;
            }

            var cards = new List<Card>();
            foreach (var pack in packs)
            {
                cards.AddRange(await _waifu.OpenBoosterPackAsync(null, pack, poolType));
            }

            var exe = new Executable($"api-packet-open u{discordId}", new Func<Task>(async () =>
            {
                using (var db = new Database.DatabaseContext(_config))
                {
                    var botUser = await db.GetUserOrCreateAsync(discordId);

                    botUser.Stats.OpenedBoosterPacks += packs.Count;

                    await UpdateWishlistCountAsync(db, cards, botUser);

                    await db.SaveChangesAsync();

                    QueryCacheManager.ExpireTag(new string[] { $"user-{botUser.Id}", "users" });
                }
            }), discordId);

            if (!await _executor.TryAdd(exe, TimeSpan.FromSeconds(1)))
            {
                return "Command queue is full".ToResponse(503);
            }

            await exe.WaitAsync();

            return cards;
        }

        /// <summary>
        /// Otwiera pakiet użytkownika (wymagany Bearer od użytkownika)
        /// </summary>
        /// <param name="packNumber">numer pakietu</param>
        /// <response code="403">The appropriate claim was not found</response>
        /// <response code="404">User not found</response>
        /// <response code="406">User has no space</response>
        /// <response code="409">Boosterpack already opened</response>
        /// <response code="503">Command queue is full / Can't connect to shinden</response>
        [HttpPost("boosterpack/open/{packNumber}"), Authorize(Policy = "Player")]
        public async Task<ActionResult<List<Card>>> OpenAPackAsync(int packNumber)
        {
            var currUser = ControllerContext.HttpContext.User;
            if (currUser.HasClaim(x => x.Type == "DiscordId"))
            {
                if (ulong.TryParse(currUser.Claims.First(x => x.Type == "DiscordId").Value, out var discordId))
                {
                    ulong packId = 0;
                    var opened = new List<Card>();
                    using (var db = new Database.DatabaseContext(_config))
                    {
                        var botUserCh = await db.GetCachedFullUserAsync(discordId);
                        if (botUserCh == null)
                        {
                            return "User not found!".ToResponse(404);
                        }

                        var packs = botUserCh.GameDeck.BoosterPacks.ToList();
                        if (packs.Count < packNumber || packNumber <= 0)
                        {
                            return "Boosterpack not found!".ToResponse(404);
                        }

                        var packCh = packs[packNumber - 1];
                        if (botUserCh.GameDeck.Cards.Count + packCh.CardCnt > botUserCh.GameDeck.MaxNumberOfCards)
                        {
                            return "User has no space left in deck!".ToResponse(406);
                        }

                        opened = await _waifu.OpenBoosterPackAsync(null, packCh, botUserCh.PoolType);
                        if (opened.Count < packCh.CardCnt)
                        {
                            return "Can't connect to shinden!".ToResponse(503);
                        }
                        packId = packCh.Id;
                    }

                    ObjectResult error = null;
                    var cards = new List<Card>();
                    var exe = new Executable($"api-packet-open u{discordId}", new Func<Task>(async () =>
                    {
                        using (var db = new Database.DatabaseContext(_config))
                        {
                            var botUser = await db.GetUserOrCreateAsync(discordId);
                            var pack = botUser.GameDeck.BoosterPacks.FirstOrDefault(x => x.Id == packId);
                            if (pack == null)
                            {
                                error = "Boosterpack already opened!".ToResponse(409);
                                return;
                            }

                            if (botUser.GameDeck.Cards.Count + opened.Count > botUser.GameDeck.MaxNumberOfCards)
                            {
                                error = "User has no space left in deck!".ToResponse(406);
                                return;
                            }

                            var mission = botUser.TimeStatuses.FirstOrDefault(x => x.Type == StatusType.DPacket);
                            if (mission == null)
                            {
                                mission = StatusType.DPacket.NewTimeStatus();
                                botUser.TimeStatuses.Add(mission);
                            }

                            if (pack.CardSourceFromPack != CardSource.Api)
                                mission.Count(_time.Now());

                            botUser.MarkActivity(_time.Now());

                            if (pack.CardSourceFromPack == CardSource.Activity || pack.CardSourceFromPack == CardSource.Migration)
                            {
                                botUser.Stats.OpenedBoosterPacksActivity += 1;
                            }
                            else
                            {
                                botUser.Stats.OpenedBoosterPacks += 1;
                            }

                            botUser.GameDeck.BoosterPacks.Remove(pack);

                            await UpdateWishlistCountAsync(db, opened, botUser);

                            await db.SaveChangesAsync();

                            QueryCacheManager.ExpireTag(new string[] { $"user-{botUser.Id}", "users" });
                            cards = opened;
                        }
                    }), discordId);

                    if (!await _executor.TryAdd(exe, TimeSpan.FromSeconds(1)))
                    {
                        return "Command queue is full".ToResponse(503);
                    }

                    await exe.WaitAsync();

                    if (error != null)
                    {
                        return error;
                    }

                    return cards;
                }
            }
            return "The appropriate claim was not found".ToResponse(403);
        }

        /// <summary>
        /// Aktywuje lub dezaktywuje kartę (wymagany Bearer od użytkownika)
        /// </summary>
        /// <param name="wid">id karty</param>
        /// <response code="403">The appropriate claim was not found / Card is in cage / Card is on expedition</response>
        /// <response code="404">Card not found</response>
        /// <response code="503">Command queue is full</response>
        [HttpPut("deck/toggle/card/{wid}"), Authorize(Policy = "Player")]
        public async Task<IActionResult> ToggleCardStatusAsync(ulong wid)
        {
            var currUser = ControllerContext.HttpContext.User;
            if (currUser.HasClaim(x => x.Type == "DiscordId"))
            {
                if (ulong.TryParse(currUser.Claims.First(x => x.Type == "DiscordId").Value, out var discordId))
                {
                    IActionResult result = null;
                    var exe = new Executable($"api-deck u{discordId}", new Func<Task>(async () =>
                    {
                        using (var db = new Database.DatabaseContext(_config))
                        {
                            var thisCard = await db.Cards.AsQueryable().FirstOrDefaultAsync(x => x.Id == wid && x.GameDeckId == discordId);
                            if (thisCard == null)
                            {
                                result = "Card not found!".ToResponse(404);
                                return;
                            }

                            if (thisCard.InCage)
                            {
                                result = "Card is in cage!".ToResponse(403);
                                return;
                            }

                            if (thisCard.Expedition != CardExpedition.None)
                            {
                                result = "Card is on expedition!".ToResponse(403);
                                return;
                            }

                            var botUser = await db.GetUserOrCreateSimpleAsync(discordId);
                            var active = await db.Cards.AsQueryable().AsNoTracking().Where(x => x.Active && x.GameDeckId == discordId && x.Id != wid).ToListAsync();

                            thisCard.Active = !thisCard.Active;
                            if (thisCard.Active) active.Add(thisCard);

                            botUser.GameDeck.DeckPower = active.Sum(x => x.CalculateCardPower());
                            botUser.GameDeck.CardsInDeck = active.Count;

                            await db.SaveChangesAsync();

                            QueryCacheManager.ExpireTag(new string[] { $"user-{botUser.Id}", "users" });
                            result = "Card status toggled".ToResponse(200);
                        }
                    }), discordId);

                    if (!await _executor.TryAdd(exe, TimeSpan.FromSeconds(1)))
                    {
                        return "Command queue is full".ToResponse(503);
                    }

                    await exe.WaitAsync();
                    return result;
                }
            }
            return "The appropriate claim was not found".ToResponse(403);
        }

        private async Task<List<CardFinalView>> ToViewWithUsernamesAsync(List<Card> cards)
        {
            var usernames = new Dictionary<ulong, string>();
            foreach (var shindenId in cards.Select(x => x.GameDeck.User.Shinden).Distinct())
            {
                usernames[shindenId] = await GetUsernameAsync(shindenId);
            }
            return cards.Select(x => x.ToView(usernames[x.GameDeck.User.Shinden], 0, _time)).ToList();
        }

        private async Task<string> GetUsernameAsync(ulong shindenId)
        {
            if (shindenId != 0)
            {
                if (shindenId == 1)
                {
                    return _client.CurrentUser.GetUserNickInGuild();
                }

                if (_nameCache.TryGetValue(shindenId, out string username))
                {
                    return username;
                }

                var res = await _shClient.User.GetAsync(shindenId);
                if (res.IsSuccessStatusCode())
                {
                    _nameCache.Set(shindenId, res.Body.Name, new MemoryCacheEntryOptions()
                        .SetAbsoluteExpiration(TimeSpan.FromHours(24)));

                    return res.Body.Name;
                }
            }
            return string.Empty;
        }

        // zwraca błąd do odesłania albo null, gdy pakiety są poprawne
        private static ObjectResult TryGetBoosterPacks(List<Models.CardBoosterPack> boosterPacks, out List<BoosterPack> packs)
        {
            packs = new List<BoosterPack>();
            if (boosterPacks.IsNullOrEmpty())
            {
                return "Model is Invalid".ToResponse(500);
            }

            foreach (var pack in boosterPacks)
            {
                var rPack = pack.ToRealPack();
                if (rPack != null) packs.Add(rPack);
            }

            if (packs.Count < 1)
            {
                return "Data is Invalid".ToResponse(500);
            }
            return null;
        }

        private async Task<ActionResult<IEnumerable<Card>>> GetCardsFormWishlistAsync(User user, Database.DatabaseContext db)
        {
            if (user == null)
            {
                return "User not found!".ToResponse(404);
            }

            if (user.GameDeck.Wishes.Count < 1)
            {
                return "Wishlist not found!".ToResponse(404);
            }

            var p = user.GameDeck.GetCharactersWishList();
            var t = user.GameDeck.GetTitlesWishList();
            var c = user.GameDeck.GetCardsWishList();

            return Ok(await _waifu.GetCardsFromWishlistAsync(c, p, t, db, user.GameDeck.Cards));
        }

        public static PhysicalFileResult CardImageFile(string path)
            => new PhysicalFileResult(System.IO.Path.GetFullPath(path), GetImageContentType(path));

        private static string GetImageContentType(string path)
            => new FileExtensionContentTypeProvider().TryGetContentType(path, out var type) ? type : "application/octet-stream";

        private async Task UpdateWishlistCountAsync(Database.DatabaseContext db, List<Card> cards, User user)
        {
            var allWWCnt = await db.WishlistCountData.AsQueryable().AsNoTracking().ToListAsync();

            foreach (var card in cards)
            {
                card.Affection += user.GameDeck.AffectionFromKarma();
                card.FirstIdOwner = user.Id;

                var wwc = allWWCnt.FirstOrDefault(x => x.Id == card.Character);
                card.AWhoWantsCount = wwc?.ACount ?? 0;
                card.WhoWantsCount = wwc?.Count ?? 0;

                user.GameDeck.Cards.Add(card);
                await user.GameDeck.RemoveCharacterFromWishListAsync(card.Character, db);
            }
        }

        private IQueryable<Card> FilterCardsByTags(IQueryable<Card> cards, CardsQueryFilter filter)
        {
            if (!filter.IncludeTags.IsNullOrEmpty())
            {
                if (filter.FilterTagsMethod == FilterTagsMethodType.And)
                {
                    foreach (var iTag in filter.IncludeTags)
                        cards = cards.Where(x => x.Tags.Any(t => t.Id == iTag.Id));
                }
                else
                {
                    var tagsIds = filter.IncludeTags.Select(x => x.Id);
                    cards = cards.Where(x => x.Tags.Any(t => tagsIds.Contains(t.Id)));
                }
            }

            if (!filter.ExcludeTags.IsNullOrEmpty())
            {
                foreach (var eTag in filter.ExcludeTags)
                    cards = cards.Where(x => !x.Tags.Any(t => t.Id == eTag.Id));
            }

            return cards;
        }

        private IQueryable<Card> FilterCardsByIds(IQueryable<Card> cards, CardsQueryFilter filter)
        {
            if (!filter.CardIds.IsNullOrEmpty())
            {
                cards = cards.Where(x => filter.CardIds.Contains(x.Id));
            }

            if (!filter.CharIds.IsNullOrEmpty())
            {
                cards = cards.Where(x => filter.CharIds.Contains(x.Character));
            }

            return cards;
        }
    }
}