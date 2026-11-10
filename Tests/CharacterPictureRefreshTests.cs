using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Sanakan;
using Sanakan.Config.Model;
using Sanakan.Database;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services;
using Sanakan.Services.PocketWaifu;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Artifacts
{
    // a card keeps the picture url from its creation; when Shinden replaced the picture, the current one is used and saved
    public class CharacterPictureRefreshTests : IDisposable
    {
        private readonly ConcurrentDictionary<string, int> _requests = new ConcurrentDictionary<string, int>();
        private readonly LocalHttpServer _server;
        private readonly ListLogger _logger = new ListLogger();
        private readonly List<(ulong Character, string Old, string New)> _moves = new List<(ulong, string, string)>();
        private int _lookups;

        public CharacterPictureRefreshTests()
        {
            var png = Png();
            // new.png and its other extensions exist, every other picture is gone; old500 fails on the server side
            _server = new LocalHttpServer(async ctx =>
            {
                var path = ctx.Request.Url.AbsolutePath.TrimStart('/');
                _requests.AddOrUpdate(path, 1, (_, v) => v + 1);
                if (path.StartsWith("new."))
                {
                    ctx.Response.ContentType = "image/png";
                    await ctx.Response.OutputStream.WriteAsync(png);
                }
                else
                {
                    ctx.Response.StatusCode = path.StartsWith("old500") ? 500 : 404;
                }
                ctx.Response.Close();
            });
        }

        public void Dispose() => _server.Dispose();

        private static byte[] Png()
        {
            using var image = new Image<Rgba32>(40, 60, new Rgba32(200, 30, 30));
            using var ms = new MemoryStream();
            image.SaveAsPng(ms);
            return ms.ToArray();
        }

        private string Url(string name) => _server.BaseUrl + name;

        private int RequestsFor(string stem) => _requests.Where(x => x.Key.StartsWith(stem + ".")).Sum(x => x.Value);

        private ImageProcessing Images(string current) => new ImageProcessing(null, null, true, _logger)
        {
            CurrentCharacterPicture = _ =>
            {
                Interlocked.Increment(ref _lookups);
                return Task.FromResult(current);
            },
            CharacterPictureMoved = (character, oldUrl, newUrl) =>
            {
                lock (_moves) _moves.Add((character, oldUrl, newUrl));
                return Task.CompletedTask;
            },
        };

        [Fact]
        public async Task GonePicture_IsReplacedByTheCurrentOne_AndSaved()
        {
            var images = Images(Url("new.png"));

            using var picture = await images.LoadCharacterPictureAsync(Url("old.jpg"), 1, characterId: 77);

            Assert.NotNull(picture);
            Assert.Equal(40, picture.Width);
            Assert.Equal((77ul, Url("old.jpg"), Url("new.png")), Assert.Single(_moves));
            // the gone picture is asked once with each extension, not retried
            Assert.Equal(5, RequestsFor("old"));
            var message = Assert.Single(_logger.Messages);
            Assert.Equal(LogKind.Routine, LoggerExtensions.Parse(message).Kind);
            Assert.Contains("new picture", message);
        }

        [Fact]
        public async Task OtherCardsWithTheOldUrl_GoStraightToTheCurrentPicture()
        {
            var images = Images(Url("new.png"));
            using (await images.LoadCharacterPictureAsync(Url("old.jpg"), 1, characterId: 77)) { }

            using var second = await images.LoadCharacterPictureAsync(Url("old.jpg"), 2, characterId: 77);

            Assert.NotNull(second);
            Assert.Equal(5, RequestsFor("old"));
            Assert.Equal(1, _lookups);
        }

        [Fact]
        public async Task PictureSetByThePlayer_IsNotReplaced()
        {
            var images = Images(Url("new.png"));

            var picture = await images.LoadCharacterPictureAsync(Url("old.jpg"), 1);

            Assert.Null(picture);
            Assert.Equal(0, _lookups);
            Assert.Empty(_moves);
        }

        [Fact]
        public async Task ServerError_IsNotTreatedAsAReplacedPicture()
        {
            var images = Images(Url("new.png"));

            var picture = await images.LoadCharacterPictureAsync(Url("old500.jpg"), 1, characterId: 77);

            Assert.Null(picture);
            Assert.Equal(0, _lookups);
            Assert.Empty(_moves);
            Assert.Equal(LogKind.Error, LoggerExtensions.Parse(Assert.Single(_logger.Messages)).Kind);
        }

        [Theory]
        [InlineData("old.jpg")]
        [InlineData("gone.jpg")]
        [InlineData(null)]
        public async Task NoUsableCurrentPicture_LeavesTheCardAlone(string current)
        {
            // the same url, a current picture that is gone too, or no picture on Shinden
            var images = Images(current == null ? null : Url(current));

            var picture = await images.LoadCharacterPictureAsync(Url("old.jpg"), 1, characterId: 77);

            Assert.Null(picture);
            Assert.Empty(_moves);
            Assert.All(_logger.Messages, x => Assert.Equal(LogKind.Routine, LoggerExtensions.Parse(x).Kind));
        }
    }

    public class CharacterPictureSaveTests : IDisposable
    {
        private const string OldUrl = "http://cdn.shinden.eu/cdn1/images/genuine/1.jpg";
        private const string NewUrl = "http://cdn.shinden.eu/cdn1/images/genuine/2.jpg";

        private readonly string _path = Path.Combine(Path.GetTempPath(), $"sanakan-picmove-{Guid.NewGuid():N}.db");
        private readonly FakeConfig _config = new FakeConfig();

        public CharacterPictureSaveTests()
        {
            _config.Model.DatabaseProvider = DatabaseProvider.Sqlite;
            _config.Model.ConnectionString = $"Data Source={_path}";
        }

        public void Dispose()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(_path); } catch (IOException) { }
        }

        [Fact]
        public async Task OnlyCardsOfThatCharacterWithTheOldUrl_GetTheNewOne()
        {
            using (var db = new DatabaseContext(_config))
            {
                db.EnsureReady();
                var user = new User().Default(20, new DateTime(2026, 1, 1));
                foreach (var (id, character, image) in new[] { (1ul, 77ul, OldUrl), (2ul, 77ul, OldUrl), (3ul, 77ul, "http://cdn.shinden.eu/cdn1/images/genuine/9.jpg"), (4ul, 78ul, OldUrl) })
                    user.GameDeck.Cards.Add(new Card { Id = id, Character = character, Image = image, Name = "x", Rarity = Rarity.A, Tags = new List<Tag>(), CreationDate = new DateTime(2026, 1, 1) });
                db.Users.Add(user);
                db.SaveChanges();
            }

            var waifu = new Waifu(null, null, null, new ListLogger(), null, null, null, new FixedTime(), null, null, _config);
            await waifu.MoveCharacterPictureAsync(77, OldUrl, NewUrl);

            using (var db = new DatabaseContext(_config))
            {
                var images = db.Cards.AsNoTracking().ToList().ToDictionary(x => x.Id, x => x.Image);
                Assert.Equal(NewUrl, images[1]);
                Assert.Equal(NewUrl, images[2]);
                Assert.Equal("http://cdn.shinden.eu/cdn1/images/genuine/9.jpg", images[3]);
                Assert.Equal(OldUrl, images[4]);
            }
        }
    }
}
