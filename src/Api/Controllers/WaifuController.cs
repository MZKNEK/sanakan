#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading;
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
using Sanakan.ShindenCompat;

namespace Sanakan.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WaifuController : ControllerBase
    {
        private const uint MaxCardsPerRequest = 4000;
        private const int MaxPacksPerRequest = 100;
        private const uint MaxActivitiesPerRequest = 4000;
        private const int MaxTopCharacters = 1000;

        /// <summary>Zabezpiecza przesunięcie (uint) przed przepełnieniem rzutowania na int.</summary>
        private static int ClampOffset(uint offset) => (int)Math.Min(offset, int.MaxValue);

        private static readonly TimeSpan SearchCountLifetime = TimeSpan.FromMinutes(5);

        // a text search has to scan every card to count its results, so the count is kept while the same results are paged
        // or sorted; other filters are counted every time. A player's list drops it when the player's cards change.
        private static async Task<int> CountFilteredCardsAsync(Database.DatabaseContext db, IQueryable<Card> filtered, CardsQueryFilter filter, ulong deckId)
        {
            if (string.IsNullOrEmpty(filter.SearchText))
                return await filtered.CountAsync();

            var options = new MemoryCacheEntryOptions().SetAbsoluteExpiration(SearchCountLifetime);
            var tags = deckId == 0 ? Array.Empty<string>() : new[] { CacheTags.User(deckId) };
            var counts = await QueryCache.GetOrLoadAsync(db, SearchCountKey(filter, deckId), async () => new List<int> { await filtered.CountAsync() }, options, tags);
            return counts[0];
        }

        // the sort order and the page do not change the count
        internal static string SearchCountKey(CardsQueryFilter filter, ulong deckId)
        {
            static string Ids(IEnumerable<ulong> ids) => ids == null ? "" : string.Join(",", ids.Distinct().OrderBy(x => x));
            return $"search-count-{deckId}|{filter.SearchText.ToLowerInvariant()}|in:{Ids(filter.IncludeTags?.Select(x => x.Id))}|{filter.FilterTagsMethod}"
                + $"|ex:{Ids(filter.ExcludeTags?.Select(x => x.Id))}|cards:{Ids(filter.CardIds)}|chars:{Ids(filter.CharIds)}";
        }

        // the page is picked by ids only, so MySQL sorts narrow rows instead of whole cards (a tag can have 200k cards);
        // then only the cards of the page are loaded, in the picked order
        private static async Task<List<Card>> LoadPageAsync(Database.DatabaseContext db, IQueryable<Card> filtered, uint offset, uint count, Func<IQueryable<Card>, IQueryable<Card>> load)
        {
            var ids = await filtered.Select(x => x.Id).Skip(ClampOffset(offset)).Take((int)Math.Min(count, MaxCardsPerRequest)).ToListAsync();
            if (ids.Count == 0)
                return new List<Card>();

            var position = ids.Select((id, index) => (id, index)).ToDictionary(x => x.id, x => x.index);
            var cards = await load(db.Cards.AsQueryable().Where(x => ids.Contains(x.Id))).ToListAsync();
            return cards.OrderBy(x => position[x.Id]).ToList();
        }
        private const int MaxFilterItems = 1000;
        private const int MaxSearchTextLength = 100;

        private readonly Waifu _waifu;
        private readonly TagHelper _tags;
        private readonly IConfig _config;
        private readonly ISystemTime _time;
        private readonly IExecutor _executor;
        private readonly Expedition _expedition;
        private readonly ShindenClient _shClient;
        private readonly Services.Shinden _shinden;
        private readonly DiscordSocketClient _client;
        private readonly ILogger _logger;

        private const int MaxShindenFailures = 2;
        private int _shindenFailures;

        private const string ShindenPacksIdempotencyScope = "shinden-boosterpack";
        private const string ShindenOpenIdempotencyScope = "shinden-boosterpack-open";

        public WaifuController(ShindenClient shClient, Waifu waifu, IExecutor executor, TagHelper tags,
            IConfig config, ISystemTime time, Services.Shinden shinden, DiscordSocketClient client, Expedition expedition,
            ILogger logger)
        {
            _time = time;
            _logger = logger;
            _tags = tags;
            _waifu = waifu;
            _client = client;
            _config = config;
            _shinden = shinden;
            _executor = executor;
            _shClient = shClient;
            _expedition = expedition;
        }

        protected virtual Database.DatabaseContext CreateDatabaseContext() => new Database.DatabaseContext(_config);

        /// <summary>
        /// Pobiera użytkowników posiadających kartę postaci
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
        /// Pobiera listę kart użytkownika
        /// </summary>
        /// <param name="id">id użytkownika shindena</param>
        /// <returns>lista kart</returns>
        /// <response code="404">User not found</response>
        [HttpGet("user/{id}/cards"), Authorize(Policy = "Site")]
        public async Task<ActionResult<IEnumerable<Database.Models.Card>>> GetUserCardsAsync(ulong id)
        {
            using (var db = new Database.DatabaseContext(_config))
            {
                var user = await db.Users.AsQueryable().Where(x => id != 0 && x.Shinden == id)
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
        [HttpPost("user/activity/{count}"), AllowAnonymous]
        public async Task<ActionResult<IEnumerable<UserActivity>>> GetUsersActivitiesAsync(uint count, [FromBody]List<ulong> users)
        {
            if (count == 0 || count > MaxActivitiesPerRequest)
                count = MaxActivitiesPerRequest;

            if (users != null && users.Count > MaxFilterItems)
                return "Too many users!".ToResponse(400);

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
        /// <param name="lastId">id aktywności od której zacząć nową listę</param>
        /// <returns>lista aktywności</returns>
        [HttpGet("user/activity/{lastId}"), AllowAnonymous]
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
        [HttpPost("total/cards/{offset}/{count}"), AllowAnonymous]
        public async Task<ActionResult<FilteredCards>> GetCardsWithOffsetAndFilterAsync(uint offset, uint count, [FromBody]CardsQueryFilter filter)
        {
            var invalid = ValidateFilter(filter);
            if (invalid != null) return invalid;

            using (var db = new Database.DatabaseContext(_config))
            {
                var query = db.Cards.AsQueryable().TagWith(Database.CardListHint.Tag);
                if (!string.IsNullOrEmpty(filter.SearchText))
                {
                    query = query.TagWith(Database.CardListHint.SearchTag)
                        .Where(x => x.Name.Contains(filter.SearchText) || x.Title.Contains(filter.SearchText));
                }

                query = CardsQueryFilter.Use(filter.OrderBy, query);
                query = FilterCardsByIds(query, filter);
                query = FilterCardsByTags(query, filter);
                var cards = await LoadPageAsync(db, query, offset, count,
                    page => page.AsSplitQuery().Include(x => x.GameDeck).ThenInclude(x => x.User).Include(x => x.Tags).AsNoTrackingWithIdentityResolution());

                return new FilteredCards{TotalCards = await CountFilteredCardsAsync(db, query, filter, 0), Cards = await ToViewWithUsernamesAsync(cards)};
            }
        }

        /// <summary>
        /// Pobiera x kart z przefiltowanej listy kart ultimate
        /// </summary>
        /// <param name="offset">przesunięcie</param>
        /// <param name="count">liczba kart</param>
        /// <param name="filter">filtry listy</param>
        /// <returns>lista kart</returns>
        [HttpPost("ultimate/cards/{offset}/{count}"), AllowAnonymous]
        public async Task<ActionResult<FilteredCards>> GetUltimateCardsWithOffsetAndFilterAsync(uint offset, uint count, [FromBody]CardsQueryFilter filter)
        {
            var invalid = ValidateFilter(filter);
            if (invalid != null) return invalid;

            using (var db = CreateDatabaseContext())
            {
                var expireTime = new MemoryCacheEntryOptions().SetAbsoluteExpiration(_time.Now().AddHours(4));
                // a bare boolean column makes MySQL guess 50% selectivity and scan all tag relations in the split tag query;
                // comparing with a parameter emits "= @p" and lets it start from the index
                var ultimate = true;
                var all = await db.Cards.AsQueryable().AsSplitQuery().Where(x => x.FromFigure == ultimate).Include(x => x.GameDeck).ThenInclude(x => x.User).Include(x => x.Tags)
                    .AsNoTrackingWithIdentityResolution().FromCacheAsync(db, "ultimate-cards", expireTime, CacheTags.UltimateCards);

                return await FilterCachedCardsAsync(all, filter, offset, count);
            }
        }

        /// <summary>
        /// Pobiera x kart z przefiltowanej listy unikatowych kart
        /// </summary>
        /// <param name="offset">przesunięcie</param>
        /// <param name="count">liczba kart</param>
        /// <param name="filter">filtry listy</param>
        /// <returns>lista kart</returns>
        [HttpPost("unique/cards/{offset}/{count}"), AllowAnonymous]
        public async Task<ActionResult<FilteredCards>> GetUniqueCardsWithOffsetAndFilterAsync(uint offset, uint count, [FromBody]CardsQueryFilter filter)
        {
            var invalid = ValidateFilter(filter);
            if (invalid != null) return invalid;

            using (var db = CreateDatabaseContext())
            {
                var expireTime = new MemoryCacheEntryOptions().SetAbsoluteExpiration(_time.Now().AddHours(8));
                // same reason as in the ultimate cards query
                var unique = true;
                var all = await db.Cards.AsQueryable().AsSplitQuery().Where(x => x.Unique == unique).Include(x => x.GameDeck).ThenInclude(x => x.User).Include(x => x.Tags)
                    .AsNoTrackingWithIdentityResolution().FromCacheAsync(db, "unique-cards", expireTime, CacheTags.UniqueCards);

                return await FilterCachedCardsAsync(all, filter, offset, count);
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
        [HttpPost("user/{id}/cards/{offset}/{count}"), AllowAnonymous]
        public async Task<ActionResult<FilteredCards>> GetUsersCardsByShindenIdWithOffsetAndFilterAsync(ulong id, uint offset, uint count, [FromBody]CardsQueryFilter filter)
        {
            var invalid = ValidateFilter(filter);
            if (invalid != null) return invalid;

            using (var db = new Database.DatabaseContext(_config))
            {
                var user = await db.Users.AsQueryable().Where(x => id != 0 && x.Shinden == id).Include(x => x.GameDeck).AsNoTracking().AsSplitQuery().FirstOrDefaultAsync();

                if (user == null)
                {
                    return "User not found".ToResponse(404);
                }

                if (user.IsBlacklisted)
                {
                    return "User on blacklist".ToResponse(401);
                }

                var deckId = user.GameDeck.Id;
                var query = db.Cards.AsQueryable().TagWith(Database.CardListHint.Tag).Where(x => x.GameDeckId == deckId);
                if (!string.IsNullOrEmpty(filter.SearchText))
                {
                    query = query.Where(x => x.Name.Contains(filter.SearchText) || x.Title.Contains(filter.SearchText));
                }

                query = CardsQueryFilter.Use(filter.OrderBy, query);
                query = FilterCardsByIds(query, filter);
                query = FilterCardsByTags(query, filter);

                var username = await GetUsernameAsync(user.Shinden);
                var cards = await LoadPageAsync(db, query, offset, count, page => page.AsSplitQuery().Include(x => x.Tags).AsNoTrackingWithIdentityResolution());

                return new FilteredCards{TotalCards = await CountFilteredCardsAsync(db, query, filter, deckId), Cards = cards.ToView(username, id, _time)};
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
        [HttpGet("user/{id}/cards/{offset}/{count}"), AllowAnonymous]
        public async Task<ActionResult<IEnumerable<CardFinalView>>> GetUsersCardsByShindenIdWithOffsetAsync(ulong id, uint offset, uint count)
        {
            using (var db = new Database.DatabaseContext(_config))
            {
                var user = await db.Users.AsQueryable().AsSplitQuery().Where(x => id != 0 && x.Shinden == id).Include(x => x.GameDeck).AsNoTracking().FirstOrDefaultAsync();

                if (user == null)
                {
                    return "User not found".ToResponse(404);
                }

                if (user.IsBlacklisted)
                {
                    return "User on blacklist".ToResponse(401);
                }

                var cards = await db.Cards.AsQueryable().AsSplitQuery().Where(x => x.GameDeckId == user.GameDeck.Id).Include(x => x.Tags).OrderBy(x => x.Id).Skip(ClampOffset(offset)).Take((int)Math.Min(count, MaxCardsPerRequest)).AsNoTrackingWithIdentityResolution().ToListAsync();
                var username = await GetUsernameAsync(user.Shinden);
                return cards.ToView(username, user.Shinden, _time);
            }
        }

        /// <summary>
        /// Pobiera kartę
        /// </summary>
        /// <param name="id">id karty</param>
        /// <returns>karta</returns>
        /// <response code="404">Card not found</response>
        [HttpGet("card/{id}/view"), AllowAnonymous]
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
        /// Pobiera surową listę życzeń użytkownika
        /// </summary>
        /// <param name="id">id użytkownika shindena</param>
        /// <returns>lista życzeń</returns>
        /// <response code="404">User not found</response>
        /// <response code="401">User wishlist is private</response>
        [HttpGet("user/shinden/{id}/wishlist/raw"), AllowAnonymous]
        public async Task<ActionResult<IEnumerable<WishlistObject>>> GetUsersRawWishlistByShindenIdAsync(ulong id)
        {
            using (var db = new Database.DatabaseContext(_config))
            {
                var user = await db.Users.AsQueryable().AsSplitQuery().Where(x => id != 0 && x.Shinden == id).Include(x => x.GameDeck).ThenInclude(x => x.Wishes).AsNoTracking().FirstOrDefaultAsync();

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
        /// Pobiera topkę życzeń użytkowników
        /// </summary>
        /// <param name="count">jak dużo wpisów</param>
        /// <returns>topka życzeń</returns>
        /// <response code="404">Not found</response>
        [HttpGet("top/characters/{count}"), AllowAnonymous]
        public async Task<ActionResult<IEnumerable<Database.Models.Analytics.WishlistCount>>> GetTopCharactersAsync(int count)
        {
            using (var db = new Database.DatabaseContext(_config))
            {
                var limit = Math.Clamp(count, 1, MaxTopCharacters);
                var top = await db.WishlistCountData.AsQueryable().AsNoTracking().OrderByDescending(x => x.Count).Take(limit).ToListAsync();
                return Ok(top);
            }
        }

        /// <summary>
        /// Pobiera profil użytkownika
        /// </summary>
        /// <param name="id">id użytkownika shindena</param>
        /// <returns>profil</returns>
        /// <response code="404">User not found</response>
        [HttpGet("user/{id}/profile"), AllowAnonymous]
        public async Task<ActionResult<UserSiteProfile>> GetUserWaifuProfileAsync(ulong id)
        {
            using (var db = new Database.DatabaseContext(_config))
            {
                var countingTimer = System.Diagnostics.Stopwatch.StartNew();
                var expireTime = new MemoryCacheEntryOptions().SetAbsoluteExpiration(_time.Now().AddMinutes(15));

                // deck, card and tag changes only expire the discord user tag, so the profile has to depend on it too
                var discordIds = await db.Users.AsQueryable().Where(x => id != 0 && x.Shinden == id).Select(x => x.Id)
                    .FromCacheAsync(db, $"user-shinden-discord-{id}", expireTime, CacheTags.UserProfile(id));
                var discordId = discordIds.FirstOrDefault();
                // most missing profiles are Shinden users without a bot account, the cached lookup answers them without the heavy query
                if (discordId == 0)
                {
                    return "User not found".ToResponse(404);
                }

                var profileTags = new[] { CacheTags.UserProfile(id), CacheTags.User(discordId) };
                var galleryTag = _tags.GetTag(TagType.Gallery);

                var cached = await QueryCache.GetOrLoadAsync(db, $"user-site-profile-{id}", () => LoadSiteProfileUserAsync(db, id, galleryTag.Id), expireTime, profileTags);
                var user = cached.FirstOrDefault();

                if (user == null)
                {
                    return "User not found".ToResponse(404);
                }

                if (user.IsBlacklisted)
                {
                    return "User on blacklist".ToResponse(401);
                }

                var pvp = (await QueryCache.GetOrLoadAsync(db, $"user-site-pvp-{id}", () => CountSiteProfilePvpAsync(db, user.GameDeck.Id), expireTime, profileTags)).First();
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
                    {"pvpplayed",       pvp.Played},
                    {"pvpwon",          pvp.Won},
                    {"pvpglobal",       user.GameDeck.GlobalPVPRank},
                    {"pvpseason",       user.IsPVPSeasonalRankActive(_time.Now()) ? user.GameDeck.SeasonalPVPRank : 0},
                    {"kccnt",           cardDetails.TotalKCount},
                    {"activekccnt",     cardDetails.TotalActiveKCCount},
                    {"gallerylimit",    user.GameDeck.CardsInGallery},
                };

                var galleryOrder = string.IsNullOrEmpty(user.GameDeck.GalleryOrderedIds) ? new List<ulong>()
                    : user.GameDeck.GalleryOrderedIds.Split(" ").Select(x => UserSiteProfile.TryParseIds(x)).ToList();

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

        // Full rows of every card are the bulk of a profile load, so statistics are computed from narrow rows
        // and only the cards the profile shows are loaded whole. They replace their narrow copies in GameDeck.Cards.
        public sealed class SiteProfilePvp
        {
            public long Played { get; init; }
            public long Won { get; init; }
        }

        // the most active players have tens of thousands of fights, the profile only shows two counts
        private static async Task<List<SiteProfilePvp>> CountSiteProfilePvpAsync(Database.DatabaseContext db, ulong deckId)
        {
            var counts = await db.CardPvPStats.AsQueryable().AsNoTracking().Where(x => x.GameDeckId == deckId && x.Type == FightType.NewVersus)
                .GroupBy(x => x.Result).Select(x => new { x.Key, Count = x.LongCount() }).ToListAsync();
            return new List<SiteProfilePvp> { new SiteProfilePvp { Played = counts.Sum(x => x.Count), Won = counts.Where(x => x.Key == FightResult.Win).Sum(x => x.Count) } };
        }

        private async Task<List<User>> LoadSiteProfileUserAsync(Database.DatabaseContext db, ulong shindenId, ulong galleryTagId)
        {
            var users = await db.Users.AsQueryable().AsSplitQuery().Where(x => shindenId != 0 && x.Shinden == shindenId).Include(x => x.GameDeck).ThenInclude(x => x.Tags)
                .Include(x => x.Stats).AsNoTracking().ToListAsync();

            var user = users.FirstOrDefault();
            if (user == null)
                return users;

            var deck = user.GameDeck;
            var galleryTag = new Tag { Id = galleryTagId };
            var narrow = await db.Cards.AsQueryable().AsNoTracking().Where(x => x.GameDeckId == deck.Id).Select(x => new
            {
                x.Id, x.Character, x.Rarity, x.Quality, x.BorderOverflow, x.BorderVariant, x.RestartCnt, x.WhoWantsCount, x.AWhoWantsCount,
                x.Unique, x.FromFigure, x.CardPower, x.IsAnimatedImage, x.Expedition,
                HasCustomImage = x.CustomImage != null && x.CustomImage != "",
                HasCustomBorder = x.CustomBorder != null && x.CustomBorder != "",
                InGallery = x.Tags.Any(t => t.Id == galleryTagId),
            }).ToListAsync();

            // the statistics only check whether the custom image and border are set, the placeholders never reach a view;
            // ties in the statistics are resolved by card order, which was the id order of the full load (sorted here, SQLite cannot order by ulong)
            deck.Cards = narrow.OrderBy(x => x.Id).Select(x => new Card
            {
                Id = x.Id, Character = x.Character, Rarity = x.Rarity, Quality = x.Quality, BorderOverflow = x.BorderOverflow, BorderVariant = x.BorderVariant,
                RestartCnt = x.RestartCnt, WhoWantsCount = x.WhoWantsCount, AWhoWantsCount = x.AWhoWantsCount, Unique = x.Unique, FromFigure = x.FromFigure,
                CardPower = x.CardPower, IsAnimatedImage = x.IsAnimatedImage, Expedition = x.Expedition, GameDeckId = deck.Id,
                CustomImage = x.HasCustomImage ? "set" : null,
                CustomBorder = x.HasCustomBorder ? "set" : null,
                Tags = x.InGallery ? new List<Tag> { galleryTag } : new List<Tag>(),
            }).ToList();

            var details = _waifu.GetCardsDetails(deck.Cards);
            var shown = deck.GetOrderedGalleryCardsUnlimited(galleryTagId).Take(deck.CardsInGallery)
                .Concat(details.CardsOnExpeditions)
                .Append(details.MostPowerfulCard).Append(details.CardWithMostRestarts).Append(deck.GetWaifuCard())
                .Where(x => x != null).Select(x => x.Id).Distinct().ToList();

            var full = await db.Cards.AsQueryable().AsNoTracking().Include(x => x.Tags).Where(x => x.GameDeckId == deck.Id && shown.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id);

            deck.User = user;
            foreach (var card in full.Values)
                card.GameDeck = deck;

            deck.Cards = deck.Cards.Select(x => full.TryGetValue(x.Id, out var card) ? card : x).ToList();
            return users;
        }

        /// <summary>
        /// Synchronizuje karty postaci z jej aktualnymi danymi na Shindenie: id postaci, obrazek (pusty, gdy postać go nie ma)
        /// i nazwę; tytuł serii zostaje bez zmian
        /// </summary>
        /// <param name="id">id postaci z bazy shindena</param>
        /// <param name="from">id usuniętej postaci, której karty przechodzą na postać {id} (0 - nic nie przenosimy)</param>
        /// <param name="overrides">opcjonalne dane podane ręcznie, mają pierwszeństwo przed danymi z Shindena; tytuł serii zmienia się tylko tak</param>
        /// <response code="400">from is the same as id</response>
        /// <response code="404">Character not found (and no data given by hand)</response>
        /// <response code="503">Shinden did not answer or the command queue is full, retry after the Retry-After header</response>
        [HttpPost("character/{id}/sync"), Authorize(Policy = "Site")]
        public async Task<IActionResult> SyncCharacterCardsAsync(ulong id, [FromQuery] ulong from = 0,
            [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] Models.CharacterCardInfoUpdate overrides = null)
        {
            if (from == id)
                return "The character to move cards from is the same as the target one!".ToResponse(400);

            // an empty object ({}) is no data given by hand, so a Shinden failure is still reported
            if (overrides != null && overrides.ImageUrl == null && overrides.CharacterName == null && overrides.CardSeriesTitle == null)
                overrides = null;

            // a cut url would not work, a too long name or title is cut by the card
            if (overrides?.ImageUrl?.Length > Card.TextMaxLength)
                return $"Image url can have at most {Card.TextMaxLength} characters!".ToResponse(400);

            var setImage = false;
            string image = null, name = null;
            Response<ShindenCompat.Models.ICharacterInfo> response = null;
            try
            {
                response = await _shClient.GetCharacterInfoAsync(id);
            }
            catch (ShindenException) { }

            if (response?.IsSuccessStatusCode() == true)
            {
                setImage = true;
                image = response.Body.HasImage ? response.Body.PictureUrl : null;
                name = response.Body.ToString();
            }
            else if (overrides == null)
            {
                if (response?.Code == System.Net.HttpStatusCode.NotFound)
                    return "Character not found!".ToResponse(404);

                // a limit, an outage or a timeout on Shinden, the site retries later; Shinden's own wait when it gave one
                var wait = response?.RetryAfter is { } retryAfter && retryAfter > TimeSpan.Zero ? (int)Math.Ceiling(retryAfter.TotalSeconds) : 60;
                Response.Headers["Retry-After"] = wait.ToString();
                return "Shinden is not available, try again later!".ToResponse(503);
            }

            if (overrides?.ImageUrl != null)
            {
                setImage = true;
                image = overrides.ImageUrl;
            }
            name = overrides?.CharacterName ?? name;
            var title = overrides?.CardSeriesTitle;

            var exe = new Executable($"sync cards-{id}{(from != 0 ? $" from {from}" : "")}", new Func<Task>(async () =>
            {
                using (var db = new Database.DatabaseContext(_config))
                {
                    var userRelease = new List<string>();
                    var cards = await db.Cards.AsQueryable().Where(x => x.Character == id || (from != 0 && x.Character == from)).ToListAsync();

                    foreach (var card in cards)
                    {
                        var newImage = setImage ? image : card.Image;
                        var newName = name ?? card.Name;
                        var newTitle = title ?? card.Title;
                        // the saved card pictures stay when nothing they show has changed
                        if (card.Character == id && card.Image == newImage && card.Name == newName && card.Title == newTitle)
                            continue;

                        card.Character = id;
                        card.Image = newImage;
                        card.Name = newName;
                        card.Title = newTitle;

                        try
                        {
                            _waifu.DeleteCardImageIfExist(card);
                        }
                        catch (Exception) { }

                        userRelease.Add(CacheTags.User(card.GameDeckId));
                    }

                    await db.SaveChangesAsync();

                    QueryCache.ExpireTag(userRelease.ToArray());
                }
            }), 0, Priority.High);

            if (!await _executor.TryAdd(exe, TimeSpan.FromSeconds(1)))
            {
                Response.Headers["Retry-After"] = "10";
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
                var user = await db.GetWishlistOwnerAsync(id);
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
                var user = await db.GetWishlistOwnerByShindenIdAsync(id);
                return await GetCardsFormWishlistAsync(user, db);
            }
        }

        /// <summary>
        /// Pobiera listę kart z danym tagiem
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
        [HttpGet("card/{id}"), AllowAnonymous]
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
        /// <response code="404">User/Season not found</response>
        /// <response code="500">Model is Invalid</response>
        /// <response code="503">Command queue is full / Can't connect to shinden</response>
        [HttpPost("discord/{id}/boosterpack"), Authorize(Policy = "Site")]
        public async Task<IActionResult> GiveUserAPacksAsync(ulong id, [FromBody]List<Models.CardBoosterPack> boosterPacks)
        {
            var (invalid, packs) = await GetBoosterPacksAsync(boosterPacks);
            if (invalid != null) return invalid;

            using (var db = new Database.DatabaseContext(_config))
            {
                if (!await db.Users.AsQueryable().AnyAsync(x => x.Id == id))
                {
                    return "User not found!".ToResponse(404);
                }

                var exe = new Executable($"api-packet u{id}", new Func<Task>(async () =>
                {
                    using (var dbs = new Database.DatabaseContext(_config))
                    {
                        var botUser = await dbs.GetUserOrCreateSimpleAsync(id, _time);
                        AddBoosterPacks(dbs, botUser, packs);

                        await dbs.SaveChangesAsync();

                        QueryCache.ExpireTag(new string[] { CacheTags.User(botUser.Id) });
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
        /// <remarks>
        /// Z nagłówkiem Idempotency-Key odpowiedź przychodzi dopiero po zapisie pakietów, a powtórka z tym samym kluczem
        /// w ciągu 48 h nie dodaje ich ponownie (nagłówek Idempotent-Replayed: true). Zapamiętywany jest tylko sukces.
        /// </remarks>
        /// <param name="id">id użytkownika shindena</param>
        /// <param name="boosterPacks">model pakietu</param>
        /// <param name="idempotencyKey">unikalny klucz odbioru (do 64 znaków ASCII)</param>
        /// <returns>użytkownik bota</returns>
        /// <response code="400">Invalid Idempotency-Key</response>
        /// <response code="404">User/Season not found</response>
        /// <response code="409">Request with this Idempotency-Key is in progress</response>
        /// <response code="422">Idempotency-Key was used with a different request</response>
        /// <response code="500">Model is Invalid</response>
        /// <response code="503">Command queue is full / Can't connect to shinden</response>
        /// <response code="504">Command timed out, boosterpacks will still be added</response>
        [HttpPost("shinden/{id}/boosterpack"), Authorize(Policy = "Site")]
        public async Task<ActionResult<UserWithToken>> GiveShindenUserAPacksAsync(ulong id, [FromBody]List<Models.CardBoosterPack> boosterPacks,
            [FromHeader(Name = Services.Idempotency.KeyHeader)] string idempotencyKey = null)
        {
            if (idempotencyKey != null && !Services.Idempotency.IsValidKey(idempotencyKey))
                return "Invalid Idempotency-Key".ToResponse(400);

            var (invalid, packs) = await GetBoosterPacksAsync(boosterPacks);
            if (invalid != null) return invalid;

            if (idempotencyKey != null)
                return await GiveShindenUserAPacksOnceAsync(id, boosterPacks, packs, idempotencyKey);

            using (var db = CreateDatabaseContext())
            {
                var user = await db.GetUserWithDeckByShindenIdAsync(id);
                if (user == null)
                {
                    return "User not found!".ToResponse(404);
                }

                var discordId = user.Id;
                var exe = new Executable($"api-packet u{discordId}", new Func<Task>(async () =>
                {
                    using (var dbs = CreateDatabaseContext())
                    {
                        var botUser = await dbs.GetUserOrCreateSimpleAsync(discordId, _time);
                        AddBoosterPacks(dbs, botUser, packs);

                        await dbs.SaveChangesAsync();

                        QueryCache.ExpireTag(new string[] { CacheTags.User(botUser.Id) });
                    }
                }), user.Id);

                if (!await _executor.TryAdd(exe, TimeSpan.FromSeconds(1)))
                {
                    return "Command queue is full".ToResponse(503);
                }

                return ShindenPacksResponse(user);
            }
        }

        // Any error response means nothing was saved, so only a success is remembered under the key.
        private async Task<ActionResult<UserWithToken>> GiveShindenUserAPacksOnceAsync(ulong id, List<Models.CardBoosterPack> request,
            List<BoosterPack> packs, string key)
        {
            var record = new IdempotencyRecord
            {
                Scope = ShindenPacksIdempotencyScope,
                Key = key,
                OwnerId = id,
                RequestHash = Services.Idempotency.Hash(request),
                CreatedAt = _time.Now(),
            };

            var running = Services.Idempotency.TryBegin(record);
            if (running != null)
            {
                return Services.Idempotency.SameRequest(running, record)
                    ? "Request with this Idempotency-Key is in progress".ToResponse(409)
                    : IdempotencyKeyReused();
            }

            var handedOff = false;
            try
            {
                using (var db = CreateDatabaseContext())
                {
                    var done = await Services.Idempotency.FindAsync(db, record.Scope, key, record.CreatedAt);
                    if (done != null)
                    {
                        if (!Services.Idempotency.SameRequest(done, record))
                            return IdempotencyKeyReused();

                        Response.Headers[Services.Idempotency.ReplayedHeader] = "true";
                        return ShindenPacksResponse(await db.GetUserWithDeckByShindenIdAsync(id));
                    }

                    var user = await db.GetUserWithDeckByShindenIdAsync(id);
                    if (user == null)
                    {
                        return "User not found!".ToResponse(404);
                    }

                    var committed = false;
                    var discordId = user.Id;
                    var exe = new Executable($"api-packet u{discordId}", new Func<Task>(async () =>
                    {
                        try
                        {
                            using (var dbs = CreateDatabaseContext())
                            {
                                var botUser = await dbs.GetUserOrCreateSimpleAsync(discordId, _time);
                                AddBoosterPacks(dbs, botUser, packs);

                                dbs.IdempotencyRecords.Add(record);

                                await dbs.SaveChangesAsync();
                                committed = true;

                                QueryCache.ExpireTag(new string[] { CacheTags.User(botUser.Id) });
                                await Services.Idempotency.CleanupIfDueAsync(dbs, record.CreatedAt);
                            }
                        }
                        finally
                        {
                            Services.Idempotency.End(record);
                        }
                    }), discordId);

                    if (!await _executor.TryAdd(exe, TimeSpan.FromSeconds(1)))
                    {
                        return "Command queue is full".ToResponse(503);
                    }
                    handedOff = true;

                    try
                    {
                        if (!await exe.WaitAsync(Executable.ApiMaxWait))
                            return "Command timed out".ToResponse(504);
                    }
                    catch (Exception)
                    {
                        if (!committed)
                            return "Boosterpack not added".ToResponse(500);
                    }

                    return ShindenPacksResponse(user);
                }
            }
            finally
            {
                if (!handedOff)
                    Services.Idempotency.End(record);
            }
        }

        private static ObjectResult IdempotencyKeyReused()
            => "Idempotency-Key was used with a different request".ToResponse(422);

        private UserWithToken ShindenPacksResponse(User user)
        {
            TokenData tokenData = null;
            var currUser = ControllerContext.HttpContext.User;
            if (user != null && currUser.HasClaim(x => x.Type == ClaimTypes.Webpage) && !user.IsBlacklisted)
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

        /// <summary>
        /// Otwiera pakiety i dodaje użytkownikowi karty wylosowane z nich
        /// </summary>
        /// <remarks>
        /// Z nagłówkiem Idempotency-Key od razu zwraca 202 ze stanem pending, a losowanie idzie w tle - wynik pod
        /// GET shinden/{id}/boosterpack/open/{key}. Powtórka z tym samym kluczem w ciągu 48 h nie losuje ponownie, tylko
        /// zwraca bieżący stan (nagłówek Idempotent-Replayed: true). Karty są dodawane wszystkie albo żadna.
        /// </remarks>
        /// <param name="id">id użytkownika shindena</param>
        /// <param name="boosterPacks">model pakietu</param>
        /// <param name="idempotencyKey">unikalny klucz odbioru (do 64 znaków ASCII)</param>
        /// <returns>karty</returns>
        /// <response code="202">Opening started or still in progress (with Idempotency-Key)</response>
        /// <response code="400">Invalid Idempotency-Key</response>
        /// <response code="404">User/Season not found</response>
        /// <response code="406">User has no space</response>
        /// <response code="422">Idempotency-Key was used with a different request</response>
        /// <response code="500">Model/Data is Invalid</response>
        /// <response code="503">Command queue is full / Can't connect to shinden</response>
        [HttpPost("shinden/{id}/boosterpack/open"), Authorize(Policy = "Site")]
        [ProducesResponseType(typeof(BoosterPackOpenStatus), 202)]
        public async Task<ActionResult<List<Card>>> GiveShindenUserAPacksAndOpenAsync(ulong id, [FromBody]List<Models.CardBoosterPack> boosterPacks,
            [FromHeader(Name = Services.Idempotency.KeyHeader)] string idempotencyKey = null)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            if (idempotencyKey != null && !Services.Idempotency.IsValidKey(idempotencyKey))
                return "Invalid Idempotency-Key".ToResponse(400);

            var (invalid, packs) = await GetBoosterPacksAsync(boosterPacks);
            if (invalid != null) return invalid;

            if (idempotencyKey != null)
                return await OpenShindenUserPacksOnceAsync(id, boosterPacks, packs, idempotencyKey, timer);

            ulong discordId = 0;
            CharacterPoolType poolType = CharacterPoolType.Anime;
            using (var db = CreateDatabaseContext())
            {
                var bUser = await db.Users.AsQueryable().Where(x => id != 0 && x.Shinden == id).Include(x => x.GameDeck).ThenInclude(x => x.Cards).AsNoTracking().AsSplitQuery().FirstOrDefaultAsync();
                if (bUser == null)
                {
                    return "User not found".ToResponse(404);
                }
                if (bUser.GameDeck.Cards.Count + packs.Sum(x => (long)x.CardCnt) > bUser.GameDeck.MaxNumberOfCards)
                {
                    return "User has no space left in deck".ToResponse(406);
                }
                discordId = bUser.Id;
                poolType = bUser.PoolType;
            }

            var drawStart = timer.ElapsedMilliseconds;
            var cards = await DrawCardsAsync(packs, poolType);
            if (cards == null)
            {
                return "Can't connect to shinden!".ToResponse(503);
            }
            var drawMs = timer.ElapsedMilliseconds - drawStart;

            var exe = new Executable($"api-packet-open u{discordId}", new Func<Task>(async () =>
            {
                using (var db = CreateDatabaseContext())
                {
                    var botUser = await db.GetUserOrCreateSimpleAsync(discordId, _time);

                    botUser.Stats.OpenedBoosterPacks += packs.Count;

                    await UpdateWishlistCountAsync(db, cards, botUser);

                    await db.SaveChangesAsync();

                    QueryCache.ExpireTag(new string[] { CacheTags.User(botUser.Id) });
                }
            }), discordId);

            if (!await _executor.TryAdd(exe, TimeSpan.FromSeconds(1)))
            {
                return "Command queue is full".ToResponse(503);
            }

            if (!await exe.WaitAsync(Executable.ApiMaxWait))
                return "Command timed out".ToResponse(504);

            _logger?.Log($"API: opened {packs.Count} packs ({cards.Count} cards) s{id} u{discordId} in {timer.ElapsedMilliseconds}ms (draw {drawMs}ms)");
            return cards;
        }

        /// <summary>
        /// Stan otwierania pakietów zleconego przez POST shinden/{id}/boosterpack/open z nagłówkiem Idempotency-Key
        /// </summary>
        /// <param name="id">id użytkownika shindena</param>
        /// <param name="key">klucz z nagłówka Idempotency-Key</param>
        /// <returns>stan: pending, done (z kartami) albo failed (nic nie zapisano)</returns>
        /// <response code="400">Invalid Idempotency-Key</response>
        /// <response code="404">Nothing was saved under this key (or it expired), the request can be sent again</response>
        [HttpGet("shinden/{id}/boosterpack/open/{key}"), Authorize(Policy = "Site")]
        public async Task<ActionResult<BoosterPackOpenStatus>> GetShindenUserPacksOpenStatusAsync(ulong id, string key)
        {
            if (!Services.Idempotency.IsValidKey(key))
                return "Invalid Idempotency-Key".ToResponse(400);

            var running = Services.Idempotency.Running(ShindenOpenIdempotencyScope, key);
            if (running != null && running.OwnerId == id)
                return new BoosterPackOpenStatus { Status = BoosterPackOpenStatus.Pending };

            using (var db = CreateDatabaseContext())
            {
                var now = _time.Now();
                var done = await Services.Idempotency.FindAsync(db, ShindenOpenIdempotencyScope, key, now);
                if (done != null && done.OwnerId == id)
                {
                    _logger?.Log($"API: opened packs result picked up s{id} key {key} {(long)(now - done.CreatedAt).TotalMilliseconds}ms after request");
                    return await OpenDoneStatusAsync(db, done);
                }
            }

            var failure = Services.Idempotency.Failure(ShindenOpenIdempotencyScope, key);
            if (failure != null && failure.Value.Record.OwnerId == id)
                return new BoosterPackOpenStatus { Status = BoosterPackOpenStatus.Failed, Reason = failure.Value.Reason };

            return "Nothing saved under this key".ToResponse(404);
        }

        // Cards are drawn and saved in the background; under the key only a complete grant is ever stored.
        private async Task<ActionResult<List<Card>>> OpenShindenUserPacksOnceAsync(ulong id, List<Models.CardBoosterPack> request,
            List<BoosterPack> packs, string key, System.Diagnostics.Stopwatch timer)
        {
            var record = new IdempotencyRecord
            {
                Scope = ShindenOpenIdempotencyScope,
                Key = key,
                OwnerId = id,
                RequestHash = Services.Idempotency.Hash(request),
                CreatedAt = _time.Now(),
            };

            var running = Services.Idempotency.TryBegin(record);
            if (running != null)
            {
                if (!Services.Idempotency.SameRequest(running, record))
                    return IdempotencyKeyReused();

                Response.Headers[Services.Idempotency.ReplayedHeader] = "true";
                return Accepted(new BoosterPackOpenStatus { Status = BoosterPackOpenStatus.Pending });
            }

            var handedOff = false;
            try
            {
                ulong discordId;
                CharacterPoolType poolType;
                using (var db = CreateDatabaseContext())
                {
                    var done = await Services.Idempotency.FindAsync(db, record.Scope, key, record.CreatedAt);
                    if (done != null)
                    {
                        if (!Services.Idempotency.SameRequest(done, record))
                            return IdempotencyKeyReused();

                        Response.Headers[Services.Idempotency.ReplayedHeader] = "true";
                        return Ok(await OpenDoneStatusAsync(db, done));
                    }

                    var bUser = await db.Users.AsQueryable().Where(x => id != 0 && x.Shinden == id)
                        .Select(x => new { x.Id, x.PoolType, Cards = x.GameDeck.Cards.Count, x.GameDeck.MaxNumberOfCards }).FirstOrDefaultAsync();
                    if (bUser == null)
                    {
                        return "User not found".ToResponse(404);
                    }
                    if (bUser.Cards + packs.Sum(x => (long)x.CardCnt) > bUser.MaxNumberOfCards)
                    {
                        return "User has no space left in deck".ToResponse(406);
                    }
                    discordId = bUser.Id;
                    poolType = bUser.PoolType;
                }

                handedOff = true;
                RunInBackground(() => DrawAndSaveOnceAsync(record, packs, poolType, discordId, id, key, timer));

                return Accepted(new BoosterPackOpenStatus { Status = BoosterPackOpenStatus.Pending });
            }
            finally
            {
                if (!handedOff)
                    Services.Idempotency.End(record);
            }
        }

        protected virtual void RunInBackground(Func<Task> work) => _ = Task.Run(work);

        // drawing asks Shinden for every card, so it runs outside the executor and only the save waits in the queue
        private async Task DrawAndSaveOnceAsync(IdempotencyRecord record, List<BoosterPack> packs, CharacterPoolType poolType,
            ulong discordId, ulong id, string key, System.Diagnostics.Stopwatch timer)
        {
            var queued = false;
            try
            {
                var drawStart = timer.ElapsedMilliseconds;
                var cards = await DrawCardsAsync(packs, poolType);
                if (cards == null)
                {
                    Services.Idempotency.Fail(record, BoosterPackOpenStatus.ShindenUnavailable);
                    return;
                }
                var drawMs = timer.ElapsedMilliseconds - drawStart;

                var exe = new Executable($"api-packet-open u{discordId}", new Func<Task>(async () =>
                {
                    var committed = false;
                    try
                    {
                        using (var db = CreateDatabaseContext())
                        {
                            var botUser = await db.GetUserOrCreateSimpleAsync(discordId, _time);
                            if (await CountCardsAsync(db, discordId) + cards.Count > botUser.GameDeck.MaxNumberOfCards)
                            {
                                Services.Idempotency.Fail(record, BoosterPackOpenStatus.NoSpace);
                                return;
                            }

                            botUser.Stats.OpenedBoosterPacks += packs.Count;

                            await UpdateWishlistCountAsync(db, cards, botUser);

                            db.IdempotencyRecords.Add(record);

                            await db.SaveChangesAsync();
                            committed = true;

                            QueryCache.ExpireTag(new string[] { CacheTags.User(botUser.Id) });

                            // card ids exist only after the first save; if this one fails the cards are still granted once
                            record.Result = Services.Idempotency.ToResult(cards.Select(x => x.Id).ToList());
                            await db.SaveChangesAsync();

                            _logger?.Log($"API: opened {packs.Count} packs ({cards.Count} cards) s{id} u{discordId} key {key} in {timer.ElapsedMilliseconds}ms (draw {drawMs}ms)");

                            await Services.Idempotency.CleanupIfDueAsync(db, record.CreatedAt);
                        }
                    }
                    catch (Exception)
                    {
                        if (!committed)
                            Services.Idempotency.Fail(record, BoosterPackOpenStatus.Error);
                        throw;
                    }
                    finally
                    {
                        Services.Idempotency.End(record);
                    }
                }), discordId);

                if (!await _executor.TryAdd(exe, TimeSpan.FromSeconds(1)))
                {
                    _logger?.LogError($"API: opening packs s{id} u{discordId} key {key}: command queue is full");
                    Services.Idempotency.Fail(record, BoosterPackOpenStatus.Error);
                    return;
                }
                queued = true;
            }
            catch (Exception ex)
            {
                _logger?.LogError($"API: opening packs s{id} u{discordId} key {key}: {ex}");
                Services.Idempotency.Fail(record, BoosterPackOpenStatus.Error);
            }
            finally
            {
                if (!queued)
                    Services.Idempotency.End(record);
            }
        }

        private static async Task<BoosterPackOpenStatus> OpenDoneStatusAsync(Database.DatabaseContext db, IdempotencyRecord record)
        {
            var ids = Services.Idempotency.ReadResult<List<ulong>>(record) ?? new List<ulong>();
            var cards = await db.Cards.AsQueryable().Where(x => ids.Contains(x.Id)).Include(x => x.Tags).AsNoTracking().ToListAsync();

            return new BoosterPackOpenStatus
            {
                Status = BoosterPackOpenStatus.Done,
                CardIds = ids,
                Cards = ids.Select(x => cards.FirstOrDefault(c => c.Id == x)).Where(x => x != null).ToList(),
            };
        }

        /// <summary>Returns null when not every card could be drawn.</summary>
        protected virtual async Task<List<Card>> DrawCardsAsync(List<BoosterPack> packs, CharacterPoolType poolType)
        {
            var cards = new List<Card>();
            foreach (var pack in packs)
            {
                cards.AddRange(await _waifu.OpenBoosterPackAsync(null, pack, poolType));
            }

            return cards.Count < packs.Sum(x => x.CardCnt) ? null : cards;
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
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var currUser = ControllerContext.HttpContext.User;
            if (currUser.HasClaim(x => x.Type == "DiscordId"))
            {
                if (ulong.TryParse(currUser.Claims.First(x => x.Type == "DiscordId").Value, out var discordId))
                {
                    ulong packId = 0;
                    long drawMs = 0;
                    var opened = new List<Card>();
                    using (var db = new Database.DatabaseContext(_config))
                    {
                        var botUserCh = await db.Users.AsQueryable().Where(x => x.Id == discordId).Include(x => x.GameDeck).ThenInclude(x => x.BoosterPacks)
                            .ThenInclude(x => x.Characters).Include(x => x.GameDeck).ThenInclude(x => x.BoosterPacks).ThenInclude(x => x.RarityExcludedFromPack)
                            .AsNoTracking().AsSplitQuery().FirstOrDefaultAsync();
                        if (botUserCh == null)
                        {
                            return "User not found!".ToResponse(404);
                        }

                        var packs = Waifu.OrderPacksForNumbering(botUserCh.GameDeck.BoosterPacks);
                        if (packs.Count < packNumber || packNumber <= 0)
                        {
                            return "Boosterpack not found!".ToResponse(404);
                        }

                        var packCh = packs[packNumber - 1];
                        if (await CountCardsAsync(db, discordId) + packCh.CardCnt > botUserCh.GameDeck.MaxNumberOfCards)
                        {
                            return "User has no space left in deck!".ToResponse(406);
                        }

                        var drawStart = timer.ElapsedMilliseconds;
                        opened = await _waifu.OpenBoosterPackAsync(null, packCh, botUserCh.PoolType);
                        if (opened.Count < packCh.CardCnt)
                        {
                            return "Can't connect to shinden!".ToResponse(503);
                        }
                        drawMs = timer.ElapsedMilliseconds - drawStart;
                        packId = packCh.Id;
                    }

                    ObjectResult error = null;
                    var cards = new List<Card>();
                    var exe = new Executable($"api-packet-open u{discordId}", new Func<Task>(async () =>
                    {
                        using (var db = new Database.DatabaseContext(_config))
                        {
                            var botUser = await db.GetUserOrCreateSimpleAsync(discordId, _time);
                            var pack = await db.BoosterPacks.AsQueryable().Where(x => x.Id == packId && x.GameDeckId == discordId)
                                .Include(x => x.Characters).Include(x => x.RarityExcludedFromPack).AsSplitQuery().FirstOrDefaultAsync();
                            if (pack == null)
                            {
                                error = "Boosterpack already opened!".ToResponse(409);
                                return;
                            }

                            if (await CountCardsAsync(db, discordId) + opened.Count > botUser.GameDeck.MaxNumberOfCards)
                            {
                                error = "User has no space left in deck!".ToResponse(406);
                                return;
                            }

                            if (pack.CardSourceFromPack == CardSource.Activity || pack.CardSourceFromPack == CardSource.Migration)
                            {
                                botUser.Stats.OpenedBoosterPacksActivity += 1;
                            }
                            else
                            {
                                botUser.Stats.OpenedBoosterPacks += 1;
                            }

                            db.BoosterPacks.Remove(pack);

                            await UpdateWishlistCountAsync(db, opened, botUser);

                            await db.SaveChangesAsync();

                            QueryCache.ExpireTag(new string[] { CacheTags.User(botUser.Id) });
                            cards = opened;
                        }
                    }), discordId);

                    if (!await _executor.TryAdd(exe, TimeSpan.FromSeconds(1)))
                    {
                        return "Command queue is full".ToResponse(503);
                    }

                    if (!await exe.WaitAsync(Executable.ApiMaxWait))
                        return "Command timed out".ToResponse(504);

                    if (error != null)
                    {
                        return error;
                    }

                    _logger?.Log($"API: opened pack {packNumber} ({cards.Count} cards) u{discordId} in {timer.ElapsedMilliseconds}ms (draw {drawMs}ms)");
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

                            var botUser = await db.GetUserOrCreateSimpleAsync(discordId, _time);
                            var active = await db.Cards.AsQueryable().AsNoTracking().Where(x => x.Active && x.GameDeckId == discordId && x.Id != wid).ToListAsync();

                            thisCard.Active = !thisCard.Active;
                            if (thisCard.Active) active.Add(thisCard);

                            botUser.GameDeck.DeckPower = UserExtension.CombineCardPowers(active.Select(x => x.CalculateCardPower()));
                            botUser.GameDeck.CardsInDeck = active.Count;

                            await db.SaveChangesAsync();

                            QueryCache.ExpireTag(new string[] { CacheTags.User(botUser.Id) });
                            result = "Card status toggled".ToResponse(200);
                        }
                    }), discordId);

                    if (!await _executor.TryAdd(exe, TimeSpan.FromSeconds(1)))
                    {
                        return "Command queue is full".ToResponse(503);
                    }

                    if (!await exe.WaitAsync(Executable.ApiMaxWait))
                        return "Command timed out".ToResponse(504);
                    return result;
                }
            }
            return "The appropriate claim was not found".ToResponse(403);
        }

        // one cached list serves every filter, so anonymous callers cannot fill the cache with filter variants
        private async Task<FilteredCards> FilterCachedCardsAsync(IEnumerable<Card> all, CardsQueryFilter filter, uint offset, uint count)
        {
            var query = all.AsQueryable();
            if (!string.IsNullOrEmpty(filter.SearchText))
            {
                query = query.Where(x => ContainsText(x.Name, filter.SearchText) || ContainsText(x.Title, filter.SearchText));
            }

            query = CardsQueryFilter.Use(filter.OrderBy, query);
            query = FilterCardsByIds(query, filter);

            var cards = FilterCardsByTags(query, filter).ToList();
            var page = cards.Skip(ClampOffset(offset)).Take((int)Math.Min(count, MaxCardsPerRequest)).ToList();

            return new FilteredCards{TotalCards = cards.Count, Cards = await ToViewWithUsernamesAsync(page)};
        }

        // mirrors the case and accent insensitive LIKE of the database collation
        private static bool ContainsText(string value, string text)
            => value != null && CultureInfo.InvariantCulture.CompareInfo.IndexOf(value, text, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;

        private async Task<List<CardFinalView>> ToViewWithUsernamesAsync(List<Card> cards)
        {
            // a page can have dozens of owners, their names are looked up a few at a time instead of one by one
            var lookups = await Services.BoundedParallel.SelectAsync(cards.Select(x => x.GameDeck.User.Shinden).Distinct(), Services.BoundedParallel.ShindenLookups,
                async shindenId => (shindenId, Name: await GetUsernameAsync(shindenId)));
            var usernames = lookups.ToDictionary(x => x.shindenId, x => x.Name);
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

                // controllers are per request - after repeated failures skip further lookups so a card list does not wait for every owner
                if (_shindenFailures >= MaxShindenFailures)
                {
                    return _shinden.GetCachedUserName(shindenId) ?? string.Empty;
                }

                try
                {
                    return await _shinden.GetUserNameAsync(shindenId) ?? string.Empty;
                }
                catch (Exception)
                {
                    Interlocked.Increment(ref _shindenFailures);
                }
            }
            return string.Empty;
        }

        private static ObjectResult TryGetBoosterPacks(List<Models.CardBoosterPack> boosterPacks, out List<BoosterPack> packs)
        {
            packs = new List<BoosterPack>();
            if (boosterPacks.IsNullOrEmpty())
            {
                return "Model is Invalid".ToResponse(500);
            }

            if (boosterPacks.Count > MaxPacksPerRequest)
                return "Model is Invalid".ToResponse(500);

            foreach (var pack in boosterPacks)
            {
                if (pack == null || pack.Count < 1 || pack.Count > MaxCardsPerRequest)
                    return "Model is Invalid".ToResponse(500);

                if (!Enum.IsDefined(pack.Rarity) || (pack.RarityExcluded?.Any(x => !Enum.IsDefined(x)) ?? false))
                    return "Model is Invalid".ToResponse(500);

                if (pack.RarityExcluded != null && Waifu.DrawableRarities.All(pack.RarityExcluded.Contains))
                    return "Model is Invalid".ToResponse(500);

                var rPack = pack.ToRealPack();
                if (rPack != null) packs.Add(rPack);
            }

            if (packs.Count < 1)
            {
                return "Data is Invalid".ToResponse(500);
            }
            return null;
        }

        private async Task<(ObjectResult Error, List<BoosterPack> Packs)> GetBoosterPacksAsync(List<Models.CardBoosterPack> boosterPacks)
        {
            var invalid = TryGetBoosterPacks(boosterPacks, out var packs);
            if (invalid != null) return (invalid, packs);

            for (int i = 0; i < packs.Count; i++)
            {
                var pool = boosterPacks[i].Pool;
                if (pool?.Type != CardsPoolType.Season) continue;

                var key = pool.Season?.Trim().ToLowerInvariant();
                if (Services.Cache.SeasonSnapshot.TryParseKey(key, out var year, out var name))
                    key = Services.Cache.SeasonSnapshot.KeyOf(year, name);
                var current = string.IsNullOrEmpty(key);
                var season = current ? await _shinden.GetCurrentSeasonAsync() : _shinden.GetSeason(key);
                if (season == null || season.Characters.Count == 0)
                    return (current ? "Can't connect to shinden!".ToResponse(503) : "Season not found!".ToResponse(404), packs);

                packs[i].Season = season.Key;
            }
            return (null, packs);
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

            var owned = t.Count > 0 ? await db.GetOwnedCharactersAsync(user.Id) : new HashSet<ulong>();
            return Ok(await _waifu.GetCardsFromWishlistAsync(c, p, t, db, owned));
        }

        public static PhysicalFileResult CardImageFile(string path)
            => new PhysicalFileResult(System.IO.Path.GetFullPath(path), GetImageContentType(path));

        private static string GetImageContentType(string path)
            => new FileExtensionContentTypeProvider().TryGetContentType(path, out var type) ? type : "application/octet-stream";

        // the deck is loaded without its cards and packs, so new ones go through the sets
        private static void AddBoosterPacks(Database.DatabaseContext db, User user, List<BoosterPack> packs)
        {
            foreach (var pack in packs)
            {
                pack.GameDeck = user.GameDeck;
                db.BoosterPacks.Add(pack);
            }
        }

        private static Task<int> CountCardsAsync(Database.DatabaseContext db, ulong discordId)
            => db.Cards.AsQueryable().CountAsync(x => x.GameDeckId == discordId);

        private async Task UpdateWishlistCountAsync(Database.DatabaseContext db, List<Card> cards, User user)
        {
            var ids = cards.Select(x => x.Character).Distinct().ToList();
            var allWWCnt = await db.WishlistCountData.AsQueryable().AsNoTracking().Where(x => ids.Contains(x.Id)).ToListAsync();

            foreach (var card in cards)
            {
                card.Affection += user.GameDeck.AffectionFromKarma();
                card.FirstIdOwner = user.Id;

                var wwc = allWWCnt.FirstOrDefault(x => x.Id == card.Character);
                card.AWhoWantsCount = wwc?.ACount ?? 0;
                card.WhoWantsCount = wwc?.Count ?? 0;

                card.GameDeck = user.GameDeck;
                db.Cards.Add(card);
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

        private static ObjectResult ValidateFilter(CardsQueryFilter filter)
        {
            if (filter == null)
                return "Filter is required!".ToResponse(400);

            if (filter.SearchText != null && filter.SearchText.Length > MaxSearchTextLength)
                return $"Search text too long (max {MaxSearchTextLength})!".ToResponse(400);

            if (TooMany(filter.CardIds) || TooMany(filter.CharIds) || TooMany(filter.IncludeTags) || TooMany(filter.ExcludeTags))
                return $"Too many filter items (max {MaxFilterItems})!".ToResponse(400);

            return null;
        }

        private static bool TooMany<T>(List<T> list) => list != null && list.Count > MaxFilterItems;
    }
}