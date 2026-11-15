#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Sanakan.Database;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using SixLabors.ImageSharp;

namespace Sanakan.Services.Profiles
{
    public class StoredProfile
    {
        public ProfileLayout Layout { get; set; }
        // 0 while nothing is stored: the default layout or an old profile not converted yet
        public int Version { get; set; }
        public bool Converted { get; set; }
    }

    // Reads and keeps the profiles of layers: the layout, the unlocks and the pictures of a player.
    // An old profile is converted the first time it is read, or all at once by MigrateAllAsync.
    public class ProfileStore
    {
        private readonly ImageProcessing _img;
        private readonly Time.ISystemTime _time;

        public ProfileStore(ImageProcessing img, Time.ISystemTime time)
        {
            _img = img;
            _time = time;
        }

        public static async Task<HashSet<string>> GetUnlocksAsync(DatabaseContext db, ulong userId)
            => (await db.ProfileUnlocks.AsNoTracking().Where(x => x.UserId == userId).Select(x => x.Feature).ToListAsync()).ToHashSet();

        public static async Task<List<ProfilePicture>> GetPicturesAsync(DatabaseContext db, ulong userId)
            => await db.ProfilePictures.AsNoTracking().Where(x => x.UserId == userId).ToListAsync();

        public static ProfileRulesContext RulesContext(IEnumerable<ProfilePicture> pictures, Func<ulong, bool> ownsCard)
        {
            var byId = pictures.ToDictionary(x => (long)x.Id);
            return new ProfileRulesContext
            {
                Image = id => byId.TryGetValue(id, out var p) ? new ProfileImageInfo { Id = id, Width = p.Width, Height = p.Height, Animated = p.Animated } : null,
                OwnsCard = ownsCard,
            };
        }

        // the layout to draw; an old customised profile is converted and stored on the way
        public async Task<StoredProfile> LoadAsync(DatabaseContext db, User user)
        {
            var row = await db.UserProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == user.Id);
            if (row != null)
                return new StoredProfile { Layout = ProfileLayout.FromJson(row.Layout), Version = row.Version };

            if (!ProfileLegacy.IsCustomised(user))
                return new StoredProfile { Layout = ProfileCatalog.Default() };

            return await ConvertAsync(db, user);
        }

        // the waifu card of the old mini waifu; the deck's cards need not be loaded
        private static async Task<ulong> WaifuCardIdAsync(DatabaseContext db, GameDeck deck)
        {
            if (deck == null || (deck.Waifu == 0 && deck.PremiumWaifu == 0))
                return 0;

            if (deck.PremiumWaifu != 0)
                return await db.Cards.AsNoTracking().AnyAsync(x => x.Id == deck.PremiumWaifu && x.GameDeckId == deck.Id) ? deck.PremiumWaifu : 0;

            var card = await db.Cards.AsNoTracking().Where(x => x.GameDeckId == deck.Id && x.Character == deck.Waifu)
                .OrderBy(x => x.Rarity).ThenByDescending(x => x.Quality).Select(x => x.Id).FirstOrDefaultAsync();
            return card;
        }

        public async Task<StoredProfile> ConvertAsync(DatabaseContext db, User user)
        {
            var deck = user.GameDeck ?? await db.GameDecks.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == user.Id);
            var waifu = Has(user, ProfileSettings.ShowWaifu) ? await WaifuCardIdAsync(db, deck) : 0;

            // every picture the old profile uses, kept once each
            var pictures = new Dictionary<string, ProfilePicture>();
            async Task Register(ProfileImageUse use, string path)
            {
                if (string.IsNullOrEmpty(path) || pictures.ContainsKey(path))
                    return;
                pictures[path] = await RegisterLegacyAsync(user.Id, use, path);
            }

            var styleUsed = ProfileLegacy.HasStyleImage(user.StatsReplacementProfileUri) && user.ProfileType != ProfileType.Stats
                && user.ProfileType != ProfileType.Cards && user.ProfileType != ProfileType.MiniGallery;
            if (!ProfileLegacy.IsDefaultBackground(user.BackgroundProfileUri)) await Register(ProfileImageUse.Header, user.BackgroundProfileUri);
            if (styleUsed) await Register(user.ProfileType == ProfileType.StatsWithImg ? ProfileImageUse.Overlay : ProfileImageUse.Style, user.StatsReplacementProfileUri);
            await Register(ProfileImageUse.Overlay, user.CustomProfileOverlayUrl);
            await Register(ProfileImageUse.Ultra, user.PremiumCustomProfileOverlayUrl);

            var found = pictures.Values.Where(x => x != null).ToList();
            db.ProfilePictures.AddRange(found);
            await db.SaveChangesAsync();

            var legacy = ProfileLegacy.FromUser(user, waifu, (use, path) => pictures.TryGetValue(path, out var p) && p != null ? (long)p.Id : 0);
            DropMissingPictures(legacy.Layout);

            var now = _time.Now();
            var owned = await GetUnlocksAsync(db, user.Id);
            foreach (var feature in legacy.Unlocks.Where(x => !owned.Contains(x)))
                db.ProfileUnlocks.Add(new ProfileUnlock { UserId = user.Id, Feature = feature, Source = ProfileUnlockSource.Migration, CreatedAt = now });

            db.UserProfiles.Add(new UserProfile { UserId = user.Id, Layout = legacy.Layout.ToJson(), Version = 1, UpdatedAt = now });
            await db.SaveChangesAsync();

            return new StoredProfile { Layout = legacy.Layout, Version = 1, Converted = true };
        }

        private static bool Has(User user, ProfileSettings flag) => user.StatsStyleSettings.HasFlag(flag);

        // a picture that could not be found leaves no layer pointing at it
        private static void DropMissingPictures(ProfileLayout layout)
        {
            if (layout.Header?.Image == 0) layout.Header = null;
            if (layout.Style?.Image == 0) layout.Style = null;
            if (layout.Ultra?.Image == 0) layout.Ultra = null;
            layout.Layers.RemoveAll(x => x is OverlayLayer { Image: 0 });
        }

        private async Task<ProfilePicture> RegisterLegacyAsync(ulong userId, ProfileImageUse use, string path)
        {
            try
            {
                if (Dir.IsLocal(path))
                    return File.Exists(path) ? await DescribeAsync(userId, use, path, _time.Now()) : null;

                // an old link: the picture is downloaded once, as the old commands did when it was set
                var temp = $"{Dir.ProfilePictures}/{userId}_download_{Guid.NewGuid():N}.tmp";
                await _img.SaveImageFromUrlAsync(path, temp, new Size(0, 0));
                return await AddFileAsync(userId, use, temp, _time.Now());
            }
            catch (Exception)
            {
                return null;
            }
        }

        // a picture already in the bot's folders, described as it is
        private static async Task<ProfilePicture> DescribeAsync(ulong userId, ProfileImageUse use, string path, DateTime now)
        {
            var info = await Image.IdentifyAsync(path);
            return new ProfilePicture
            {
                UserId = userId, Use = use, File = path, Width = info.Width, Height = info.Height,
                Animated = info.FrameMetadataCollection.Count > 1, Sha256 = await HashAsync(path), CreatedAt = now,
            };
        }

        // a new file of a player moved under its own name, <user>_<start of its SHA-256>, kept once whatever its source
        public static async Task<ProfilePicture> AddFileAsync(ulong userId, ProfileImageUse use, string temporary, DateTime now)
        {
            var hash = await HashAsync(temporary);
            var extension = Path.GetExtension(temporary) is ".tmp" or "" ? await ExtensionOfAsync(temporary) : Path.GetExtension(temporary);
            var target = $"{Dir.ProfilePictures}/{userId}_{hash.Substring(0, 16)}{extension}";
            if (File.Exists(target)) File.Delete(temporary);
            else File.Move(temporary, target);
            return await DescribeAsync(userId, use, target, now);
        }

        private static async Task<string> ExtensionOfAsync(string path)
        {
            var format = await Image.DetectFormatAsync(path);
            return "." + (format?.FileExtensions.FirstOrDefault() ?? "webp");
        }

        private static async Task<string> HashAsync(string path)
        {
            await using var stream = File.OpenRead(path);
            return Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
        }

        // ---- all at once ----

        public sealed class MigrationReport
        {
            public int Candidates { get; set; }
            public int Converted { get; set; }
            public int Failed { get; set; }
        }

        // the ids of the players whose old profile differs from a fresh one and has no layout yet
        public static async Task<List<ulong>> PendingConversionsAsync(DatabaseContext db)
            => await db.Users.AsNoTracking()
                .Where(u => !db.UserProfiles.Any(p => p.UserId == u.Id))
                .Where(u => (u.BackgroundProfileUri != null && u.BackgroundProfileUri != "" && !u.BackgroundProfileUri.EndsWith("np/pbg.png"))
                    || u.ProfileType != ProfileType.Stats
                    || (u.CustomProfileOverlayUrl != null && u.CustomProfileOverlayUrl != "")
                    || (u.PremiumCustomProfileOverlayUrl != null && u.PremiumCustomProfileOverlayUrl != "")
                    || u.AvatarBorder != AvatarBorder.Base
                    || u.StatsStyleSettings != ProfileSettings.Default)
                .Select(u => u.Id).ToListAsync();

        // converts every old customised profile; run again, it only takes the ones still missing
        public async Task<MigrationReport> MigrateAllAsync(Func<DatabaseContext> createDb, Action<int, int> progress = null)
        {
            List<ulong> ids;
            using (var db = createDb())
                ids = await PendingConversionsAsync(db);

            var report = new MigrationReport { Candidates = ids.Count };
            foreach (var id in ids)
            {
                try
                {
                    using var db = createDb();
                    var user = await db.Users.AsNoTracking().Include(x => x.GameDeck).FirstOrDefaultAsync(x => x.Id == id);
                    if (user == null || await db.UserProfiles.AnyAsync(x => x.UserId == id))
                        continue;

                    await ConvertAsync(db, user);
                    report.Converted++;
                    QueryCache.ExpireTag(new[] { CacheTags.User(id) });
                }
                catch (Exception)
                {
                    report.Failed++;
                }
                progress?.Invoke(report.Converted + report.Failed, ids.Count);
            }
            return report;
        }
    }
}
