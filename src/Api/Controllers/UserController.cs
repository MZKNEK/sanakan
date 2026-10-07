#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Discord.WebSocket;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using Sanakan.Api.Models;
using Sanakan.Config;
using Sanakan.Extensions;
using Sanakan.Services.Executor;
using Sanakan.Services.Time;
using Shinden;
using Shinden.Logger;
using Z.EntityFramework.Plus;

namespace Sanakan.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UserController : ControllerBase
    {
        private const ulong MainGuildId = 245931283031523330;

        private readonly IConfig _config;
        private readonly ILogger _logger;
        private readonly ISystemTime _time;
        private readonly IExecutor _executor;
        private readonly ShindenClient _shClient;
        private readonly IMemoryCache _nameCache;
        private readonly DiscordSocketClient _client;

        public UserController(DiscordSocketClient client, ShindenClient shClient, ILogger logger,
            IExecutor executor, IConfig config, ISystemTime time, IMemoryCache cache)
        {
            _time = time;
            _config = config;
            _client = client;
            _logger = logger;
            _nameCache = cache;
            _executor = executor;
            _shClient = shClient;
        }

        /// <summary>
        /// Pobieranie użytkownika bota
        /// </summary>
        /// <param name="id">id użytkownika discorda</param>
        /// <returns>użytkownik bota</returns>
        [HttpGet("discord/{id}"), Authorize(Policy = "Site")]
        public async Task<ActionResult<Database.Models.User>> GetUserByDiscordIdAsync(ulong id)
        {
            using (var db = new Database.DatabaseContext(_config))
            {
                return await db.GetCachedFullUserAsync(id);
            }
        }

        /// <summary>
        /// Wyszukuje id użytkownika na shinden
        /// </summary>
        /// <param name="name">nazwa użytkownika</param>
        /// <returns>id użytkownika</returns>
        [HttpPost("find")]
        public async Task<ActionResult<IEnumerable<Shinden.Models.IUserSearch>>> GetUserIdByNameAsync([FromBody, Required]string name)
        {
            var res = await _shClient.Search.UserAsync(name);
            if (!res.IsSuccessStatusCode())
            {
                return "User not found!".ToResponse(404);
            }
            return res.Body;
        }

        /// <summary>
        /// Pobiera nazwę użytkownika z shindena
        /// </summary>
        /// <param name="id">id użytkownika shindena</param>
        /// <returns>nazwa użytkownika</returns>
        [HttpGet("shinden/{id}/username")]
        public async Task<ActionResult<string>> GetShindenUsernameByShindenId(ulong id)
        {
            if (_nameCache.TryGetValue(id, out string username))
            {
                return username;
            }

            var res = await _shClient.User.GetAsync(id);
            if (!res.IsSuccessStatusCode())
            {
                return "User not found!".ToResponse(404);
            }

            _nameCache.Set(id, res.Body.Name, new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromHours(12)));

            return res.Body.Name;
        }

        /// <summary>
        /// Pobieranie użytkownika bota
        /// </summary>
        /// <param name="id">id użytkownika shindena</param>
        /// <returns>użytkownik bota</returns>
        [HttpGet("shinden/{id}"), Authorize(Policy = "Site")]
        public async Task<ActionResult<UserWithToken>> GetUserByShindenIdAsync(ulong id)
        {
            using (var db = new Database.DatabaseContext(_config))
            {
                var user = await db.GetCachedFullUserByShindenIdAsync(id);
                if (user == null)
                {
                    return "User not found!".ToResponse(404);
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
        /// Pobieranie użytkownika bota ze zmniejszoną ilością danych
        /// </summary>
        /// <param name="id">id użytkownika shindena</param>
        /// <returns>użytkownik bota</returns>
        [HttpGet("shinden/simple/{id}"), Authorize(Policy = "Site")]
        public async Task<ActionResult<UserWithToken>> GetUserByShindenIdSimpleAsync(ulong id)
        {
            using (var db = new Database.DatabaseContext(_config))
            {
                var user = await db.Users.AsQueryable().AsSplitQuery().Where(x => x.Shinden == id).Include(x => x.GameDeck).AsNoTracking().FirstOrDefaultAsync();
                if (user == null)
                {
                    return "User not found!".ToResponse(404);
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
        /// Pobiera uprawnienia użytkownika na serwerze: dev, tester, admin, moderator itp. (token strony lub nagłówek x-app-key z uprawnieniem Info)
        /// </summary>
        /// <param name="id">id użytkownika discorda</param>
        /// <param name="guildId">id serwera, domyślnie główny serwer</param>
        /// <response code="404">Guild not found</response>
        [HttpGet("discord/{id}/permissions"), Authorize(Policy = "Info")]
        public async Task<ActionResult<UserPermissions>> GetUserPermissionsByDiscordIdAsync(ulong id, [FromQuery] ulong? guildId)
        {
            return await GetUserPermissionsAsync(id, guildId ?? MainGuildId);
        }

        /// <summary>
        /// Pobiera uprawnienia użytkownika na serwerze: dev, tester, admin, moderator itp. (token strony lub nagłówek x-app-key z uprawnieniem Info)
        /// </summary>
        /// <param name="id">id użytkownika shindena</param>
        /// <param name="guildId">id serwera, domyślnie główny serwer</param>
        /// <response code="404">User or guild not found</response>
        [HttpGet("shinden/{id}/permissions"), Authorize(Policy = "Info")]
        public async Task<ActionResult<UserPermissions>> GetUserPermissionsByShindenIdAsync(ulong id, [FromQuery] ulong? guildId)
        {
            using (var db = new Database.DatabaseContext(_config))
            {
                var discordId = await db.Users.AsQueryable().Where(x => x.Shinden == id).AsNoTracking().Select(x => (ulong?)x.Id).FirstOrDefaultAsync();
                if (discordId == null)
                {
                    return "User not found!".ToResponse(404);
                }

                return await GetUserPermissionsAsync(discordId.Value, guildId ?? MainGuildId);
            }
        }

        private async Task<ActionResult<UserPermissions>> GetUserPermissionsAsync(ulong userId, ulong guildId)
        {
            var guild = _client.GetGuild(guildId);
            if (guild == null)
            {
                return "Guild not found!".ToResponse(404);
            }

            var permissions = new UserPermissions
            {
                DiscordId = userId.ToString(),
                GuildId = guildId.ToString(),
                Dev = _config.Get().Dev?.Any(x => x == userId) ?? false
            };

            var user = guild.GetUser(userId);
            if (user == null) return permissions;

            using (var db = new Database.DatabaseContext(_config))
            {
                var gConfig = await db.GetCachedGuildFullConfigAsync(guildId);
                bool hasRole(ulong roleId) => user.Roles.Any(x => x.Id == roleId);

                permissions.OnGuild = true;
                permissions.Admin = user.GuildPermissions.Administrator || (gConfig != null && hasRole(gConfig.AdminRole));
                if (gConfig == null)
                {
                    permissions.User = true;
                    return permissions;
                }

                permissions.Tester = hasRole(gConfig.TesterRole);
                permissions.SemiAdmin = hasRole(gConfig.SemiAdminRole);
                permissions.Moderator = gConfig.ModeratorRoles.Any(x => hasRole(x.Role));
                permissions.User = permissions.Admin || guild.GetRole(gConfig.UserRole) == null || hasRole(gConfig.UserRole);
            }

            return permissions;
        }

        /// <summary>
        /// Zmienia użytkownikowi shindena nick
        /// </summary>
        /// <param name="id">id użytkownika shindena</param>
        /// <param name="nickname">ksywka użytkownika</param>
        /// <response code="404">User not found</response>
        [HttpPost("shinden/{id}/nickname"), Authorize(Policy = "Site")]
        public async Task<IActionResult> ChangeNicknameShindenUserAsync(ulong id, [FromBody, Required]string nickname)
        {
            using (var db = new Database.DatabaseContext(_config))
            {
                var user = await db.Users.AsQueryable().AsSplitQuery().Where(x => x.Shinden == id).AsNoTracking().FirstOrDefaultAsync();
                if (user == null)
                {
                    return "User not found!".ToResponse(404);
                }

                var guild = _client.GetGuild(MainGuildId);
                if (guild == null)
                {
                    return "Guild not found!".ToResponse(404);
                }

                var userOnGuild = guild.GetUser(user.Id);
                if (userOnGuild == null)
                {
                    return "User not found!".ToResponse(404);
                }

                await userOnGuild.ModifyAsync(x => x.Nickname = nickname);
            }

            return "User nickname changed!".ToResponse(200);
        }

        /// <summary>
        /// Pełne łączenie użytkownika
        /// </summary>
        /// <param name="id">relacja</param>
        /// <response code="403">Can't connect to shinden!</response>
        /// <response code="404">User not found</response>
        /// <response code="500">Model is invalid!</response>
        [HttpPut("register"), Authorize(Policy = "Site")]
        public async Task<IActionResult> RegisterUserAsync([FromBody, Required]UserRegistration id)
        {
            if (id == null)
            {
                return "Model is Invalid!".ToResponse(500);
            }

            var user = _client.GetUser(id.DiscordUserId);
            if (user == null)
            {
                return "User not found!".ToResponse(404);
            }

            using (var db = new Database.DatabaseContext(_config))
            {
                var botUser = db.Users.FirstOrDefault(x => x.Id == id.DiscordUserId);
                if (botUser != null)
                {
                    if (botUser.Shinden != 0)
                    {
                        return "User already connected!".ToResponse(404);
                    }
                }

                var response = await _shClient.Search.UserAsync(id.Username);
                if (!response.IsSuccessStatusCode())
                {
                    return "Can't connect to shinden!".ToResponse(403);
                }

                var found = PickShindenUser(response.Body, id.Username);
                if (found == null)
                {
                    return "User not found on shinden!".ToResponse(404);
                }

                var sResponse = await _shClient.User.GetAsync(found);
                var sUser = sResponse.IsSuccessStatusCode() ? sResponse.Body : null;
                if (sUser?.ForumId == null || sUser.ForumId.Value != id.ForumUserId)
                {
                    return "Something went wrong!".ToResponse(500);
                }

                if (db.Users.Any(x => x.Shinden == sUser.Id))
                {
                    var oldUsers = await db.Users.AsQueryable().Where(x => x.Shinden == sUser.Id && x.Id != id.DiscordUserId).ToListAsync();

                    if (oldUsers.Count > 0)
                    {
                        var rmcs = _config.Get().RMConfig.Where(x => x.Type == RichMessageType.AdminNotify);
                        foreach (var rmc in rmcs)
                        {
                            var guild = _client.GetGuild(rmc.GuildId);
                            if (guild == null) continue;

                            var channel = guild.GetTextChannel(rmc.ChannelId);
                            if (channel == null) continue;

                            await channel.SendMessageAsync("", embed: ($"Potencjalne multikonto:\nDID: {id.DiscordUserId} <@{id.DiscordUserId}>\nSID: {sUser.Id}\n"
                                + $"SN: {sUser.Name}\n\noDID: {string.Join(",", oldUsers.Select(x => $"{x.Id} <@{x.Id}>"))}").TrimToLength().ToEmbedMessage(EMType.Error).Build());
                        }
                    }
                    return "This account is already linked!".ToResponse(401);
                }

                var exe = new Executable($"api-register u{id.DiscordUserId}", new Func<Task>(async () =>
                {
                    using (var dbs = new Database.DatabaseContext(_config))
                    {
                        botUser = await dbs.GetUserOrCreateSimpleAsync(id.DiscordUserId);
                        botUser.Shinden = sUser.Id;

                        await dbs.SaveChangesAsync();

                        QueryCacheManager.ExpireTag(new string[] { CacheTags.User(user.Id) });
                    }
                }), id.DiscordUserId, Priority.High);

                if (!await _executor.TryAdd(exe, TimeSpan.FromSeconds(1)))
                {
                    return "Command queue is full".ToResponse(503);
                }

                return "User connected!".ToResponse(200);
            }
        }

        public static Shinden.Models.IUserSearch PickShindenUser(IEnumerable<Shinden.Models.IUserSearch> results, string username)
        {
            if (results == null) return null;

            var list = results.Where(x => x != null).ToList();
            return list.FirstOrDefault(x => string.Equals(x.Name, username, StringComparison.OrdinalIgnoreCase))
                ?? list.FirstOrDefault();
        }

        /// <summary>
        /// Zmiana ilości punktów TC użytkownika
        /// </summary>
        /// <param name="id">id użytkownika discorda</param>
        /// <param name="value">liczba TC</param>
        /// <response code="404">User not found</response>
        [HttpPut("discord/{id}/tc"), Authorize(Policy = "Site")]
        public async Task<IActionResult> ModifyPointsTCDiscordAsync(ulong id, [FromBody, Required]long value)
        {
            using (var db = new Database.DatabaseContext(_config))
            {
                var user = db.Users.FirstOrDefault(x => x.Id == id);
                if (user == null)
                {
                    return "User not found!".ToResponse(404);
                }

                var exe = new Executable($"api-tc u{id} ({value})", new Func<Task>(async () =>
                {
                    using (var dbc = new Database.DatabaseContext(_config))
                    {
                        user = await dbc.GetUserOrCreateSimpleAsync(id);
                        var beforeChange = user.TcCnt;
                        user.TcCnt += value;

                        dbc.TransferData.Add(new Database.Models.Analytics.TransferAnalytics()
                        {
                            Value = value,
                            DiscordId = user.Id,
                            Date = _time.Now(),
                            ShindenId = user.Shinden,
                            ValueBefore = beforeChange,
                            ExpectedValue = user.TcCnt,
                            Source = Database.Models.Analytics.TransferSource.ByDiscordId,
                        });

                        await dbc.SaveChangesAsync();

                        QueryCacheManager.ExpireTag(new string[] { CacheTags.User(user.Id) });
                    }
                }), id, Priority.High);

                if (!await _executor.TryAdd(exe, TimeSpan.FromSeconds(1)))
                {
                    return "Command queue is full".ToResponse(503);
                }

                return "TC added!".ToResponse(200);
            }
        }

        /// <summary>
        /// Zmiana ilości punktów TC użytkownika
        /// </summary>
        /// <param name="id">id użytkownika shindena</param>
        /// <param name="value">liczba TC</param>
        /// <response code="404">User not found</response>
        [HttpPut("shinden/{id}/tc"), Authorize(Policy = "Site")]
        public async Task<IActionResult> ModifyPointsTCAsync(ulong id, [FromBody, Required]long value)
        {
            using (var db = new Database.DatabaseContext(_config))
            {
                var user = db.Users.FirstOrDefault(x => x.Shinden == id);
                if (user == null)
                {
                    return "User not found!".ToResponse(404);
                }

                var exe = new Executable($"api-tc su{id} ({value})", new Func<Task>(async () =>
                {
                    using (var dbs = new Database.DatabaseContext(_config))
                    {
                        user = await dbs.Users.AsQueryable().FirstOrDefaultAsync(x => x.Shinden == id);
                        if (user == null) return;

                        var beforeChange = user.TcCnt;
                        user.TcCnt += value;

                        dbs.TransferData.Add(new Database.Models.Analytics.TransferAnalytics()
                        {
                            Value = value,
                            DiscordId = user.Id,
                            Date = _time.Now(),
                            ShindenId = user.Shinden,
                            ValueBefore = beforeChange,
                            ExpectedValue = user.TcCnt,
                            Source = Database.Models.Analytics.TransferSource.ByShindenId,
                        });

                        await dbs.SaveChangesAsync();

                        QueryCacheManager.ExpireTag(new string[] { CacheTags.User(user.Id) });
                    }
                }), user.Id, Priority.High);

                if (!await _executor.TryAdd(exe, TimeSpan.FromSeconds(1)))
                {
                    return "Command queue is full".ToResponse(503);
                }

                return "TC added!".ToResponse(200);
            }
        }
    }
}