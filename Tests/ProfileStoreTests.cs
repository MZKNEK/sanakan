using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sanakan.Database;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services;
using Sanakan.Services.PocketWaifu;
using Sanakan.Services.Profiles;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Artifacts
{
    public class ProfileStoreTests : IDisposable
    {
        private readonly SqliteConnection _connection = new SqliteConnection("Data Source=:memory:");
        private readonly FixedTime _time = new FixedTime();
        private readonly ProfileStore _store;
        private readonly string _dir = $"{Dir.SavedData}/test-store-{Guid.NewGuid():N}";

        private sealed class TestDatabase : DatabaseContext
        {
            private readonly SqliteConnection _connection;
            public TestDatabase(SqliteConnection connection) : base(new FakeConfig()) => _connection = connection;
            protected override void OnConfiguring(DbContextOptionsBuilder builder) => builder.UseSqlite(_connection);
        }

        public ProfileStoreTests()
        {
            _connection.Open();
            Sanakan.Database.SqliteText.RegisterPow(_connection);
            using (var db = Open())
                db.Database.EnsureCreated();
            Directory.CreateDirectory(_dir);
            Directory.CreateDirectory(Dir.ProfilePictures);
            _store = new ProfileStore(new ImageProcessing(null, new TagIcon(7, "galeria", "x"), true), _time);
        }

        public void Dispose()
        {
            _connection.Dispose();
            try { Directory.Delete(_dir, true); } catch (IOException) { }
            foreach (var file in Directory.GetFiles(Dir.ProfilePictures, "910*"))
                File.Delete(file);
        }

        private DatabaseContext Open() => new TestDatabase(_connection);

        private string Picture(string name, int w, int h)
        {
            var path = $"{_dir}/{name}";
            using var image = new Image<Rgba32>(w, h, new Rgba32(10, 20, 30, 255));
            image.SaveAsPng(path);
            return path;
        }

        private async Task<User> AddUserAsync(ulong id, Action<User> change = null)
        {
            using var db = Open();
            var user = new User().Default(id, _time.Now());
            change?.Invoke(user);
            db.Users.Add(user);
            await db.SaveChangesAsync();
            return user;
        }

        private async Task<StoredProfile> LoadAsync(ulong id)
        {
            using var db = Open();
            var user = await db.Users.AsNoTracking().Include(x => x.GameDeck).FirstAsync(x => x.Id == id);
            return await _store.LoadAsync(db, user);
        }

        [Fact]
        public async Task AFreshProfileGetsTheDefaultAndStoresNothing()
        {
            await AddUserAsync(91001);

            var profile = await LoadAsync(91001);

            Assert.Equal(0, profile.Version);
            Assert.Equal(ProfileCatalog.Default().ToJson(), profile.Layout.ToJson());
            using var db = Open();
            Assert.False(await db.UserProfiles.AnyAsync());
            Assert.False(await db.ProfileUnlocks.AnyAsync());
        }

        [Fact]
        public async Task AnOldProfileIsConvertedOnceWithItsPicturesAndUnlocks()
        {
            var bg = Picture("BG.png", 750, 160);
            var ov = Picture("OV.png", 750, 402);
            await AddUserAsync(91002, u =>
            {
                u.BackgroundProfileUri = bg;
                u.CustomProfileOverlayUrl = ov;
                u.AvatarBorder = AvatarBorder.Fire;
            });

            var first = await LoadAsync(91002);
            var again = await LoadAsync(91002);

            Assert.True(first.Converted);
            Assert.Equal(1, again.Version);
            Assert.False(again.Converted);
            Assert.Equal(first.Layout.ToJson(), again.Layout.ToJson());

            using var db = Open();
            var pictures = (await db.ProfilePictures.ToListAsync()).OrderBy(x => x.Id).ToList();
            Assert.Equal(new[] { (ProfileImageUse.Header, 750, 160), (ProfileImageUse.Overlay, 750, 402) }, pictures.Select(x => (x.Use, x.Width, x.Height)));
            Assert.Equal((long)pictures[0].Id, again.Layout.Header.Image);
            Assert.Equal((long)pictures[1].Id, again.Layout.LayersOf<OverlayLayer>().Single().Image);

            var unlocks = await db.ProfileUnlocks.Where(x => x.UserId == 91002).ToListAsync();
            Assert.Equal(new[] { "frame:Fire", "headerImg", "overlay:1" }, unlocks.Select(x => x.Feature).OrderBy(x => x, StringComparer.Ordinal));
            Assert.All(unlocks, x => Assert.Equal(ProfileUnlockSource.Migration, x.Source));

            var rules = ProfileStore.RulesContext(pictures, _ => true);
            Assert.Empty(ProfileRules.Validate(again.Layout, rules));
            Assert.Empty(ProfileRules.MissingUnlocks(again.Layout, unlocks.Select(x => x.Feature)));
        }

        [Fact]
        public async Task OldPicturesAreStretchedAsTheOldProfileStretchedThem()
        {
            var bg = Picture("BG-narrow.png", 104, 160);
            var ov = $"{_dir}/OV-moving.gif";
            using (var gif = new Image<Rgba32>(300, 200, new Rgba32(200, 0, 0, 255)))
            {
                using var second = new Image<Rgba32>(300, 200, new Rgba32(0, 200, 0, 255));
                gif.Frames.AddFrame(second.Frames.RootFrame);
                gif.SaveAsGif(ov);
            }
            await AddUserAsync(91010, u =>
            {
                u.BackgroundProfileUri = bg;
                u.CustomProfileOverlayUrl = ov;
            });

            await LoadAsync(91010);

            using var db = Open();
            var pictures = await db.ProfilePictures.Where(x => x.UserId == 91010).ToListAsync();
            var header = pictures.Single(x => x.Use == ProfileImageUse.Header);
            var overlay = pictures.Single(x => x.Use == ProfileImageUse.Overlay);
            Assert.Equal((750, 160), (header.Width, header.Height));
            Assert.Equal((750, 402, true), (overlay.Width, overlay.Height, overlay.Animated));
            Assert.All(pictures, x => Assert.StartsWith(Dir.ProfilePictures, x.File));
            Assert.Equal((104, 160), (Image.Identify(bg).Width, Image.Identify(bg).Height));
        }

        [Fact]
        public async Task AMissingPictureLeavesNoLayer()
        {
            await AddUserAsync(91003, u => u.CustomProfileOverlayUrl = $"{_dir}/gone.webp");

            var profile = await LoadAsync(91003);

            Assert.Empty(profile.Layout.LayersOf<OverlayLayer>());
            using var db = Open();
            Assert.False(await db.ProfilePictures.AnyAsync());
        }

        [Fact]
        public async Task TheMiniWaifuBecomesASticker()
        {
            var user = await AddUserAsync(91004, u => u.StatsStyleSettings |= ProfileSettings.ShowWaifu);
            using (var db = Open())
            {
                var deck = await db.GameDecks.FirstAsync(x => x.UserId == 91004);
                deck.Waifu = 555;
                db.Cards.Add(new Card { Name = "a", Title = "b", Character = 555, Rarity = Rarity.A, GameDeckId = deck.Id, CreationDate = _time.Now() });
                db.Cards.Add(new Card { Name = "a", Title = "b", Character = 555, Rarity = Rarity.SSS, GameDeckId = deck.Id, CreationDate = _time.Now() });
                await db.SaveChangesAsync();
            }

            var profile = await LoadAsync(91004);

            using var check = Open();
            var best = await check.Cards.Where(x => x.Character == 555).OrderBy(x => x.Rarity).Select(x => x.Id).FirstAsync();
            Assert.Equal(best, profile.Layout.LayersOf<StickerLayer>().Single().Card);
        }

        [Fact]
        public async Task MigratingAllConvertsOnlyWhatIsMissing()
        {
            await AddUserAsync(91005);
            await AddUserAsync(91006, u => u.ProfileType = ProfileType.Cards);
            await AddUserAsync(91007, u => u.StatsStyleSettings |= ProfileSettings.BarOnTop);

            var report = await _store.MigrateAllAsync(Open);
            var again = await _store.MigrateAllAsync(Open);

            Assert.Equal(2, report.Candidates);
            Assert.Equal(2, report.Converted);
            Assert.Equal(0, report.Failed);
            Assert.Equal(0, again.Candidates);
            using var db = Open();
            Assert.Equal(new ulong[] { 91006, 91007 }, (await db.UserProfiles.Select(x => x.UserId).ToListAsync()).OrderBy(x => x));
            Assert.Contains(await db.ProfileUnlocks.Where(x => x.UserId == 91007).Select(x => x.Feature).ToListAsync(), x => x == ProfileCatalog.BarPlace);
        }

        [Fact]
        public async Task RemovingThePlayerRemovesTheProfile()
        {
            await AddUserAsync(91008, u => u.AvatarBorder = AvatarBorder.Gold);
            await LoadAsync(91008);

            using (var db = Open())
            {
                db.Users.Remove(await db.Users.FirstAsync(x => x.Id == 91008));
                await db.SaveChangesAsync();
            }

            using var check = Open();
            Assert.False(await check.UserProfiles.AnyAsync(x => x.UserId == 91008));
            Assert.False(await check.ProfileUnlocks.AnyAsync(x => x.UserId == 91008));
        }

        [Fact]
        public async Task ResettingGivesTheDefaultLayoutAndKeepsWhatWasPaid()
        {
            var bg = Picture("BG9.png", 750, 160);
            await AddUserAsync(91009, u =>
            {
                u.BackgroundProfileUri = bg;
                u.AvatarBorder = AvatarBorder.Fire;
            });

            using (var db = Open())
                await _store.ResetAsync(db, await db.Users.Include(x => x.GameDeck).FirstAsync(x => x.Id == 91009), removePictures: false);

            var kept = await LoadAsync(91009);
            Assert.Equal(2, kept.Version);
            Assert.Equal(ProfileCatalog.Default().ToJson(), kept.Layout.ToJson());
            string file;
            using (var db = Open())
            {
                Assert.Equal(new[] { "frame:Fire", "headerImg" }, (await db.ProfileUnlocks.Where(x => x.UserId == 91009).Select(x => x.Feature).ToListAsync()).OrderBy(x => x, StringComparer.Ordinal));
                file = (await db.ProfilePictures.SingleAsync(x => x.UserId == 91009)).File;
                Assert.True(File.Exists(file));

                await _store.ResetAsync(db, await db.Users.Include(x => x.GameDeck).FirstAsync(x => x.Id == 91009), removePictures: true);
            }

            using var check = Open();
            Assert.Equal(3, (await check.UserProfiles.SingleAsync(x => x.UserId == 91009)).Version);
            Assert.False(await check.ProfilePictures.AnyAsync(x => x.UserId == 91009));
            Assert.Equal(2, await check.ProfileUnlocks.CountAsync(x => x.UserId == 91009));
            Assert.False(File.Exists(file));
        }
    }
}
