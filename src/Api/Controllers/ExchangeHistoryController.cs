#pragma warning disable 1591

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Discord.WebSocket;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sanakan.Api.Models;
using Sanakan.Config;
using Sanakan.Extensions;

namespace Sanakan.Api.Controllers
{
    /// <summary>
    /// Historia wymian gracza (wymagany x-user-key lub Bearer od użytkownika)
    /// </summary>
    [ApiController]
    [Route("api/waifu")]
    [Authorize(Policy = "Player")]
    public class ExchangeHistoryController : ControllerBase
    {
        public const int MaxExchangesPerRequest = 100;

        private const int MaxShindenFailures = 2;

        private readonly IConfig _config;
        private readonly DiscordSocketClient _client;
        private readonly Services.Shinden _shinden;
        private int _shindenFailures;

        public ExchangeHistoryController(IConfig config, DiscordSocketClient client, Services.Shinden shinden)
        {
            _config = config;
            _client = client;
            _shinden = shinden;
        }

        /// <summary>
        /// Pobiera własną historię wymian kart: podsumowanie, wszystkich partnerów i wybraną stronę wymian (od najnowszej)
        /// </summary>
        /// <param name="offset">pominięte wymiany</param>
        /// <param name="count">liczba wymian na stronie (maks. 100)</param>
        /// <response code="403">The appropriate claim was not found</response>
        [HttpGet("exchanges"), ProducesResponseType(typeof(UserExchanges), 200)]
        public async Task<IActionResult> GetExchangesAsync([FromQuery] uint offset = 0, [FromQuery] uint count = 50)
        {
            var claim = HttpContext?.User?.Claims.FirstOrDefault(x => x.Type == "DiscordId");
            if (claim == null || !ulong.TryParse(claim.Value, out var discordId))
                return "The appropriate claim was not found".ToResponse(403);

            using var db = CreateDatabaseContext();
            var all = (await db.CardExchanges.AsNoTracking().Where(x => x.User1Id == discordId || x.User2Id == discordId).ToListAsync())
                .OrderByDescending(x => x.Date).ThenByDescending(x => x.Id).ToList();

            var pageIds = all.Skip((int)Math.Min(offset, int.MaxValue)).Take((int)Math.Min(count, MaxExchangesPerRequest)).Select(x => x.Id).ToList();
            var moved = await db.CardExchangeCards.AsNoTracking().Where(x => pageIds.Contains(x.ExchangeId)).ToListAsync();
            var cardIds = moved.Select(x => x.CardId).Distinct().ToList();
            var cards = await db.Cards.AsNoTracking().Where(x => cardIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);

            var page = all.Where(x => pageIds.Contains(x.Id)).ToList();
            foreach (var exchange in page)
                exchange.Cards = moved.Where(x => x.ExchangeId == exchange.Id).ToList();

            var playerIds = all.SelectMany(x => new[] { x.User1Id, x.User2Id }).Append(discordId).Distinct().ToList();
            var shindenIds = await db.Users.AsNoTracking().Where(x => playerIds.Contains(x.Id) && x.Shinden != 0)
                .ToDictionaryAsync(x => x.Id, x => x.Shinden);
            var lookups = await Services.BoundedParallel.SelectAsync(shindenIds.Values.Distinct(), Services.BoundedParallel.ShindenLookups,
                async id => (id, Name: await GetShindenNickAsync(id)));
            var shindenNicks = lookups.ToDictionary(x => x.id, x => x.Name);
            var storedNames = await db.PlayerNames.AsNoTracking().Where(x => playerIds.Contains(x.UserId)).ToDictionaryAsync(x => x.UserId, x => x.Name);

            var players = playerIds.ToDictionary(id => id, id =>
            {
                var shinden = shindenIds.TryGetValue(id, out var sid) ? sid : 0;
                return new ExchangePlayer
                {
                    DiscordId = id.ToString(),
                    Nick = GetNick(id) ?? (storedNames.TryGetValue(id, out var stored) ? stored : null),
                    ShindenId = shinden == 0 ? null : shinden.ToString(),
                    ShindenNick = shinden == 0 ? null : shindenNicks[shinden],
                };
            });

            return Ok(UserExchanges.From(discordId, all, page, cards, id => players[id]));
        }

        // only the bot's cache: a long history can name hundreds of players, Discord is not asked about each
        protected virtual string GetNick(ulong id)
        {
            var user = (Discord.IUser)_client?.GetGuild(UserController.MainGuildId)?.GetUser(id) ?? _client?.GetUser(id);
            return user?.GetUserNickInGuild();
        }

        // after repeated failures the rest is taken from the cache, so a long history does not wait for every player
        protected virtual async Task<string> GetShindenNickAsync(ulong shindenId)
        {
            if (_shinden == null)
                return null;

            if (Volatile.Read(ref _shindenFailures) < MaxShindenFailures)
            {
                try
                {
                    return await _shinden.GetUserNameAsync(shindenId);
                }
                catch (Exception)
                {
                    Interlocked.Increment(ref _shindenFailures);
                }
            }
            return _shinden.GetCachedUserName(shindenId);
        }

        protected virtual Database.DatabaseContext CreateDatabaseContext() => new Database.DatabaseContext(_config);
    }
}
