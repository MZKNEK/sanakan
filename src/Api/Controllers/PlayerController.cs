#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sanakan.Api.Models;
using Sanakan.Config;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services.Executor;
using Sanakan.Services.PocketWaifu;
using Sanakan.Services.Time;
using Z.EntityFramework.Plus;

namespace Sanakan.Api.Controllers
{
    /// <summary>
    /// Operacje gracza na własnym koncie (wymagany x-user-key lub Bearer od użytkownika)
    /// </summary>
    [ApiController]
    [Route("api/waifu")]
    [Authorize(Policy = "Player")]
    public class PlayerController : ControllerBase
    {
        public const int MaxTagNameLength = 100;
        public const int MaxExchangeConditionsLength = 2000;
        public const int MaxCardsPerOperation = 1000;

        private static readonly TagType[] _defaultTags = new[]
        {
            TagType.Favorite, TagType.Gallery, TagType.Reservation, TagType.Exchange, TagType.TrashBin
        };

        private readonly IConfig _config;
        private readonly ISystemTime _time;
        private readonly IExecutor _executor;
        private readonly TagHelper _tags;

        public PlayerController(IConfig config, ISystemTime time, IExecutor executor, TagHelper tags)
        {
            _config = config;
            _time = time;
            _executor = executor;
            _tags = tags;
        }

        /// <summary>
        /// Pobiera listę pakietów użytkownika
        /// </summary>
        /// <returns>pakiety, numer odpowiada numerowi w api/waifu/boosterpack/open/{numer}</returns>
        [HttpGet("boosterpacks"), ProducesResponseType(typeof(List<UserBoosterPack>), 200)]
        public Task<IActionResult> GetUserBoosterPacksAsync()
            => WithCachedPlayerAsync(user => user.GameDeck.BoosterPacks.Select((x, i) => UserBoosterPack.From(x, i + 1)).ToList());

        /// <summary>
        /// Pobiera listę przedmiotów użytkownika
        /// </summary>
        /// <returns>przedmioty, numer odpowiada numerowi w poleceniach bota</returns>
        [HttpGet("items"), ProducesResponseType(typeof(List<UserItem>), 200)]
        public Task<IActionResult> GetUserItemsAsync()
            => WithCachedPlayerAsync(user => user.GetAllItems().Select((x, i) => UserItem.From(x, i + 1)).ToList());

        /// <summary>
        /// Pobiera postęp misji dziennych i tygodniowych
        /// </summary>
        [HttpGet("missions"), ProducesResponseType(typeof(UserMissions), 200)]
        public Task<IActionResult> GetMissionsAsync()
            => WithCachedPlayerAsync(user => UserMissions.From(user, _time.Now()));

        /// <summary>
        /// Pobiera dzienne limity i czasy odnowienia (drobne, zaskórniaki, karta+, rynek, PvP, pakiety, druciarstwo)
        /// </summary>
        [HttpGet("limits"), ProducesResponseType(typeof(UserLimits), 200)]
        public Task<IActionResult> GetLimitsAsync()
            => WithCachedPlayerAsync(user => UserLimits.From(user, _time.Now(), _config.Get().PacksPerDay));

        /// <summary>
        /// Pobiera aktywną talię, jej moc i informację czy można grać w PvP
        /// </summary>
        [HttpGet("deck"), ProducesResponseType(typeof(UserDeck), 200)]
        public Task<IActionResult> GetDeckAsync()
            => WithCachedPlayerAsync(user => UserDeck.From(user, _time.Now()));

        /// <summary>
        /// Pobiera własną listę życzeń (również gdy jest prywatna)
        /// </summary>
        [HttpGet("wishlist"), ProducesResponseType(typeof(UserWishlist), 200)]
        public Task<IActionResult> GetWishlistAsync()
            => WithCachedPlayerAsync(user => UserWishlist.From(user.GameDeck));

        /// <summary>
        /// Pobiera figurki i ich części
        /// </summary>
        [HttpGet("figures"), ProducesResponseType(typeof(List<UserFigure>), 200)]
        public Task<IActionResult> GetFiguresAsync()
            => WithCachedPlayerAsync(user => user.GameDeck.Figures.Select(UserFigure.From).ToList());

        /// <summary>
        /// Pobiera własne i domyślne oznaczenia wraz z liczbą oznaczonych kart
        /// </summary>
        [HttpGet("tags"), ProducesResponseType(typeof(UserTags), 200)]
        public async Task<IActionResult> GetTagsAsync()
        {
            if (!TryGetDiscordId(out var discordId))
                return NoClaim();

            using (var db = new Database.DatabaseContext(_config))
            {
                var user = await db.GetCachedFullUserAsync(discordId);
                if (user == null)
                    return "User not found!".ToResponse(404);

                var custom = await db.Tags.AsQueryable().AsNoTracking().Where(x => x.GameDeckId == discordId).ToListAsync();
                var ordered = user.GameDeck.TagsOrder == TagsOrder.Alphabetically
                    ? custom.OrderBy(x => x.Name).ThenBy(x => x.Id) : custom.OrderBy(x => x.Id);

                var counts = user.GameDeck.Cards.SelectMany(x => x.Tags).GroupBy(x => x.Id).ToDictionary(x => x.Key, x => (long)x.Count());
                long CountOf(ulong id) => counts.TryGetValue(id, out var c) ? c : 0;

                return Ok(new UserTags
                {
                    Custom = ordered.Select(x => new UserTag { Id = x.Id, Name = x.Name, CardCount = CountOf(x.Id) }).ToList(),
                    Default = _defaultTags.Select(x => _tags.GetTag(x)).Select(x => new UserTag { Id = x.Id, Name = x.Name, CardCount = CountOf(x.Id) }).ToList(),
                    Max = user.GameDeck.MaxNumberOfTags,
                    Order = user.GameDeck.TagsOrder,
                });
            }
        }

        /// <summary>
        /// Tworzy własne oznaczenie
        /// </summary>
        /// <param name="name">nazwa (bez spacji)</param>
        /// <response code="400">Invalid tag name</response>
        /// <response code="409">Tag already exists / too similar to default tag</response>
        /// <response code="406">Tag limit reached</response>
        [HttpPost("tags"), ProducesResponseType(typeof(UserTag), 201)]
        public Task<IActionResult> CreateTagAsync([FromBody] string name)
        {
            var invalid = ValidateTagName(name);
            if (invalid != null) return Task.FromResult(invalid);

            return RunAsPlayerAsync("tag-create", async (db, discordId) =>
            {
                var buser = await db.GetUserOrCreateSimpleAsync(discordId);
                if (buser.GameDeck.MaxNumberOfTags <= buser.GameDeck.Tags.Count)
                    return "Tag limit reached!".ToResponse(406);

                if (_tags.IsSimilar(name))
                    return "Tag is too similar to default tag!".ToResponse(409);

                if (buser.GameDeck.Tags.Any(x => x.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase)))
                    return "Tag already exists!".ToResponse(409);

                var tag = new Tag { Name = name };
                buser.GameDeck.Tags.Add(tag);
                await db.SaveChangesAsync();

                return StatusCode(201, new UserTag { Id = tag.Id, Name = tag.Name, CardCount = 0 });
            });
        }

        /// <summary>
        /// Zmienia nazwę własnego oznaczenia
        /// </summary>
        /// <param name="id">id oznaczenia</param>
        /// <param name="name">nowa nazwa (bez spacji)</param>
        /// <response code="400">Invalid tag name</response>
        /// <response code="404">Tag not found</response>
        /// <response code="409">Tag already exists / too similar to default tag</response>
        [HttpPut("tags/{id}")]
        public Task<IActionResult> RenameTagAsync(ulong id, [FromBody] string name)
        {
            var invalid = ValidateTagName(name);
            if (invalid != null) return Task.FromResult(invalid);

            return RunAsPlayerAsync("tag-rename", async (db, discordId) =>
            {
                var buser = await db.GetUserOrCreateSimpleAsync(discordId);
                var tag = buser.GameDeck.Tags.FirstOrDefault(x => x.Id == id);
                if (tag == null)
                    return "Tag not found!".ToResponse(404);

                if (_tags.IsSimilar(name))
                    return "Tag is too similar to default tag!".ToResponse(409);

                if (buser.GameDeck.Tags.Any(x => x.Id != id && x.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase)))
                    return "Tag already exists!".ToResponse(409);

                tag.Name = name;
                await db.SaveChangesAsync();

                return "Tag renamed!".ToResponse(200);
            });
        }

        /// <summary>
        /// Usuwa własne oznaczenie (znika też ze wszystkich kart)
        /// </summary>
        /// <param name="id">id oznaczenia</param>
        /// <response code="404">Tag not found</response>
        [HttpDelete("tags/{id}")]
        public Task<IActionResult> DeleteTagAsync(ulong id)
            => RunAsPlayerAsync("tag-delete", async (db, discordId) =>
            {
                var buser = await db.GetUserOrCreateSimpleAsync(discordId);
                var tag = buser.GameDeck.Tags.FirstOrDefault(x => x.Id == id);
                if (tag == null)
                    return "Tag not found!".ToResponse(404);

                buser.GameDeck.Tags.Remove(tag);
                await db.SaveChangesAsync();

                return "Tag deleted!".ToResponse(200);
            });

        /// <summary>
        /// Ustawia sortowanie własnych oznaczeń
        /// </summary>
        /// <param name="order">id lub alphabetically</param>
        [HttpPut("tags/order")]
        public Task<IActionResult> SetTagsOrderAsync([FromBody] TagsOrder order)
            => RunAsPlayerAsync("tag-order", async (db, discordId) =>
            {
                var buser = await db.GetUserOrCreateSimpleAsync(discordId);
                buser.GameDeck.TagsOrder = order;
                await db.SaveChangesAsync();

                return "Tags order changed!".ToResponse(200);
            });

        /// <summary>
        /// Dodaje oznaczenie (własne lub domyślne) do kart
        /// </summary>
        /// <param name="id">id oznaczenia</param>
        /// <param name="wids">WID kart</param>
        /// <returns>liczba oznaczonych kart</returns>
        /// <response code="400">No cards / too many cards</response>
        /// <response code="404">Tag not found</response>
        [HttpPost("tags/{id}/cards")]
        public Task<IActionResult> AddTagToCardsAsync(ulong id, [FromBody] List<ulong> wids)
            => ChangeCardsTagAsync(id, wids, true);

        /// <summary>
        /// Usuwa oznaczenie (własne lub domyślne) z kart
        /// </summary>
        /// <param name="id">id oznaczenia</param>
        /// <param name="wids">WID kart</param>
        /// <returns>liczba kart, z których usunięto oznaczenie</returns>
        /// <response code="400">No cards / too many cards</response>
        /// <response code="404">Tag not found</response>
        [HttpPost("tags/{id}/cards/remove")]
        public Task<IActionResult> RemoveTagFromCardsAsync(ulong id, [FromBody] List<ulong> wids)
            => ChangeCardsTagAsync(id, wids, false);

        /// <summary>
        /// Ustawia zasady wymiany (pusty tekst lub null kasuje zasady)
        /// </summary>
        /// <param name="conditions">zasady wymiany</param>
        /// <response code="400">Text too long</response>
        [HttpPut("exchange-conditions")]
        public Task<IActionResult> SetExchangeConditionsAsync([FromBody] string conditions)
        {
            if (conditions?.Length > MaxExchangeConditionsLength)
                return Task.FromResult<IActionResult>("Text too long!".ToResponse(400));

            return RunAsPlayerAsync("exchange-conditions", async (db, discordId) =>
            {
                var buser = await db.GetUserOrCreateSimpleAsync(discordId);
                buser.GameDeck.ExchangeConditions = string.IsNullOrWhiteSpace(conditions) ? null : conditions;
                await db.SaveChangesAsync();

                return "Exchange conditions changed!".ToResponse(200);
            });
        }

        /// <summary>
        /// Ustawia kolejność kart w galerii
        /// </summary>
        /// <param name="wids">WID kart w kolejności wyświetlania</param>
        /// <response code="400">No cards / too many cards</response>
        [HttpPut("gallery/order")]
        public Task<IActionResult> SetGalleryOrderAsync([FromBody] List<ulong> wids)
        {
            var invalid = ValidateCardList(wids);
            if (invalid != null) return Task.FromResult(invalid);

            return RunAsPlayerAsync("gallery-order", async (db, discordId) =>
            {
                var buser = await db.GetUserOrCreateSimpleAsync(discordId);
                buser.GameDeck.GalleryOrderedIds = string.Join(" ", wids);
                await db.SaveChangesAsync();

                return "Gallery order changed!".ToResponse(200);
            });
        }

        /// <summary>
        /// Ustawia ulubioną postać na profilu (0 - reset)
        /// </summary>
        /// <param name="wid">WID posiadanej karty z postacią (karta nie może być w klatce)</param>
        /// <response code="404">Card not found or in cage</response>
        /// <response code="409">Character already set</response>
        [HttpPut("waifu/{wid}")]
        public Task<IActionResult> SetWaifuAsync(ulong wid)
            => RunAsPlayerAsync("waifu", async (db, discordId) =>
            {
                var buser = await db.GetUserOrCreateAsync(discordId);
                if (wid == 0)
                {
                    if (buser.GameDeck.Waifu != 0)
                    {
                        LowerPreviousWaifusAffection(buser);
                        buser.GameDeck.Waifu = 0;
                        buser.GameDeck.PremiumWaifu = 0;
                        await db.SaveChangesAsync();
                    }
                    return "Waifu reset!".ToResponse(200);
                }

                var card = buser.GameDeck.Cards.FirstOrDefault(x => x.Id == wid && !x.InCage);
                if (card == null)
                    return "Card not found or in cage!".ToResponse(404);

                if (buser.GameDeck.Waifu == card.Character)
                    return "Character already set!".ToResponse(409);

                LowerPreviousWaifusAffection(buser);
                buser.GameDeck.Waifu = card.Character;
                buser.GameDeck.PremiumWaifu = 0;
                await db.SaveChangesAsync();

                return "Waifu changed!".ToResponse(200);
            });

        private static void LowerPreviousWaifusAffection(User buser)
        {
            foreach (var card in buser.GameDeck.Cards.Where(x => x.Character == buser.GameDeck.Waifu))
            {
                card.Affection -= 5;
                if (buser.GameDeck.PremiumWaifu != 0)
                    card.Affection -= 50;

                _ = card.CalculateCardPower();
            }
        }

        private Task<IActionResult> ChangeCardsTagAsync(ulong tagId, List<ulong> wids, bool add)
        {
            var invalid = ValidateCardList(wids);
            if (invalid != null) return Task.FromResult(invalid);

            return RunAsPlayerAsync(add ? "tag-add" : "tag-remove", async (db, discordId) =>
            {
                var isDefault = _defaultTags.Any(x => _tags.GetTagId(x) == tagId);
                var tag = await db.Tags.AsQueryable().FirstOrDefaultAsync(x => x.Id == tagId && (isDefault || x.GameDeckId == discordId));
                if (tag == null)
                    return "Tag not found!".ToResponse(404);

                var cards = await db.Cards.AsQueryable().Where(x => x.GameDeckId == discordId).Include(x => x.Tags)
                    .Where(x => wids.Contains(x.Id) && x.Tags.Any(t => t.Id == tagId) != add).ToListAsync();

                foreach (var card in cards)
                {
                    if (add) card.Tags.Add(tag);
                    else card.Tags.Remove(tag);
                }

                await db.SaveChangesAsync();
                return Ok(new { count = cards.Count });
            });
        }

        public static IActionResult ValidateTagName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Any(char.IsWhiteSpace) || name.Length > MaxTagNameLength)
                return $"Invalid tag name (1-{MaxTagNameLength} characters, no spaces)!".ToResponse(400);

            return null;
        }

        public static IActionResult ValidateCardList(List<ulong> wids)
        {
            if (wids == null || wids.Count < 1)
                return "No cards provided!".ToResponse(400);

            if (wids.Count > MaxCardsPerOperation)
                return $"Too many cards (max {MaxCardsPerOperation})!".ToResponse(400);

            return null;
        }

        private async Task<IActionResult> WithCachedPlayerAsync<T>(Func<User, T> map)
        {
            if (!TryGetDiscordId(out var discordId))
                return NoClaim();

            using (var db = new Database.DatabaseContext(_config))
            {
                var user = await db.GetCachedFullUserAsync(discordId);
                if (user == null)
                    return "User not found!".ToResponse(404);

                return Ok(map(user));
            }
        }

        private async Task<IActionResult> RunAsPlayerAsync(string name, Func<Database.DatabaseContext, ulong, Task<IActionResult>> action)
        {
            if (!TryGetDiscordId(out var discordId))
                return NoClaim();

            IActionResult result = null;
            var exe = new Executable($"api-{name} u{discordId}", new Func<Task>(async () =>
            {
                using (var db = new Database.DatabaseContext(_config))
                {
                    if (!await db.Users.AsQueryable().AnyAsync(x => x.Id == discordId))
                    {
                        result = "User not found!".ToResponse(404);
                        return;
                    }

                    result = await action(db, discordId);
                    QueryCacheManager.ExpireTag(new string[] { CacheTags.User(discordId) });
                }
            }), discordId);

            if (!await _executor.TryAdd(exe, TimeSpan.FromSeconds(1)))
                return "Command queue is full".ToResponse(503);

            await exe.WaitAsync();
            return result;
        }

        private bool TryGetDiscordId(out ulong discordId)
        {
            discordId = 0;
            var claim = HttpContext?.User?.Claims.FirstOrDefault(x => x.Type == "DiscordId");
            return claim != null && ulong.TryParse(claim.Value, out discordId);
        }

        private static IActionResult NoClaim() => "The appropriate claim was not found".ToResponse(403);
    }
}
