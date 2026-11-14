#pragma warning disable 1591

using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Sanakan.Extensions;
using Sanakan.Services;

namespace Sanakan.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ModerationController : ControllerBase
    {
        private readonly Moderator _moderator;

        public ModerationController(Moderator moderator)
        {
            _moderator = moderator;
        }

        /// <summary>
        /// Dane odwołania od wyciszenia
        /// </summary>
        public class MuteAppeal
        {
            /// <summary>
            /// Id serwera Discord (liczba albo tekst)
            /// </summary>
            [Required]
            public ulong? Guild { get; set; }
            /// <summary>
            /// Id użytkownika Discord (liczba albo tekst)
            /// </summary>
            [Required]
            public ulong? User { get; set; }
            /// <summary>
            /// Identyfikator odwołania nadany przez stronę, np. appeal:123; do 64 znaków ASCII bez spacji
            /// </summary>
            [Required]
            public string Key { get; set; }
        }

        /// <summary>
        /// Wynik operacji na odwołaniu
        /// </summary>
        public class MuteAppealStatus
        {
            public const string Ok = "ok";
            public const string None = "none";
            public const string Already = "already";

            /// <summary>
            /// ok, none albo already
            /// </summary>
            [JsonProperty("status")]
            public string Status { get; set; }
            /// <summary>
            /// Koniec kary (UTC): przy lift - kiedy miała się skończyć, przy restore - kiedy się teraz skończy
            /// </summary>
            [JsonProperty("end", NullValueHandling = NullValueHandling.Ignore)]
            public DateTime? End { get; set; }
            /// <summary>
            /// Tylko restore: true, gdy pozostały czas doliczono do wyciszenia, które osoba dostała w międzyczasie
            /// </summary>
            [JsonProperty("extended", NullValueHandling = NullValueHandling.Ignore)]
            public bool? Extended { get; set; }
        }

        /// <summary>
        /// Zdejmuje wyciszenie po przyjętym odwołaniu
        /// </summary>
        /// <remarks>
        /// Działa jak polecenie unmute: zdejmuje role wyciszenia i oddaje zabrane role. Karę zapamiętuje pod podanym kluczem,
        /// żeby restore mogło ją przywrócić na pozostały czas. Bot nie wysyła DM; wpis trafia do logu bota i na kanał powiadomień serwera.
        /// Status: ok - zdjęto, none - brak trwającego wyciszenia, already - ten klucz już zdjął karę (powtórka nic nie zmienia).
        /// </remarks>
        /// <response code="200">status: ok, none albo already; end - kiedy kara miała się skończyć</response>
        /// <response code="400">Brak pól albo nieprawidłowy key.</response>
        /// <response code="404">Bot nie zna tego serwera albo serwer nie jest skonfigurowany.</response>
        /// <response code="422">Ten key był użyty dla innej osoby lub innego serwera.</response>
        /// <response code="502">Discord nie pozwolił zdjąć ról; nic nie zapisano, można ponowić.</response>
        /// <response code="503">Bot właśnie sprawdza kary, ponów po czasie z nagłówka Retry-After.</response>
        [HttpPost("mute/lift"), Authorize(Policy = "Site")]
        public async Task<IActionResult> LiftMuteAsync([FromBody, Required] MuteAppeal appeal)
        {
            if (!Idempotency.IsValidKey(appeal.Key))
                return "Invalid key".ToResponse(400);

            MuteAppeals.Lift lift;
            try
            {
                lift = await _moderator.LiftMuteAfterAppealAsync(appeal.Guild.Value, appeal.User.Value, appeal.Key, Caller());
            }
            catch (TimeoutException)
            {
                return Busy();
            }
            catch (Discord.Net.HttpException ex)
            {
                return $"Discord refused: {ex.Reason ?? ex.Message}".ToResponse(502);
            }

            return lift.Result switch
            {
                MuteAppeals.Result.Ok => Ok(new MuteAppealStatus { Status = MuteAppealStatus.Ok, End = lift.End }),
                MuteAppeals.Result.Already => Ok(new MuteAppealStatus { Status = MuteAppealStatus.Already }),
                MuteAppeals.Result.None => Ok(new MuteAppealStatus { Status = MuteAppealStatus.None }),
                _ => Error(lift.Result),
            };
        }

        /// <summary>
        /// Przywraca zdjęte wyciszenie na pozostały czas
        /// </summary>
        /// <remarks>
        /// Działa tylko dla key, który wcześniej zdjął karę. Pozostały czas to czas od zdjęcia do pierwotnego końca kary;
        /// kara wraca z pierwotnym powodem i rolami. Jeśli osoba ma w tym czasie inne wyciszenie, pozostały czas jest do niego doliczany.
        /// Bot nie wysyła DM; wpis trafia do logu bota i na kanał powiadomień serwera.
        /// Status: ok - przywrócono, already - ten key już przywrócił karę, none - nie zostało nic do odbycia.
        /// </remarks>
        /// <response code="200">status: ok, none albo already; end - nowy koniec kary; extended - doliczono do trwającej kary</response>
        /// <response code="400">Brak pól albo nieprawidłowy key.</response>
        /// <response code="404">Nieznany key, bot nie zna tego serwera albo serwer nie jest skonfigurowany.</response>
        /// <response code="422">Ten key należy do innej osoby lub innego serwera.</response>
        /// <response code="503">Bot właśnie sprawdza kary, ponów po czasie z nagłówka Retry-After.</response>
        [HttpPost("mute/restore"), Authorize(Policy = "Site")]
        public async Task<IActionResult> RestoreMuteAsync([FromBody, Required] MuteAppeal appeal)
        {
            if (!Idempotency.IsValidKey(appeal.Key))
                return "Invalid key".ToResponse(400);

            MuteAppeals.Restore restore;
            try
            {
                restore = await _moderator.RestoreMuteAfterAppealAsync(appeal.Guild.Value, appeal.User.Value, appeal.Key, Caller());
            }
            catch (TimeoutException)
            {
                return Busy();
            }

            return restore.Result switch
            {
                MuteAppeals.Result.Ok => Ok(new MuteAppealStatus { Status = MuteAppealStatus.Ok, End = restore.End, Extended = restore.Extended }),
                MuteAppeals.Result.Already => Ok(new MuteAppealStatus { Status = MuteAppealStatus.Already }),
                MuteAppeals.Result.None => Ok(new MuteAppealStatus { Status = MuteAppealStatus.None }),
                _ => Error(restore.Result),
            };
        }

        private string Caller()
            => User.Claims.FirstOrDefault(x => x.Type == ClaimTypes.Webpage)?.Value
                ?? User.Claims.FirstOrDefault(x => x.Type == AppKeyAuthenticationHandler.AppClaim)?.Value
                ?? "strona";

        private IActionResult Busy()
        {
            Response.Headers["Retry-After"] = "5";
            return "Penalties are being checked, try again".ToResponse(503);
        }

        private static IActionResult Error(MuteAppeals.Result result) => result switch
        {
            MuteAppeals.Result.KeyMismatch => "Key was used for another user or guild".ToResponse(422),
            MuteAppeals.Result.UnknownKey => "Unknown key".ToResponse(404),
            _ => "Unknown or unconfigured guild".ToResponse(404),
        };
    }
}
