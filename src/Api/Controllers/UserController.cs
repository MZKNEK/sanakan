#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Discord.WebSocket;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Sanakan.Api.Models;
using Sanakan.Config;
using Sanakan.Config.Model;
using Sanakan.Extensions;
using Sanakan.Services.Executor;
using Sanakan.Services.PocketWaifu;
using Sanakan.Services.Time;
using Sanakan.ShindenCompat;
using Sanakan.ShindenCompat.Models;

namespace Sanakan.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UserController : ControllerBase
    {
        private const ulong MainGuildId = 245931283031523330;
        private static readonly SemaphoreSlim AccountLinkLock = new SemaphoreSlim(1, 1);

        private readonly IConfig _config;
        private readonly ILogger _logger;
        private readonly ISystemTime _time;
        private readonly IExecutor _executor;
        private readonly ShindenClient _shClient;
        private readonly Services.Shinden _shinden;
        private readonly TagHelper _tags;
        private readonly Services.Profile _profile;
        private readonly DiscordSocketClient _client;

        public UserController(DiscordSocketClient client, ShindenClient shClient, ILogger logger,
            IExecutor executor, IConfig config, ISystemTime time, Services.Shinden shinden, TagHelper tags, Services.Profile profile)
        {
            _tags = tags;
            _profile = profile;
            _time = time;
            _config = config;
            _client = client;
            _logger = logger;
            _shinden = shinden;
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
        [HttpPost("find"), AllowAnonymous]
        public async Task<ActionResult<IEnumerable<ShindenCompat.Models.IUserSearch>>> GetUserIdByNameAsync([FromBody, Required]string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 256)
                return "Invalid name!".ToResponse(400);

            ShindenCompat.Response<List<ShindenCompat.Models.IUserSearch>> res;
            try
            {
                res = await _shClient.Search.UserAsync(name);
            }
            catch (Exception)
            {
                return "Can't connect to shinden!".ToResponse(503);
            }

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
        [HttpGet("shinden/{id}/username"), AllowAnonymous]
        public async Task<ActionResult<string>> GetShindenUsernameByShindenId(ulong id)
        {
            string username;
            try
            {
                username = await _shinden.GetUserNameAsync(id);
            }
            catch (Exception)
            {
                return "Can't connect to shinden!".ToResponse(503);
            }

            if (username == null)
            {
                return "User not found!".ToResponse(404);
            }

            return username;
        }

        /// <summary>
        /// Pobieranie użytkownika bota
        /// </summary>
        /// <param name="id">id użytkownika shindena</param>
        /// <returns>użytkownik bota</returns>
        [HttpGet("shinden/{id}"), Authorize(Policy = "Site")]
        public async Task<ActionResult<UserWithToken>> GetUserByShindenIdAsync(ulong id)
        {
            using (var db = CreateDatabaseContext())
            {
                var user = await db.GetCachedFullUserByShindenIdAsync(id);
                if (user == null)
                {
                    return "User not found!".ToResponse(404);
                }

                TokenData tokenData = null;
                var currUser = ControllerContext.HttpContext.User;
                if (currUser.HasClaim(x => x.Type == ClaimTypes.Webpage) && !user.IsBlacklisted)
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
            using (var db = CreateDatabaseContext())
            {
                var user = await db.Users.AsQueryable().AsSplitQuery().Where(x => id != 0 && x.Shinden == id).Include(x => x.GameDeck).AsNoTracking().FirstOrDefaultAsync();
                if (user == null)
                {
                    return "User not found!".ToResponse(404);
                }

                TokenData tokenData = null;
                var currUser = ControllerContext.HttpContext.User;
                if (currUser.HasClaim(x => x.Type == ClaimTypes.Webpage) && !user.IsBlacklisted)
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
                var discordId = await db.Users.AsQueryable().Where(x => id != 0 && x.Shinden == id).AsNoTracking().Select(x => (ulong?)x.Id).FirstOrDefaultAsync();
                if (discordId == null)
                {
                    return "User not found!".ToResponse(404);
                }

                return await GetUserPermissionsAsync(discordId.Value, guildId ?? MainGuildId);
            }
        }

        /// <summary>
        /// Pobiera dane do podglądu profilu w Profilatorze: kod wyglądu z obrazkami (wysłanymi na kanał śmieci głównego serwera), liczby, karty i statystyki z Shindena (nagłówek x-app-key z uprawnieniem Info)
        /// </summary>
        /// <param name="id">id użytkownika discorda</param>
        /// <response code="404">User not found</response>
        [HttpGet("discord/{id}/profile"), Authorize(Policy = "Info")]
        public async Task<ActionResult<ProfilePreview>> GetProfilePreviewAsync(ulong id)
        {
            using (var db = new Database.DatabaseContext(_config))
            {
                var user = await db.GetCachedFullUserAsync(id);
                if (user == null)
                {
                    return "User not found!".ToResponse(404);
                }

                var ranking = await db.GetCachedAllUsersLiteAsync(1);
                var top = ranking.OrderByDescending(x => x.ExpCnt).Select(x => x.Id).ToList().IndexOf(id) + 1;

                // as the profile is drawn: the colour of the highest role, grey without one
                var member = _client.GetGuild(MainGuildId)?.GetUser(id);
                var account = (Discord.IUser)member ?? _client.GetUser(id);
                var color = member?.Roles.OrderByDescending(x => x.Position).FirstOrDefault()?.Color ?? Discord.Color.Default;
                if (color == Discord.Color.Default)
                    color = Discord.Color.DarkerGrey;

                // the code with the account's own pictures, sent to the trash channel of the main server
                var gConfig = await db.GetCachedGuildFullConfigAsync(MainGuildId);
                var trash = _client.GetGuild(MainGuildId)?.GetTextChannel(gConfig?.WaifuConfig?.TrashCommandsChannel ?? 0);
                var code = trash == null ? ProfileCode.FromUser(user) : await _profile.GetProfileCodeWithImagesAsync(user, trash, true);

                var shinden = await GetShindenUserAsync(user.Shinden);
                var stats = shinden?.ListStats;
                var cards = user.GameDeck.Cards;
                var waifu = user.GameDeck.Waifu != 0 ? user.GameDeck.GetWaifuCard() : null;

                return new ProfilePreview
                {
                    Code = code.Encode(),
                    Nick = account?.GetUserNickInGuild() ?? "",
                    Avatar = account?.GetUserOrDefaultAvatarUrl(true) ?? "",
                    Color = "#" + color.RawValue.ToString("x6"),
                    Rank = shinden?.Rank ?? "",
                    Level = user.Level,
                    Exp = user.ExpCnt,
                    Top = top,
                    Messages = user.MessagesCnt,
                    Sc = user.ScCnt,
                    Tc = user.TcCnt,
                    Ac = user.AcCnt,
                    Ct = user.GameDeck.CTCnt,
                    Pc = user.GameDeck.PVPCoins,
                    Cards = cards.Count,
                    Limit = user.GameDeck.MaxNumberOfCards,
                    Karma = user.GameDeck.Karma,
                    Rarities = cards.Where(x => !x.FromFigure).GroupBy(x => x.Rarity).ToDictionary(x => x.Key.ToString(), x => x.Count()),
                    Scalpels = cards.Count(x => !string.IsNullOrEmpty(x.CustomImage)),
                    Scissors = cards.Count(x => !string.IsNullOrEmpty(x.CustomBorder)),
                    Ultimates = cards.Count(x => x.FromFigure),
                    Waifu = waifu == null ? null : CardFinalView.GetCardProfileInShindenUrl(waifu),
                    Gallery = user.GetProfileGalleryCards(_tags.GetTag(TagType.Gallery).Id, 12).Select(CardFinalView.GetCardProfileInShindenUrl).ToList(),
                    Anime = ToListStats(stats?.AnimeStatus, stats?.AnimeMeanScore, stats?.EpisodesCount, stats?.AnimeTime),
                    Manga = ToListStats(stats?.MangaStatus, stats?.MangaMeanScore, stats?.ChaptersCount, stats?.MangaTime),
                };
            }
        }

        private async Task<IUserInfo> GetShindenUserAsync(ulong shindenId)
        {
            if (shindenId == 0)
                return null;

            try
            {
                var res = await _shClient.User.GetAsync(shindenId);
                return res.IsSuccessStatusCode() ? res.Body : null;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Profile preview: shinden user {shindenId}: {ex.Message}");
                return null;
            }
        }

        private static ProfileListStats ToListStats(ISeriesStatus status, IMeanScore score, ulong? count, IDedicatedTime time)
        {
            if (status == null)
                return null;

            return new ProfileListStats
            {
                InProgress = status.InProgress ?? 0,
                Completed = status.Completed ?? 0,
                Skipped = status.Skipped ?? 0,
                OnHold = status.OnHold ?? 0,
                Dropped = status.Dropped ?? 0,
                InPlan = status.InPlan ?? 0,
                Total = status.Total ?? 0,
                Rating = score?.Rating ?? 0,
                Count = count ?? 0,
                Years = time?.Years ?? 0,
                Months = time?.Months ?? 0,
                Days = time?.Days ?? 0,
                Hours = time?.Hours ?? 0,
                Minutes = time?.Minutes ?? 0,
            };
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
        /// Łączy konto Discord z Shinden i tworzy gracza, jeśli nie ma go w bazie.
        /// Identyfikatory przekazuje uwierzytelniona strona; nie są sprawdzane przez API Shinden.
        /// </summary>
        /// <param name="id">relacja</param>
        /// <response code="200">Zapisano powiązanie lub ta sama para kont jest już połączona.</response>
        /// <response code="400">Nieprawidłowe dane żądania.</response>
        /// <response code="409">
        /// Kod shinden_account_already_linked: konto Shinden jest połączone z innym ID Discord.
        /// Kod discord_account_already_linked: konto Discord jest połączone z innym kontem Shinden.
        /// Odpowiedź zawiera success: false, code oraz message; dotychczasowe powiązanie pozostaje bez zmian.
        /// Lista accounts zawiera discordUserId, shindenUserId, username, nickname i avatarUrl dla istniejących powiązań.
        /// Identyfikatory są tekstem; przy niedostępnym koncie Discord nazwy i awatar mają wartość null.
        /// </response>
        /// <response code="503">Kolejka poleceń jest pełna.</response>
        /// <response code="504">Przekroczono 30 sekund oczekiwania; zadanie może nadal się wykonać.</response>
        [HttpPut("register"), Authorize(Policy = "Site")]
        public async Task<IActionResult> RegisterUserAsync([FromBody, Required]UserRegistration id)
        {
            if (id == null || id.DiscordUserId == 0 || id.ShindenUserId == 0)
            {
                return "Model is Invalid!".ToResponse(400);
            }

            IActionResult result = null;
            var exe = new Executable($"api-register u{id.DiscordUserId}", new Func<Task>(async () =>
            {
                await AccountLinkLock.WaitAsync();
                try
                {
                    using var db = CreateDatabaseContext();
                    var oldUsers = await db.Users.AsQueryable()
                        .Where(x => x.Shinden == id.ShindenUserId && x.Id != id.DiscordUserId).ToListAsync();
                    if (oldUsers.Count != 0)
                    {
                        result = LinkConflict("shinden_account_already_linked",
                            "This Shinden account is already linked to another Discord account.", oldUsers);
                        foreach (var anc in _config.Get().AdminNotifyChannels ?? Enumerable.Empty<LogChannelConfig>())
                        {
                            var channel = _client?.GetGuild(anc.GuildId)?.GetTextChannel(anc.ChannelId);
                            if (channel == null) continue;

                            try
                            {
                                await channel.SendMessageAsync("", embed: ($"Potencjalne multikonto:\nDID: {id.DiscordUserId} <@{id.DiscordUserId}>\nSID: {id.ShindenUserId}\n"
                                    + $"\noDID: {string.Join(",", oldUsers.Select(x => $"{x.Id} <@{x.Id}>"))}").TrimToLength().ToEmbedMessage(EMType.Error).Build());
                            }
                            catch (Exception ex)
                            {
                                _logger?.LogError($"API registration notification failed: {ex.Message}");
                            }
                        }
                        return;
                    }

                    var botUser = await db.Users.AsQueryable().FirstOrDefaultAsync(x => x.Id == id.DiscordUserId);
                    if (botUser != null && botUser.Shinden != 0)
                    {
                        result = botUser.Shinden == id.ShindenUserId
                            ? "User already connected!".ToResponse(200)
                            : LinkConflict("discord_account_already_linked",
                                "This Discord account is already linked to another Shinden account.", new[] { botUser });
                        return;
                    }

                    botUser = await db.GetUserOrCreateSimpleAsync(id.DiscordUserId);
                    botUser.Shinden = id.ShindenUserId;
                    await db.SaveChangesAsync();
                    QueryCache.ExpireTag(new string[] { CacheTags.User(id.DiscordUserId), CacheTags.UserProfile(id.ShindenUserId) });
                    result = "User connected!".ToResponse(200);
                }
                finally
                {
                    AccountLinkLock.Release();
                }
            }), id.DiscordUserId, Priority.High);

            if (!await _executor.TryAdd(exe, TimeSpan.FromSeconds(1)))
                return "Command queue is full".ToResponse(503);

            if (!await exe.WaitAsync(Executable.ApiMaxWait))
                return "Command timed out".ToResponse(504);

            return await PopulateLinkConflictAsync(result);
        }

        /// <summary>
        /// Rozłącza wskazaną parę kont Discord i Shinden, zachowując gracza i jego postęp.
        /// </summary>
        /// <param name="discordUserId">Id konta Discord.</param>
        /// <param name="shindenUserId">Id konta Shinden, które ma zostać odłączone; nie jest to ID forum.</param>
        /// <response code="200">Usunięto powiązanie lub konto Discord jest już rozłączone.</response>
        /// <response code="400">Identyfikator Discord lub Shinden ma wartość zero.</response>
        /// <response code="404">Nie znaleziono gracza w bazie.</response>
        /// <response code="409">Kod account_link_mismatch: konto Discord jest teraz połączone z innym Shindenem. Lista accounts zawiera aktualne powiązanie; nie usunięto go.</response>
        /// <response code="503">Kolejka poleceń jest pełna.</response>
        /// <response code="504">Przekroczono 30 sekund oczekiwania; zadanie może nadal się wykonać.</response>
        [HttpDelete("discord/{discordUserId}/shinden/{shindenUserId}"), Authorize(Policy = "Site")]
        public async Task<IActionResult> DisconnectUserAsync(ulong discordUserId, ulong shindenUserId)
        {
            if (discordUserId == 0 || shindenUserId == 0)
                return "Model is Invalid!".ToResponse(400);

            IActionResult result = null;
            var exe = new Executable($"api-disconnect u{discordUserId} s{shindenUserId}", new Func<Task>(async () =>
            {
                await AccountLinkLock.WaitAsync();
                try
                {
                    using var db = CreateDatabaseContext();
                    var user = await db.Users.AsQueryable().FirstOrDefaultAsync(x => x.Id == discordUserId);
                    if (user == null)
                    {
                        result = "User not found!".ToResponse(404);
                        return;
                    }

                    if (user.Shinden != 0 && user.Shinden != shindenUserId)
                    {
                        result = LinkConflict("account_link_mismatch",
                            "This Discord account is linked to a different Shinden account.", new[] { user });
                        return;
                    }

                    if (user.Shinden != 0)
                    {
                        user.Shinden = 0;
                        await db.SaveChangesAsync();
                        QueryCache.ExpireTag(new string[] { CacheTags.User(discordUserId), CacheTags.UserProfile(shindenUserId) });
                    }
                    result = "User disconnected!".ToResponse(200);
                }
                finally
                {
                    AccountLinkLock.Release();
                }
            }), discordUserId, Priority.High);

            if (!await _executor.TryAdd(exe, TimeSpan.FromSeconds(1)))
                return "Command queue is full".ToResponse(503);

            if (!await exe.WaitAsync(Executable.ApiMaxWait))
                return "Command timed out".ToResponse(504);

            return await PopulateLinkConflictAsync(result);
        }

        private IActionResult LinkConflict(string code, string message, IEnumerable<Database.Models.User> users)
            => Conflict(new AccountLinkConflict
            {
                Code = code,
                Message = message,
                Accounts = users.OrderBy(x => x.Id).Select(x => new LinkedDiscordAccount
                {
                    DiscordUserId = x.Id.ToString(),
                    ShindenUserId = x.Shinden.ToString()
                }).ToList()
            });

        private async Task<IActionResult> PopulateLinkConflictAsync(IActionResult result)
        {
            if (result is ConflictObjectResult { Value: AccountLinkConflict conflict })
                await Task.WhenAll(conflict.Accounts.Select(PopulateDiscordAccountAsync));
            return result;
        }

        private async Task PopulateDiscordAccountAsync(LinkedDiscordAccount account)
        {
            try
            {
                var user = await GetLinkedDiscordUserAsync(ulong.Parse(account.DiscordUserId));
                if (user == null) return;
                account.Username = user.Username;
                account.Nickname = (user as Discord.IGuildUser)?.Nickname ?? user.GlobalName ?? user.Username;
                account.AvatarUrl = user.GetUserOrDefaultAvatarUrl(true);
            }
            catch (Exception ex)
            {
                _logger?.LogError($"API account link: Discord user {account.DiscordUserId}: {ex.Message}");
            }
        }

        protected virtual async Task<Discord.IUser> GetLinkedDiscordUserAsync(ulong discordUserId)
        {
            if (_client == null) return null;
            var member = _client.GetGuild(MainGuildId)?.GetUser(discordUserId);
            return (Discord.IUser)member ?? _client.GetUser(discordUserId)
                ?? await _client.GetUserAsync(discordUserId, new Discord.RequestOptions { Timeout = 5000 });
        }

        protected virtual Database.DatabaseContext CreateDatabaseContext() => new Database.DatabaseContext(_config);

        /// <summary>
        /// Zmiana ilości punktów TC użytkownika
        /// </summary>
        /// <remarks>
        /// Z nagłówkiem Idempotency-Key odpowiedź przychodzi po zapisie TC; powtórka tej samej operacji w ciągu 48 h
        /// nie zmienia salda ponownie i zwraca Idempotent-Replayed: true. Bez nagłówka każde żądanie dodaje TC jak wcześniej.
        /// Klucze są osobne dla operacji po ID Discord i po ID Shinden.
        /// </remarks>
        /// <param name="id">id użytkownika discorda</param>
        /// <param name="value">liczba TC</param>
        /// <param name="idempotencyKey">Opcjonalny identyfikator transakcji, do 64 znaków ASCII bez spacji.</param>
        /// <response code="400">Nieprawidłowy Idempotency-Key.</response>
        /// <response code="404">User not found</response>
        /// <response code="409">Transakcja z tym kluczem jest w trakcie wykonywania.</response>
        /// <response code="422">Klucz został użyty z innym użytkownikiem lub inną kwotą.</response>
        /// <response code="500">Nie zapisano zmiany TC.</response>
        /// <response code="503">Kolejka poleceń jest pełna.</response>
        /// <response code="504">Przekroczono czas oczekiwania; transakcja może nadal się wykonać.</response>
        [HttpPut("discord/{id}/tc"), Authorize(Policy = "Site")]
        public async Task<IActionResult> ModifyPointsTCDiscordAsync(ulong id, [FromBody, Required]long value,
            [FromHeader(Name = Services.Idempotency.KeyHeader)] string idempotencyKey = null)
        {
            if (idempotencyKey != null || HttpContext?.Request.Headers.ContainsKey(Services.Idempotency.KeyHeader) == true)
            {
                if (!Services.Idempotency.IsValidKey(idempotencyKey))
                    return "Invalid Idempotency-Key".ToResponse(400);
                return await ModifyPointsTCOnceAsync(id, value, idempotencyKey, Database.Models.Analytics.TransferSource.ByDiscordId);
            }

            using (var db = CreateDatabaseContext())
            {
                var user = await db.Users.AsQueryable().FirstOrDefaultAsync(x => x.Id == id);
                if (user == null)
                {
                    return "User not found!".ToResponse(404);
                }

                var exe = new Executable($"api-tc u{id} ({value})", new Func<Task>(async () =>
                {
                    using (var dbc = CreateDatabaseContext())
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

                        QueryCache.ExpireTag(new string[] { CacheTags.User(user.Id) });
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
        /// <remarks>
        /// Z nagłówkiem Idempotency-Key odpowiedź przychodzi po zapisie TC; powtórka tej samej operacji w ciągu 48 h
        /// nie zmienia salda ponownie i zwraca Idempotent-Replayed: true. Bez nagłówka każde żądanie dodaje TC jak wcześniej.
        /// Klucze są osobne dla operacji po ID Discord i po ID Shinden.
        /// </remarks>
        /// <param name="id">id użytkownika shindena</param>
        /// <param name="value">liczba TC</param>
        /// <param name="idempotencyKey">Opcjonalny identyfikator transakcji, do 64 znaków ASCII bez spacji.</param>
        /// <response code="400">Nieprawidłowy Idempotency-Key.</response>
        /// <response code="404">User not found</response>
        /// <response code="409">Transakcja z tym kluczem jest w trakcie wykonywania.</response>
        /// <response code="422">Klucz został użyty z innym użytkownikiem lub inną kwotą.</response>
        /// <response code="500">Nie zapisano zmiany TC.</response>
        /// <response code="503">Kolejka poleceń jest pełna.</response>
        /// <response code="504">Przekroczono czas oczekiwania; transakcja może nadal się wykonać.</response>
        [HttpPut("shinden/{id}/tc"), Authorize(Policy = "Site")]
        public async Task<IActionResult> ModifyPointsTCAsync(ulong id, [FromBody, Required]long value,
            [FromHeader(Name = Services.Idempotency.KeyHeader)] string idempotencyKey = null)
        {
            if (idempotencyKey != null || HttpContext?.Request.Headers.ContainsKey(Services.Idempotency.KeyHeader) == true)
            {
                if (!Services.Idempotency.IsValidKey(idempotencyKey))
                    return "Invalid Idempotency-Key".ToResponse(400);
                return await ModifyPointsTCOnceAsync(id, value, idempotencyKey, Database.Models.Analytics.TransferSource.ByShindenId);
            }

            using (var db = CreateDatabaseContext())
            {
                var user = await db.Users.AsQueryable().FirstOrDefaultAsync(x => id != 0 && x.Shinden == id);
                if (user == null)
                {
                    return "User not found!".ToResponse(404);
                }

                var exe = new Executable($"api-tc su{id} ({value})", new Func<Task>(async () =>
                {
                    using (var dbs = CreateDatabaseContext())
                    {
                        user = await dbs.Users.AsQueryable().FirstOrDefaultAsync(x => id != 0 && x.Shinden == id);
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

                        QueryCache.ExpireTag(new string[] { CacheTags.User(user.Id) });
                    }
                }), user.Id, Priority.High);

                if (!await _executor.TryAdd(exe, TimeSpan.FromSeconds(1)))
                {
                    return "Command queue is full".ToResponse(503);
                }

                return "TC added!".ToResponse(200);
            }
        }

        private async Task<IActionResult> ModifyPointsTCOnceAsync(ulong id, long value, string key, Database.Models.Analytics.TransferSource source)
        {
            var byDiscord = source == Database.Models.Analytics.TransferSource.ByDiscordId;
            var record = new Database.Models.IdempotencyRecord
            {
                Scope = byDiscord ? "discord-tc" : "shinden-tc",
                Key = key,
                OwnerId = id,
                RequestHash = Services.Idempotency.Hash(value),
                CreatedAt = _time.Now(),
            };

            var running = Services.Idempotency.TryBegin(record);
            if (running != null)
                return Services.Idempotency.SameRequest(running, record)
                    ? "Request with this Idempotency-Key is in progress".ToResponse(409)
                    : "Idempotency-Key was used with a different request".ToResponse(422);

            var handedOff = false;
            var committed = false;
            try
            {
                using var db = CreateDatabaseContext();
                var done = await Services.Idempotency.FindAsync(db, record.Scope, key, record.CreatedAt);
                if (done != null)
                {
                    if (!Services.Idempotency.SameRequest(done, record))
                        return "Idempotency-Key was used with a different request".ToResponse(422);

                    Response.Headers[Services.Idempotency.ReplayedHeader] = "true";
                    return "TC added!".ToResponse(200);
                }

                var user = await db.Users.AsQueryable().FirstOrDefaultAsync(x => byDiscord ? x.Id == id : id != 0 && x.Shinden == id);
                if (user == null)
                    return "User not found!".ToResponse(404);

                var discordId = user.Id;
                IActionResult result = null;
                var exe = new Executable($"api-tc {(byDiscord ? "u" : "su")}{id} ({value})", new Func<Task>(async () =>
                {
                    try
                    {
                        using var dbs = CreateDatabaseContext();
                        var botUser = await dbs.Users.AsQueryable().FirstOrDefaultAsync(x => x.Id == discordId && (byDiscord || x.Shinden == id));
                        if (botUser == null)
                        {
                            result = "User not found!".ToResponse(404);
                            return;
                        }

                        var beforeChange = botUser.TcCnt;
                        botUser.TcCnt += value;
                        dbs.TransferData.Add(new Database.Models.Analytics.TransferAnalytics
                        {
                            Value = value,
                            DiscordId = botUser.Id,
                            Date = _time.Now(),
                            ShindenId = botUser.Shinden,
                            ValueBefore = beforeChange,
                            ExpectedValue = botUser.TcCnt,
                            Source = source,
                        });
                        dbs.IdempotencyRecords.Add(record);
                        await dbs.SaveChangesAsync();
                        committed = true;
                        result = "TC added!".ToResponse(200);

                        QueryCache.ExpireTag(new string[] { CacheTags.User(botUser.Id) });
                        await Services.Idempotency.CleanupIfDueAsync(dbs, record.CreatedAt);
                    }
                    finally
                    {
                        Services.Idempotency.End(record);
                    }
                }), discordId, Priority.High);

                if (!await _executor.TryAdd(exe, TimeSpan.FromSeconds(1)))
                    return "Command queue is full".ToResponse(503);
                handedOff = true;

                if (!await exe.WaitAsync(Executable.ApiMaxWait))
                    return "Command timed out".ToResponse(504);

                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError($"API TC transaction {record.Scope}: {ex.Message}");
                return committed ? "TC added!".ToResponse(200) : "TC not added".ToResponse(500);
            }
            finally
            {
                if (!handedOff)
                    Services.Idempotency.End(record);
            }
        }
    }
}
