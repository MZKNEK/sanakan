#pragma warning disable 1591

using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sanakan.Config;
using Sanakan.Config.Model;
using Sanakan.Extensions;
using Sanakan.Services.Time;

namespace Sanakan.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UserKeyController : ControllerBase
    {
        private readonly ISystemTime _time;
        private readonly IConfig _config;

        public UserKeyController(IConfig config, ISystemTime time)
        {
            _config = config;
            _time = time;
        }

        /// <summary>
        /// Generuje nowy klucz dla użytkownika, poprzedni klucz tej aplikacji przestaje działać (wymagany nagłówek x-app-key)
        /// </summary>
        /// <param name="id">id użytkownika discorda</param>
        /// <param name="appKey">klucz aplikacji</param>
        /// <response code="401">App Key Not Provided</response>
        /// <response code="403">App Key Is Invalid or app has no UserKeys permission</response>
        /// <response code="404">User not found</response>
        [HttpPost("discord/{id}"), AllowAnonymous]
        public async Task<IActionResult> GenerateUserKeyAsync(ulong id, [FromHeader(Name = "x-app-key")]string appKey)
        {
            if (string.IsNullOrEmpty(appKey)) return "App Key Not Provided".ToResponse(401);

            var app = Authenticate(appKey);
            if (app == null) return "App Key Is Invalid".ToResponse(403);

            using (var db = new Database.DatabaseContext(_config))
            {
                if (!await db.Users.AsQueryable().AnyAsync(x => x.Id == id))
                    return "User not found!".ToResponse(404);

                var oldKeys = await db.UserApiKeys.AsQueryable().Where(x => x.UserId == id && x.Application == app).ToListAsync();
                db.UserApiKeys.RemoveRange(oldKeys);

                var key = UserKeyAuthenticationHandler.GenerateKey();
                var userKey = new Database.Models.UserApiKey
                {
                    UserId = id,
                    Application = app,
                    CreatedAt = _time.Now(),
                    KeyHash = UserKeyAuthenticationHandler.HashKey(key),
                };

                db.UserApiKeys.Add(userKey);
                await db.SaveChangesAsync();

                return Ok(new { key, userId = id.ToString(), created = userKey.CreatedAt });
            }
        }

        /// <summary>
        /// Unieważnia klucz użytkownika wygenerowany przez aplikację (wymagany nagłówek x-app-key)
        /// </summary>
        /// <param name="id">id użytkownika discorda</param>
        /// <param name="appKey">klucz aplikacji</param>
        /// <response code="401">App Key Not Provided</response>
        /// <response code="403">App Key Is Invalid or app has no UserKeys permission</response>
        /// <response code="404">Key not found</response>
        [HttpDelete("discord/{id}"), AllowAnonymous]
        public async Task<IActionResult> RevokeUserKeyAsync(ulong id, [FromHeader(Name = "x-app-key")]string appKey)
        {
            if (string.IsNullOrEmpty(appKey)) return "App Key Not Provided".ToResponse(401);

            var app = Authenticate(appKey);
            if (app == null) return "App Key Is Invalid".ToResponse(403);

            using (var db = new Database.DatabaseContext(_config))
            {
                var keys = await db.UserApiKeys.AsQueryable().Where(x => x.UserId == id && x.Application == app).ToListAsync();
                if (keys.Count == 0) return "Key not found!".ToResponse(404);

                db.UserApiKeys.RemoveRange(keys);
                await db.SaveChangesAsync();
            }

            return "Key revoked!".ToResponse(200);
        }

        /// <summary>
        /// Zwraca użytkownika, do którego należy klucz (wymagany nagłówek x-user-key lub Bearer od użytkownika)
        /// </summary>
        /// <response code="403">The appropriate claim was not found</response>
        [HttpGet("me"), Authorize(Policy = "Player")]
        public IActionResult WhoAmI()
        {
            var currUser = ControllerContext.HttpContext.User;
            var discordId = currUser.Claims.FirstOrDefault(x => x.Type == "DiscordId")?.Value;
            if (discordId == null) return "The appropriate claim was not found".ToResponse(403);

            return Ok(new { userId = discordId, application = currUser.Claims.FirstOrDefault(x => x.Type == "UserKeyApp")?.Value });
        }

        private string Authenticate(string appKey)
        {
            var app = AppKeyAuthenticationHandler.FindApp(_config, appKey);
            return app != null && app.Has(ApiAppPermission.UserKeys) ? app.Bearer : null;
        }
    }
}
