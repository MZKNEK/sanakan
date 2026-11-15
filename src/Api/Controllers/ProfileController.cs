#pragma warning disable 1591

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Discord.WebSocket;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Sanakan.Api.Models;
using Sanakan.Config;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services;
using Sanakan.Services.Executor;
using Sanakan.Services.PocketWaifu;
using Sanakan.Services.Profiles;
using Sanakan.Services.Time;
using SixLabors.ImageSharp;

namespace Sanakan.Api.Controllers
{
    /// <summary>
    /// Profil z warstw dla Profilatora: cennik, odczyt, obrazki, podgląd, wycena i zapis z płatnością.
    /// Identyfikator gracza podaje serwer strony z sesji zalogowanego konta (klucz aplikacji z uprawnieniem Sansite).
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class ProfileController : ControllerBase
    {
        public const int MaxPictureBytes = 10 * 1024 * 1024;
        public const int MaxPictureSide = 4096;
        public const int MaxPicturesPerPlayer = 40;
        public const int StickerChoices = 40;
        private static readonly TimeSpan KeepUnusedPictures = TimeSpan.FromHours(1);

        // drawing is heavy: two at a time for the whole API and one per player
        private static readonly SemaphoreSlim Drawing = new SemaphoreSlim(2, 2);
        private static readonly ConcurrentDictionary<ulong, byte> DrawingFor = new ConcurrentDictionary<ulong, byte>();

        private readonly IConfig _config;
        private readonly ILogger _logger;
        private readonly ISystemTime _time;
        private readonly IExecutor _executor;
        private readonly Services.Profile _profile;
        private readonly DiscordSocketClient _client;

        public ProfileController(DiscordSocketClient client, ILogger logger, IExecutor executor, IConfig config, ISystemTime time, Services.Profile profile)
        {
            _client = client;
            _logger = logger;
            _executor = executor;
            _config = config;
            _time = time;
            _profile = profile;
        }

        protected virtual Database.DatabaseContext CreateDatabaseContext() => new Database.DatabaseContext(_config);

        protected virtual Discord.IUser Account(ulong id)
            => (Discord.IUser)_client?.GetGuild(UserController.MainGuildId)?.GetUser(id) ?? _client?.GetUser(id);

        private static JToken ToJson(ProfileLayout layout) => JToken.Parse(layout.ToJson());

        private static ProfileCardView CardView(Card card) => card == null ? null : new ProfileCardView
        {
            Id = card.Id, Name = card.Name, Rarity = card.Rarity.ToString(), Animated = card.IsAnimatedImage, Url = CardFinalView.GetCardProfileInShindenUrl(card),
        };

        private static ProfileListView ListView(Services.Profiles.ProfileListStats stats) => stats == null ? null : new ProfileListView
        {
            Statuses = stats.Statuses, Total = stats.Total, Units = stats.Units, Time = stats.Time, Mean = stats.Mean,
            Recent = stats.Recent.Select(x => new ProfileRecentView { Title = x.Title, Progress = x.Progress, Ratio = x.Ratio, Cover = x.CoverUrl }).ToList(),
        };

        private static ProfilePictureView PictureView(ProfilePicture p) => new ProfilePictureView
        {
            Id = p.Id, Use = p.Use.ToString().ToLowerInvariant(), Width = p.Width, Height = p.Height, Animated = p.Animated,
        };

        /// <summary>
        /// Cennik odblokowań, gotowe układy, ramki awatara i limity profilu z warstw
        /// </summary>
        [HttpGet("catalog"), AllowAnonymous]
        public ActionResult<ProfileCatalogView> GetCatalog() => new ProfileCatalogView
        {
            LayoutVersion = ProfileLayout.CurrentVersion,
            MaxStickers = ProfileCatalog.MaxStickers,
            MaxOverlays = ProfileCatalog.MaxOverlays,
            MaxLayoutJson = ProfileCatalog.MaxLayoutJson,
            MaxPictureBytes = MaxPictureBytes,
            Features = ProfileCatalog.Features.Select(x => new ProfileFeatureView { Key = x.Key, Name = x.Name, Description = x.Description, Sc = x.Sc, Tc = x.Tc }).ToList(),
            Presets = ProfileCatalog.Presets.Select(x => new ProfilePresetView { Key = x.Key, Name = x.Name, Layout = ToJson(x.Make()) }).ToList(),
            Frames = Enum.GetValues<AvatarBorder>().Select(x => new ProfileFrameView
            {
                Key = x.ToString(), Name = ProfileCatalog.FrameName(x), Free = ProfileCatalog.IsFreeFrame(x), Selectable = ProfileCatalog.IsSelectableFrame(x),
            }).ToList(),
        };

        // the player with the deck and all cards, as the profile command reads it
        private static async Task<User> LoadPlayerAsync(Database.DatabaseContext db, ulong id)
        {
            var user = await db.Users.AsQueryable().Include(x => x.GameDeck).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (user != null)
                user.GameDeck.Cards = (await db.GetCachedUserGameDeckAsync(id)).Cards;
            return user;
        }

        private static async Task<long> TopAsync(Database.DatabaseContext db, ulong id)
        {
            var ranking = await db.GetCachedAllUsersLiteAsync(1);
            return ranking.OrderByDescending(x => x.ExpCnt).Select(x => x.Id).ToList().IndexOf(id) + 1;
        }

        /// <summary>
        /// Profil gracza do edycji: układ, odblokowania, obrazki, karty do naklejek i dane do podglądu
        /// </summary>
        /// <param name="id">id użytkownika discorda</param>
        /// <response code="404">User not found</response>
        [HttpGet("discord/{id}"), Authorize(Policy = "Sansite")]
        public async Task<ActionResult<ProfileEditorView>> GetEditorAsync(ulong id)
        {
            using var db = CreateDatabaseContext();
            var user = await LoadPlayerAsync(db, id);
            if (user == null)
                return "User not found!".ToResponse(404);

            var stored = await _profile.Store.LoadAsync(db, user);
            var unlocks = await ProfileStore.GetUnlocksAsync(db, id);
            var pictures = await ProfileStore.GetPicturesAsync(db, id);

            // the data of both lists with their recent titles, so every option can be shown before it is chosen
            var probe = ProfileCatalog.FindPreset("lists").Make();
            foreach (var list in probe.LayersOf<ListPanelLayer>())
                list.Recent = ListPanelLayer.MaxRecent;
            var data = await _profile.GetLayeredDataAsync(user, probe, Account(id), await TopAsync(db, id));

            var stickers = stored.Layout.LayersOf<StickerLayer>().Select(x => x.Card).ToHashSet();
            var choices = new List<Card>();
            if (data.Waifu != null) choices.Add(data.Waifu);
            choices.AddRange(user.GameDeck.Cards.Where(x => stickers.Contains(x.Id)));
            choices.AddRange(_profile.GalleryCards(user, StickerChoices));

            return new ProfileEditorView
            {
                Layout = ToJson(stored.Layout),
                Version = stored.Version,
                Unlocks = unlocks.OrderBy(x => x, StringComparer.Ordinal).ToList(),
                Pictures = pictures.Select(PictureView).ToList(),
                Cards = choices.GroupBy(x => x.Id).Select(x => CardView(x.First())).Take(StickerChoices).ToList(),
                Data = new ProfileDataView
                {
                    Nick = data.Nick, Rank = data.Rank, Avatar = data.AvatarUrl, Color = data.RoleColor, Top = data.Top, Level = data.Level,
                    LevelProgress = data.LevelProgress, Messages = data.Messages, Sc = data.Sc, Tc = data.Tc, Ac = data.Ac, Ct = data.Ct, Pc = data.Pc,
                    Cards = data.Cards, Limit = data.Limit, Karma = data.Karma.ToString().ToLowerInvariant(),
                    Rarities = data.Rarities.ToDictionary(x => x.Key.ToString(), x => x.Value),
                    Ultimates = data.Ultimates, CustomImages = data.CustomImages, CustomBorders = data.CustomBorders,
                    Waifu = CardView(data.Waifu), Gallery = data.Gallery.Select(CardView).ToList(),
                    Anime = ListView(data.Anime), Manga = ListView(data.Manga),
                },
            };
        }

        /// <summary>
        /// Obrazek gracza z jego profilu
        /// </summary>
        [HttpGet("discord/{id}/images/{picture}"), Authorize(Policy = "Sansite")]
        public async Task<IActionResult> GetPictureAsync(ulong id, ulong picture)
        {
            using var db = CreateDatabaseContext();
            var row = await db.ProfilePictures.AsNoTracking().FirstOrDefaultAsync(x => x.Id == picture && x.UserId == id);
            if (row == null || !System.IO.File.Exists(row.File))
                return "Picture not found!".ToResponse(404);

            var type = Path.GetExtension(row.File).ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                _ => "image/webp",
            };
            Response.Headers.CacheControl = "private, max-age=86400";
            return File(System.IO.File.OpenRead(row.File), type);
        }

        /// <summary>
        /// Wysłanie obrazka do profilu (png, jpg, gif, webp, do 10 MB i 4096 px na bok); zwraca jego id do układu
        /// </summary>
        /// <response code="400">To nie jest obrazek albo jest za duży.</response>
        /// <response code="404">User not found</response>
        /// <response code="429">Za dużo nieużywanych obrazków, zapisz profil albo poczekaj.</response>
        [HttpPost("discord/{id}/images"), Authorize(Policy = "Sansite"), RequestSizeLimit(MaxPictureBytes + 64 * 1024)]
        public async Task<ActionResult<ProfilePictureView>> UploadPictureAsync(ulong id, IFormFile file, [FromForm] string use = "overlay")
        {
            if (file == null || file.Length == 0 || file.Length > MaxPictureBytes)
                return "Missing or too big file".ToResponse(400);
            if (!Enum.TryParse<ProfileImageUse>(use, true, out var kind) || !Enum.IsDefined(kind))
                return "Unknown use".ToResponse(400);

            using var db = CreateDatabaseContext();
            if (!await db.Users.AsQueryable().AnyAsync(x => x.Id == id))
                return "User not found!".ToResponse(404);
            await RemoveUnusedPicturesAsync(db, id, null);
            if (await db.ProfilePictures.CountAsync(x => x.UserId == id) >= MaxPicturesPerPlayer)
                return "Too many pictures".ToResponse(429);

            Directory.CreateDirectory(Dir.ProfilePictures);
            var temporary = $"{Dir.ProfilePictures}/{id}_upload_{Guid.NewGuid():N}.tmp";
            try
            {
                await using (var target = System.IO.File.Create(temporary))
                    await file.CopyToAsync(target);

                var format = await Image.DetectFormatAsync(temporary);
                var info = await Image.IdentifyAsync(temporary);
                var allowed = format != null && new[] { "PNG", "JPEG", "GIF", "WEBP" }.Contains(format.Name.ToUpperInvariant());
                if (!allowed || info.Width < 1 || info.Height < 1 || info.Width > MaxPictureSide || info.Height > MaxPictureSide)
                    return "Not a picture or too big".ToResponse(400);

                var extension = "." + format.FileExtensions.First();
                var named = Path.ChangeExtension(temporary, extension);
                System.IO.File.Move(temporary, named);
                temporary = named;

                var picture = await ProfileStore.AddFileAsync(id, kind, temporary, _time.Now());
                var same = await db.ProfilePictures.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == id && x.Sha256 == picture.Sha256);
                if (same != null)
                    return PictureView(same);

                db.ProfilePictures.Add(picture);
                await db.SaveChangesAsync();
                return PictureView(picture);
            }
            catch (UnknownImageFormatException)
            {
                return "Not a picture".ToResponse(400);
            }
            catch (InvalidImageContentException)
            {
                return "Not a picture".ToResponse(400);
            }
            finally
            {
                if (System.IO.File.Exists(temporary) && temporary.EndsWith(".tmp"))
                    System.IO.File.Delete(temporary);
            }
        }

        /// <summary>
        /// Usunięcie obrazka gracza, którego nie używa jego zapisany profil
        /// </summary>
        /// <response code="404">Picture not found</response>
        /// <response code="409">Obrazek jest w zapisanym profilu, najpierw zapisz profil bez niego.</response>
        [HttpDelete("discord/{id}/images/{picture}"), Authorize(Policy = "Sansite")]
        public async Task<IActionResult> DeletePictureAsync(ulong id, ulong picture)
        {
            using var db = CreateDatabaseContext();
            var row = await db.ProfilePictures.FirstOrDefaultAsync(x => x.Id == picture && x.UserId == id);
            if (row == null)
                return "Picture not found!".ToResponse(404);

            var stored = await db.UserProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == id);
            if (stored != null && PicturesOf(ProfileLayout.FromJson(stored.Layout)).Contains((long)picture))
                return "Picture is in the saved profile".ToResponse(409);

            db.ProfilePictures.Remove(row);
            await db.SaveChangesAsync();
            await DeleteFilesAsync(db, new[] { row.File });
            return "Picture deleted!".ToResponse(200);
        }

        private static IEnumerable<long> PicturesOf(ProfileLayout layout)
        {
            if (layout.Header != null) yield return layout.Header.Image;
            if (layout.Style != null) yield return layout.Style.Image;
            if (layout.Ultra != null) yield return layout.Ultra.Image;
            if (layout.Avatar.HasCustomFrame) yield return layout.Avatar.CustomImage;
            foreach (var overlay in layout.LayersOf<OverlayLayer>())
                yield return overlay.Image;
        }

        // pictures neither the stored layout nor the one being saved uses, older than an hour, with their files
        private async Task<List<string>> RemoveUnusedPicturesAsync(Database.DatabaseContext db, ulong id, ProfileLayout saving)
        {
            var used = new HashSet<long>(saving != null ? PicturesOf(saving) : Enumerable.Empty<long>());
            var row = await db.UserProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == id);
            if (saving == null && row != null)
                used.UnionWith(PicturesOf(ProfileLayout.FromJson(row.Layout)));

            var before = _time.Now() - KeepUnusedPictures;
            var unused = (await db.ProfilePictures.Where(x => x.UserId == id).ToListAsync())
                .Where(x => !used.Contains((long)x.Id) && x.CreatedAt < before).ToList();
            if (unused.Count == 0)
                return new List<string>();

            db.ProfilePictures.RemoveRange(unused);
            if (saving == null)
                await db.SaveChangesAsync();
            return unused.Select(x => x.File).Distinct().ToList();
        }

        private static async Task DeleteFilesAsync(Database.DatabaseContext db, IEnumerable<string> files)
        {
            foreach (var file in files)
            {
                // the same file may stand behind another picture of the player
                if (await db.ProfilePictures.AnyAsync(x => x.File == file) || !Dir.IsLocal(file))
                    continue;
                try { System.IO.File.Delete(file); }
                catch (IOException) { }
            }
        }

        private (ProfileLayout Layout, ActionResult Error) ReadLayout(JToken body)
        {
            var json = body?.ToString(Formatting.None);
            if (string.IsNullOrEmpty(json) || body.Type != JTokenType.Object)
                return (null, "Missing layout".ToResponse(400));
            if (json.Length > ProfileCatalog.MaxLayoutJson)
                return (null, "Layout too big".ToResponse(400));

            try
            {
                return (ProfileLayout.FromJson(json), null);
            }
            catch (JsonException)
            {
                return (null, "Invalid layout".ToResponse(400));
            }
        }

        private static async Task<HashSet<ulong>> OwnedCardsAsync(Database.DatabaseContext db, ulong deckId, ProfileLayout layout)
        {
            var wanted = layout.LayersOf<StickerLayer>().Select(x => x.Card).Distinct().ToList();
            if (wanted.Count == 0)
                return new HashSet<ulong>();
            return (await db.Cards.AsNoTracking().Where(x => x.GameDeckId == deckId && wanted.Contains(x.Id)).Select(x => x.Id).ToListAsync()).ToHashSet();
        }

        private async Task<(List<string> Errors, List<ProfileFeature> Missing)> CheckAsync(Database.DatabaseContext db, User user, ProfileLayout layout)
        {
            var pictures = await ProfileStore.GetPicturesAsync(db, user.Id);
            var owned = await OwnedCardsAsync(db, user.GameDeck.Id, layout);
            var errors = ProfileRules.Validate(layout, ProfileStore.RulesContext(pictures, owned.Contains));
            var missing = ProfileRules.MissingUnlocks(layout, await ProfileStore.GetUnlocksAsync(db, user.Id));
            return (errors, missing);
        }

        private static ProfileFeatureView FeatureView(ProfileFeature x) => new ProfileFeatureView { Key = x.Key, Name = x.Name, Description = x.Description, Sc = x.Sc, Tc = x.Tc };

        /// <summary>
        /// Wycena układu: błędy, brakujące odblokowania i ich koszt w SC i TC
        /// </summary>
        [HttpPost("discord/{id}/quote"), Authorize(Policy = "Sansite")]
        public async Task<ActionResult<ProfileQuoteView>> QuoteAsync(ulong id, [FromBody, Required] JToken layoutBody)
        {
            var (layout, error) = ReadLayout(layoutBody);
            if (error != null)
                return error;

            using var db = CreateDatabaseContext();
            var user = await db.Users.AsQueryable().Include(x => x.GameDeck).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (user == null)
                return "User not found!".ToResponse(404);

            await _profile.Store.LoadAsync(db, user);
            var (errors, missing) = await CheckAsync(db, user, layout);
            return new ProfileQuoteView
            {
                Errors = errors,
                Missing = missing.Select(FeatureView).ToList(),
                Sc = ProfileRules.Cost(missing, CurrencyType.SC),
                Tc = ProfileRules.Cost(missing, CurrencyType.TC),
                WalletSc = user.ScCnt,
                WalletTc = user.TcCnt,
            };
        }

        private async Task<IActionResult> DrawAsync(ulong id, Func<Task<Image>> draw)
        {
            if (!DrawingFor.TryAdd(id, 0))
                return ApiResponses.Unavailable(Response, "A picture of this profile is being drawn", 2);
            try
            {
                if (!await Drawing.WaitAsync(TimeSpan.FromSeconds(10)))
                    return ApiResponses.Unavailable(Response, "Drawing is busy", 5);
                try
                {
                    using var image = await draw();
                    if (image == null)
                        return "User not found!".ToResponse(404);
                    var stream = image.ToWebpStream();
                    Response.Headers.CacheControl = "no-store";
                    return File(stream, "image/webp");
                }
                finally
                {
                    Drawing.Release();
                }
            }
            finally
            {
                DrawingFor.TryRemove(id, out _);
            }
        }

        /// <summary>
        /// Układ narysowany przez bota, do sprawdzenia przed zapisem
        /// </summary>
        /// <response code="400">Błędny układ, w treści lista powodów.</response>
        /// <response code="503">Rysowanie zajęte, ponów po czasie z nagłówka Retry-After.</response>
        [HttpPost("discord/{id}/preview"), Authorize(Policy = "Sansite")]
        public async Task<IActionResult> PreviewAsync(ulong id, [FromBody, Required] JToken layoutBody)
        {
            var (layout, error) = ReadLayout(layoutBody);
            if (error != null)
                return error;

            using var db = CreateDatabaseContext();
            var user = await LoadPlayerAsync(db, id);
            if (user == null)
                return "User not found!".ToResponse(404);

            var (errors, _) = await CheckAsync(db, user, layout);
            if (errors.Count > 0)
                return new BadRequestObjectResult(errors);

            var top = await TopAsync(db, id);
            return await DrawAsync(id, () => _profile.GetLayeredProfileImageAsync(db, user, Account(id), top, layout));
        }

        /// <summary>
        /// Obecny profil gracza tak, jak pokazuje go bot
        /// </summary>
        [HttpGet("discord/{id}/image"), Authorize(Policy = "Sansite")]
        public async Task<IActionResult> CurrentImageAsync(ulong id)
        {
            using var db = CreateDatabaseContext();
            var user = await LoadPlayerAsync(db, id);
            if (user == null)
                return "User not found!".ToResponse(404);

            var top = await TopAsync(db, id);
            return await DrawAsync(id, () => _profile.GetLayeredProfileImageAsync(db, user, Account(id), top));
        }

        /// <summary>
        /// Zapis układu; brakujące odblokowania są opłacane jednorazowo w wybranej walucie
        /// </summary>
        /// <remarks>
        /// Wymaga Idempotency-Key: powtórka z tym samym kluczem w ciągu 48 h nie pobiera opłaty drugi raz.
        /// Version musi być tą z odczytu profilu; gdy profil zmienił się w międzyczasie, odpowiedź to 409.
        /// </remarks>
        /// <response code="400">Błędny układ (w treści lista powodów) albo brak klucza.</response>
        /// <response code="402">Za mało SC albo TC.</response>
        /// <response code="404">User not found</response>
        /// <response code="409">Profil zmienił się w międzyczasie albo zapis z tym kluczem trwa.</response>
        /// <response code="422">Klucz został użyty z innym zapisem.</response>
        [HttpPut("discord/{id}"), Authorize(Policy = "Sansite")]
        public async Task<IActionResult> SaveAsync(ulong id, [FromBody, Required] ProfileSaveRequest request,
            [FromHeader(Name = Services.Idempotency.KeyHeader)] string idempotencyKey = null)
        {
            if (idempotencyKey == null)
                return ApiResponses.KeyRequired();
            if (!Services.Idempotency.IsValidKey(idempotencyKey))
                return "Invalid Idempotency-Key".ToResponse(400);

            var (layout, error) = ReadLayout(request.Layout);
            if (error != null)
                return error;

            CurrencyType currency;
            switch (request.Currency?.ToLowerInvariant())
            {
                case null: case "sc": currency = CurrencyType.SC; break;
                case "tc": currency = CurrencyType.TC; break;
                default: return "Unknown currency".ToResponse(400);
            }

            var json = layout.ToJson();
            var record = new IdempotencyRecord
            {
                Scope = "profile",
                Key = idempotencyKey,
                OwnerId = id,
                RequestHash = Services.Idempotency.Hash(new { json, currency, request.Version }),
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
                using (var db = CreateDatabaseContext())
                {
                    var done = await Services.Idempotency.FindAsync(db, record.Scope, idempotencyKey, record.CreatedAt);
                    if (done != null)
                    {
                        if (!Services.Idempotency.SameRequest(done, record))
                            return "Idempotency-Key was used with a different request".ToResponse(422);

                        Response.Headers[Services.Idempotency.ReplayedHeader] = "true";
                        return new OkObjectResult(JsonConvert.DeserializeObject<ProfileSaveView>(done.Result ?? "{}"));
                    }

                    if (!await db.Users.AsQueryable().AnyAsync(x => x.Id == id))
                        return "User not found!".ToResponse(404);
                }

                IActionResult result = null;
                var exe = new Executable($"api-profile u{id}", new Func<Task>(async () =>
                {
                    try
                    {
                        result = await SaveNowAsync(id, layout, json, currency, request.Version, record, () => committed = true);
                    }
                    finally
                    {
                        Services.Idempotency.End(record);
                    }
                }), id, Priority.High);

                if (!await _executor.TryAdd(exe, TimeSpan.FromSeconds(1)))
                    return ApiResponses.QueueFull(Response);
                handedOff = true;

                if (!await exe.WaitAsync(Executable.ApiMaxWait))
                    return "Command timed out".ToResponse(504);

                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError($"API: profile save u{id}: {ex.Message}");
                return committed ? "Profile saved".ToResponse(200) : "Profile not saved".ToResponse(500);
            }
            finally
            {
                if (!handedOff)
                    Services.Idempotency.End(record);
            }
        }

        private async Task<IActionResult> SaveNowAsync(ulong id, ProfileLayout layout, string json, CurrencyType currency, int version,
            IdempotencyRecord record, Action committed)
        {
            using var db = CreateDatabaseContext();
            var user = await db.Users.AsQueryable().Include(x => x.GameDeck).FirstOrDefaultAsync(x => x.Id == id);
            if (user == null)
                return "User not found!".ToResponse(404);

            var stored = await _profile.Store.LoadAsync(db, user);
            if (stored.Version != version)
                return "Profile changed in the meantime".ToResponse(409);

            var (errors, missing) = await CheckAsync(db, user, layout);
            if (errors.Count > 0)
                return new BadRequestObjectResult(errors);

            var cost = ProfileRules.Cost(missing, currency);
            if (cost > 0)
            {
                var wallet = currency == CurrencyType.TC ? user.TcCnt : user.ScCnt;
                if (wallet < cost)
                    return $"Not enough {currency}".ToResponse(402);

                if (currency == CurrencyType.TC) user.TcCnt -= cost;
                else user.ScCnt -= cost;
            }

            var now = _time.Now();
            foreach (var feature in missing)
            {
                db.ProfileUnlocks.Add(new ProfileUnlock
                {
                    UserId = id, Feature = feature.Key, Source = ProfileUnlockSource.Bought, Currency = currency,
                    Price = currency == CurrencyType.TC ? feature.Tc : feature.Sc, CreatedAt = now,
                });
            }

            var row = await db.UserProfiles.FirstOrDefaultAsync(x => x.UserId == id);
            if (row == null)
            {
                row = new UserProfile { UserId = id, Version = 0 };
                db.UserProfiles.Add(row);
            }
            row.Layout = json;
            row.Version++;
            row.UpdatedAt = now;

            var files = await RemoveUnusedPicturesAsync(db, id, layout);

            var view = new ProfileSaveView { Version = row.Version, Currency = currency.ToString().ToLowerInvariant(), Paid = cost, Unlocked = missing.Select(x => x.Key).ToList() };
            record.Result = JsonConvert.SerializeObject(view);
            db.IdempotencyRecords.Add(record);
            await db.SaveChangesAsync();
            committed();

            QueryCache.ExpireTag(new[] { CacheTags.User(id) });
            await DeleteFilesAsync(db, files);
            try { System.IO.File.Delete($"{Dir.FakeCardsAsProfiles}/{id}.webp"); }
            catch (IOException) { }
            await Services.Idempotency.CleanupIfDueAsync(db, record.CreatedAt);

            return new OkObjectResult(view);
        }
    }
}
