using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using Sanakan.Api.Controllers;
using Sanakan.Api.Models;
using Sanakan.Database;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services;
using Sanakan.Services.Executor;
using Sanakan.Services.PocketWaifu;
using Sanakan.Services.Profiles;
using Sanakan.Services.Time;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Artifacts
{
    public class ProfileApiTests : IDisposable
    {
        private const ulong PlayerId = 92001;

        private readonly SqliteConnection _connection = new SqliteConnection("Data Source=:memory:");
        private readonly FakeExecutor _executor = new FakeExecutor { RunImmediately = true };
        private readonly FixedTime _time = new FixedTime { Value = new DateTime(2026, 11, 15, 12, 0, 0) };
        private readonly Profile _profile;
        private ulong _cardId;

        private sealed class TestDatabase : DatabaseContext
        {
            private readonly SqliteConnection _connection;
            public TestDatabase(SqliteConnection connection) : base(new FakeConfig()) => _connection = connection;
            protected override void OnConfiguring(DbContextOptionsBuilder builder) => builder.UseSqlite(_connection);
        }

        private sealed class TestProfileController : ProfileController
        {
            private readonly Func<DatabaseContext> _factory;
            public TestProfileController(IExecutor executor, ISystemTime time, Profile profile, Func<DatabaseContext> factory)
                : base(null, new ListLogger(), executor, new FakeConfig(), time, profile) => _factory = factory;
            protected override DatabaseContext CreateDatabaseContext() => _factory();
            protected override Discord.IUser Account(ulong id) => null;
        }

        public ProfileApiTests()
        {
            _connection.Open();
            Sanakan.Database.SqliteText.RegisterPow(_connection);
            using var db = Open();
            db.Database.EnsureCreated();
            var user = new User().Default(PlayerId, _time.Now());
            user.ScCnt = 50000;
            user.TcCnt = 300;
            db.Users.Add(user);
            db.SaveChanges();
            var card = new Card { Name = "a", Title = "b", Character = 77, Rarity = Rarity.A, GameDeckId = PlayerId, CreationDate = _time.Now() };
            db.Cards.Add(card);
            db.SaveChanges();
            _cardId = card.Id;

            Directory.CreateDirectory(Dir.ProfilePictures);
            _profile = new Profile(null, null, new ImageProcessing(null, new TagIcon(7, "galeria", "x"), true), new ListLogger(), new FakeConfig(), _time, _executor);
            Sanakan.QueryCache.ExpireTag(new[] { Sanakan.CacheTags.User(PlayerId) });
        }

        public void Dispose()
        {
            _connection.Dispose();
            foreach (var file in Directory.GetFiles(Dir.ProfilePictures, $"{PlayerId}_*"))
                File.Delete(file);
        }

        private DatabaseContext Open() => new TestDatabase(_connection);

        private TestProfileController Controller() => ControllerHarness.Attach(new TestProfileController(_executor, _time, _profile, Open));

        private static JToken Layout(Action<ProfileLayout> change = null)
        {
            var layout = ProfileCatalog.Default();
            change?.Invoke(layout);
            return JToken.Parse(layout.ToJson());
        }

        private static ProfileSaveRequest Save(JToken layout, string currency = "sc", int version = 0)
            => new ProfileSaveRequest { Layout = layout, Currency = currency, Version = version };

        private async Task<User> PlayerAsync()
        {
            using var db = Open();
            return await db.Users.AsNoTracking().FirstAsync(x => x.Id == PlayerId);
        }

        private static int Status(IActionResult result) => result switch
        {
            ObjectResult o => o.StatusCode ?? 200,
            StatusCodeResult s => s.StatusCode,
            _ => 200,
        };

        [Fact]
        public void TheCatalogHasPricesPresetsAndFrames()
        {
            var catalog = Controller().GetCatalog().Value;

            Assert.Equal(ProfileCatalog.Features.Count, catalog.Features.Count);
            Assert.True(catalog.Features.Single(x => x.Key == ProfileCatalog.HighResolution).TcOnly);
            Assert.All(catalog.Presets, p => Assert.NotNull(ProfileLayout.FromJson(p.Layout.ToString())));
            Assert.Contains(catalog.Frames, f => f.Key == "Base" && f.Free);
            Assert.Contains(catalog.Frames, f => f.Key == "TurqLeaves" && !f.Selectable);
        }

        [Fact]
        public async Task AFreshPlayerReadsTheDefaultLayout()
        {
            var editor = (await Controller().GetEditorAsync(PlayerId)).Value;

            Assert.Equal(0, editor.Version);
            Assert.Equal(ProfileCatalog.Default().ToJson(), ProfileLayout.FromJson(editor.Layout.ToString()).ToJson());
            Assert.Empty(editor.Unlocks);
            Assert.Equal(50000, editor.Data.Sc);
        }

        [Fact]
        public async Task TheQuoteListsWhatIsMissing()
        {
            var quote = (await Controller().QuoteAsync(PlayerId, Layout(l => { l.Accent = "#ff0000"; l.Panels = PanelLook.Glass; }))).Value;

            Assert.Empty(quote.Errors);
            Assert.Equal(new[] { "accent", "panels" }, quote.Missing.Select(x => x.Key).OrderBy(x => x));
            Assert.Equal(18000 + 12000, quote.Sc);
            Assert.Equal(299 + 199, quote.Tc);
            Assert.Equal(50000, quote.WalletSc);
        }

        [Fact]
        public async Task TheProfileOfTwiceTheSizeIsBoughtOnlyWithTc()
        {
            using (var db = Open())
            {
                var user = await db.Users.FirstAsync(x => x.Id == PlayerId);
                user.TcCnt = 6000;
                await db.SaveChangesAsync();
            }
            var layout = Layout(l => l.Hd = true);

            var quote = (await Controller().QuoteAsync(PlayerId, layout)).Value;
            Assert.True(quote.TcOnly);
            Assert.Equal(5000, quote.Tc);

            Assert.Equal(400, Status(await Controller().SaveAsync(PlayerId, Save(layout, "sc"), "key-hd1")));
            Assert.Equal(200, Status(await Controller().SaveAsync(PlayerId, Save(layout, "tc"), "key-hd2")));
            var player = await PlayerAsync();
            Assert.Equal((50000L, 1000L), (player.ScCnt, player.TcCnt));
        }

        [Fact]
        public async Task SavingPaysOnceAndAReplayPaysNothing()
        {
            var layout = Layout(l => { l.Accent = "#ff0000"; l.Panels = PanelLook.Glass; });

            var first = await Controller().SaveAsync(PlayerId, Save(layout), "key-1");
            var replay = Controller();
            var again = await replay.SaveAsync(PlayerId, Save(layout), "key-1");

            Assert.Equal(200, Status(first));
            var view = Assert.IsType<ProfileSaveView>(((ObjectResult)first).Value);
            Assert.Equal(1, view.Version);
            Assert.Equal(30000, view.Paid);
            Assert.Equal(200, Status(again));
            Assert.Equal("true", replay.Response.Headers["Idempotent-Replayed"].ToString());
            Assert.Equal(20000, (await PlayerAsync()).ScCnt);

            using var db = Open();
            var unlocks = await db.ProfileUnlocks.Where(x => x.UserId == PlayerId).ToListAsync();
            Assert.Equal(2, unlocks.Count);
            Assert.All(unlocks, x => Assert.Equal((ProfileUnlockSource.Bought, CurrencyType.SC), (x.Source, x.Currency)));
            Assert.Equal("#ff0000", ProfileLayout.FromJson((await db.UserProfiles.SingleAsync()).Layout).Accent);
        }

        [Fact]
        public async Task OwnedUnlocksAreNotPaidAgain()
        {
            var layout = Layout(l => l.Accent = "#ff0000");
            await Controller().SaveAsync(PlayerId, Save(layout), "key-1");

            var other = Layout(l => l.Accent = "#00ff00");
            var second = await Controller().SaveAsync(PlayerId, Save(other, version: 1), "key-2");

            Assert.Equal(0, Assert.IsType<ProfileSaveView>(((ObjectResult)second).Value).Paid);
            Assert.Equal(50000 - 18000, (await PlayerAsync()).ScCnt);
        }

        [Fact]
        public async Task TheKeyCannotBeReusedForAnotherSave()
        {
            await Controller().SaveAsync(PlayerId, Save(Layout(l => l.Accent = "#ff0000")), "key-1");
            var other = await Controller().SaveAsync(PlayerId, Save(Layout(l => l.Accent = "#00ff00")), "key-1");

            Assert.Equal(422, Status(other));
        }

        [Fact]
        public async Task AStaleVersionIsRefused()
        {
            await Controller().SaveAsync(PlayerId, Save(Layout()), "key-1");
            var stale = await Controller().SaveAsync(PlayerId, Save(Layout(l => l.Accent = "#ff0000"), version: 0), "key-2");

            Assert.Equal(409, Status(stale));
            Assert.Equal(50000, (await PlayerAsync()).ScCnt);
        }

        [Fact]
        public async Task NotEnoughTcChangesNothing()
        {
            var result = await Controller().SaveAsync(PlayerId, Save(Layout(l => { l.Accent = "#ff0000"; l.Panels = PanelLook.Glass; }), "tc"), "key-1");

            Assert.Equal(402, Status(result));
            using var db = Open();
            Assert.False(await db.UserProfiles.AnyAsync());
            Assert.False(await db.ProfileUnlocks.AnyAsync());
            Assert.Equal(300, (await PlayerAsync()).TcCnt);
        }

        [Fact]
        public async Task PayingInTcTakesTc()
        {
            var result = await Controller().SaveAsync(PlayerId, Save(Layout(l => l.Panels = PanelLook.Glass), "tc"), "key-1");

            Assert.Equal(200, Status(result));
            var player = await PlayerAsync();
            Assert.Equal(300 - 199, player.TcCnt);
            Assert.Equal(50000, player.ScCnt);
        }

        [Fact]
        public async Task SomeoneElsesCardCannotBeASticker()
        {
            var foreign = Layout(l => l.Layers.Add(new StickerLayer { Id = 30, Card = _cardId + 1000, X = 10, Y = 10 }));
            var own = Layout(l => l.Layers.Add(new StickerLayer { Id = 30, Card = _cardId, X = 10, Y = 10 }));

            var refused = await Controller().SaveAsync(PlayerId, Save(foreign), "key-1");
            var saved = await Controller().SaveAsync(PlayerId, Save(own), "key-2");

            Assert.Equal(400, Status(refused));
            Assert.Equal(200, Status(saved));
            Assert.Equal(new[] { "sticker:1" }, Assert.IsType<ProfileSaveView>(((ObjectResult)saved).Value).Unlocked);
        }

        [Fact]
        public async Task ASaveNeedsAKeyAndAKnownCurrency()
        {
            Assert.Equal(400, Status(await Controller().SaveAsync(PlayerId, Save(Layout()), null)));
            Assert.Equal(400, Status(await Controller().SaveAsync(PlayerId, Save(Layout(), "ac"), "key-1")));
            Assert.Equal(400, Status(await Controller().SaveAsync(PlayerId, Save(JToken.Parse("[1]")), "key-2")));
        }

        private static IFormFile Upload(byte[] bytes, string name = "a.png")
            => new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", name) { Headers = new HeaderDictionary(), ContentType = "image/png" };

        private static byte[] Png(int w, int h, byte shade)
        {
            using var image = new Image<Rgba32>(w, h, new Rgba32(shade, 20, 30, 255));
            using var stream = new MemoryStream();
            image.SaveAsPng(stream);
            return stream.ToArray();
        }

        [Fact]
        public async Task UploadedPicturesAreKeptOnceAndUsable()
        {
            var bytes = Png(300, 200, 40);
            var first = (await Controller().UploadPictureAsync(PlayerId, Upload(bytes), "overlay")).Value;
            var same = (await Controller().UploadPictureAsync(PlayerId, Upload(bytes), "overlay")).Value;
            var notImage = await Controller().UploadPictureAsync(PlayerId, Upload(new byte[] { 1, 2, 3, 4, 5 }), "overlay");

            Assert.Equal((300, 200), (first.Width, first.Height));
            Assert.Equal(first.Id, same.Id);
            Assert.Equal(400, Status(notImage.Result));

            var layout = Layout(l => l.Layers.Add(new OverlayLayer { Id = 30, Image = (long)first.Id, X = 100, Y = 100 }));
            var saved = await Controller().SaveAsync(PlayerId, Save(layout), "key-1");

            Assert.Equal(200, Status(saved));
            Assert.Equal(new[] { "overlay:1" }, Assert.IsType<ProfileSaveView>(((ObjectResult)saved).Value).Unlocked);
            var picture = Assert.IsType<FileStreamResult>(await Controller().GetPictureAsync(PlayerId, first.Id));
            picture.FileStream.Dispose();
            Assert.Equal("image/png", picture.ContentType);
            Assert.Equal(404, Status(await Controller().GetPictureAsync(PlayerId + 1, first.Id)));
        }

        [Fact]
        public async Task APictureIsDeletedOnlyWhenTheSavedProfileDoesNotUseIt()
        {
            var used = (await Controller().UploadPictureAsync(PlayerId, Upload(Png(100, 100, 30)), "overlay")).Value;
            var spare = (await Controller().UploadPictureAsync(PlayerId, Upload(Png(100, 100, 70)), "overlay")).Value;
            var saved = await Controller().SaveAsync(PlayerId, Save(Layout(l => l.Layers.Add(new OverlayLayer { Id = 30, Image = (long)used.Id, X = 10, Y = 200 }))), "key-d1");
            Assert.Equal(200, Status(saved));

            Assert.Equal(409, Status(await Controller().DeletePictureAsync(PlayerId, used.Id)));
            Assert.Equal(404, Status(await Controller().DeletePictureAsync(PlayerId + 1, spare.Id)));
            Assert.Equal(200, Status(await Controller().DeletePictureAsync(PlayerId, spare.Id)));
            Assert.Equal(404, Status(await Controller().DeletePictureAsync(PlayerId, spare.Id)));

            using var db = Open();
            Assert.Equal(new[] { used.Id }, await db.ProfilePictures.Select(x => x.Id).ToListAsync());
            Assert.Single(Directory.GetFiles(Dir.ProfilePictures, $"{PlayerId}_*"));
        }

        [Fact]
        public async Task ReplacedPicturesGoAwayAfterAnHour()
        {
            var old = (await Controller().UploadPictureAsync(PlayerId, Upload(Png(100, 100, 50)), "overlay")).Value;
            await Controller().SaveAsync(PlayerId, Save(Layout(l => l.Layers.Add(new OverlayLayer { Id = 30, Image = (long)old.Id, X = 10, Y = 200 }))), "key-1");

            _time.Value = _time.Value.AddHours(2);
            var fresh = (await Controller().UploadPictureAsync(PlayerId, Upload(Png(100, 100, 90)), "overlay")).Value;
            var saved = await Controller().SaveAsync(PlayerId, Save(Layout(l => l.Layers.Add(new OverlayLayer { Id = 30, Image = (long)fresh.Id, X = 10, Y = 200 })), version: 1), "key-2");

            Assert.Equal(200, Status(saved));
            using var db = Open();
            Assert.Equal(new[] { fresh.Id }, await db.ProfilePictures.Select(x => x.Id).ToListAsync());
            Assert.Single(Directory.GetFiles(Dir.ProfilePictures, $"{PlayerId}_*"));
        }
    }
}
