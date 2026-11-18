#pragma warning disable 1591

using System;
using System.Collections.Generic;
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
        /// <remarks>
        /// Filtry są opcjonalne i łączą się ze sobą; podsumowanie i partnerzy zawsze dotyczą całej historii,
        /// a strona i MatchingExchanges tylko wymian pasujących do filtrów.
        /// </remarks>
        /// <param name="offset">pominięte wymiany</param>
        /// <param name="count">liczba wymian na stronie (maks. 100)</param>
        /// <param name="partner">tylko wymiany z tym graczem (id na discordzie)</param>
        /// <param name="nick">tylko wymiany z graczem, którego nick na discordzie (obecny lub zapamiętany) albo na Shindenie zawiera ten tekst, bez względu na wielkość liter</param>
        /// <param name="card">tylko wymiany, w których przeszła ta karta (WID); wymiany odczytane z dawnych wiadomości, z ofertą ponad 12 kart, nie mają listy kart</param>
        /// <param name="since">tylko wymiany od tej chwili</param>
        /// <param name="before">tylko wymiany przed tą chwilą</param>
        /// <response code="403">The appropriate claim was not found</response>
        [HttpGet("exchanges"), ProducesResponseType(typeof(UserExchanges), 200)]
        public async Task<IActionResult> GetExchangesAsync([FromQuery] uint offset = 0, [FromQuery] uint count = 50,
            [FromQuery] ulong? partner = null, [FromQuery] string nick = null, [FromQuery] ulong? card = null,
            [FromQuery] DateTime? since = null, [FromQuery] DateTime? before = null)
        {
            var claim = HttpContext?.User?.Claims.FirstOrDefault(x => x.Type == "DiscordId");
            if (claim == null || !ulong.TryParse(claim.Value, out var discordId))
                return "The appropriate claim was not found".ToResponse(403);

            using var db = CreateDatabaseContext();
            var all = (await db.CardExchanges.AsNoTracking().Where(x => x.User1Id == discordId || x.User2Id == discordId).ToListAsync())
                .OrderByDescending(x => x.Date).ThenByDescending(x => x.Id).ToList();

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

            var withCard = card == null ? null
                : (await db.CardExchangeCards.AsNoTracking().Where(x => x.CardId == card).Select(x => x.ExchangeId).ToListAsync()).ToHashSet();
            nick = nick?.Trim();
            var matching = all.Where(x =>
            {
                var other = x.User1Id == discordId ? x.User2Id : x.User1Id;
                return (partner == null || other == partner)
                    && (string.IsNullOrEmpty(nick) || NickContains(nick, players[other].Nick, players[other].ShindenNick, storedNames.GetValueOrDefault(other)))
                    && (withCard == null || withCard.Contains(x.Id))
                    && (since == null || x.Date >= since)
                    && (before == null || x.Date < before);
            }).ToList();

            var page = matching.Skip((int)Math.Min(offset, int.MaxValue)).Take((int)Math.Min(count, MaxExchangesPerRequest)).ToList();
            var pageIds = page.Select(x => x.Id).ToList();
            var moved = await db.CardExchangeCards.AsNoTracking().Where(x => pageIds.Contains(x.ExchangeId)).ToListAsync();
            var cardIds = moved.Select(x => x.CardId).Distinct().ToList();
            var cards = await db.Cards.AsNoTracking().Where(x => cardIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
            foreach (var exchange in page)
                exchange.Cards = moved.Where(x => x.ExchangeId == exchange.Id).ToList();

            return Ok(UserExchanges.From(discordId, all, matching.Count, page, cards, id => players[id]));
        }

        private static bool NickContains(string text, params string[] names)
            => names.Any(x => x != null && x.Contains(text, StringComparison.OrdinalIgnoreCase));

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
