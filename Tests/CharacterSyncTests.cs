using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Sanakan.Api.Controllers;
using Sanakan.Api.Models;
using Sanakan.Config.Model;
using Sanakan.Database;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services.PocketWaifu;
using Xunit;

namespace Artifacts
{
    public class CharacterSyncTests : IDisposable
    {
        private const string OldImage = "http://cdn.shinden.eu/cdn1/images/genuine/1.jpg";
        private const string NewImage = "http://cdn.shinden.eu/cdn1/images/genuine/555.jpg";
        // high ids, the saved pictures of other tests' cards live in the same folder
        private const ulong UpToDateCard = 91_001, StaleCard = 91_002;

        private readonly string _path = Path.Combine(Path.GetTempPath(), $"sanakan-charsync-{Guid.NewGuid():N}.db");
        private readonly FakeConfig _config = new FakeConfig();
        private readonly FixedTime _time = new FixedTime();
        private readonly LocalHttpServer _shindenServer;
        private readonly WaifuController _controller;

        public CharacterSyncTests()
        {
            _config.Model.DatabaseProvider = DatabaseProvider.Sqlite;
            _config.Model.ConnectionString = $"Data Source={_path}";

            using (var db = new DatabaseContext(_config))
            {
                db.EnsureReady();
                var first = new User().Default(20, _time.Now());
                var second = new User().Default(30, _time.Now());
                first.GameDeck.Cards.Add(NewCard(1, 100, "Seria A"));
                second.GameDeck.Cards.Add(NewCard(2, 100, "Seria A"));
                second.GameDeck.Cards.Add(NewCard(3, 200, "Seria B"));
                first.GameDeck.Cards.Add(NewCard(4, 300, "Seria C"));
                first.GameDeck.Cards.Add(NewCard(5, 101, "Seria D"));
                var current = NewCard(UpToDateCard, 100, "Seria A");
                current.Name = "Nowa Postać";
                current.Image = NewImage;
                second.GameDeck.Cards.Add(current);
                db.Users.AddRange(first, second);
                db.SaveChanges();
            }

            // character 100 has picture 555, character 101 has no picture, 500 and 429 fail on Shinden's side, anything else does not exist
            _shindenServer = new LocalHttpServer(async ctx =>
            {
                var path = ctx.Request.Url.AbsolutePath;
                if (path.EndsWith("/character/500") || path.EndsWith("/character/429"))
                {
                    ctx.Response.StatusCode = int.Parse(path[^3..]);
                    ctx.Response.Close();
                    return;
                }
                var body = path.EndsWith("/character/100")
                    ? @"{ ""character_id"": ""100"", ""first_name"": ""Nowa"", ""last_name"": ""Postać"", ""picture_artifact_id"": ""555"" }"
                    : path.EndsWith("/character/101")
                        ? @"{ ""character_id"": ""101"", ""first_name"": ""Bez"", ""last_name"": ""Obrazka"", ""picture_artifact_id"": null }"
                        : null;
                ctx.Response.StatusCode = body == null ? 404 : 200;
                if (body != null)
                    await ctx.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(body));
                ctx.Response.Close();
            });

            _controller = Controller(_shindenServer.BaseUrl);
        }

        private WaifuController Controller(string shindenUrl)
        {
            var shinden = Sanakan.ShindenCompat.ShindenClient.Create(new ConfigShinden { Token = "token", UserAgent = "tests",
                Marmolade = "marmolade", BaseUri = shindenUrl }, null, null, TimeSpan.FromSeconds(5));
            var waifu = new Waifu(null, null, null, new ListLogger(), null, null, null, _time, null, null, _config);
            return ControllerHarness.Attach(new WaifuController(shinden, waifu, new FakeExecutor { RunImmediately = true }, null, _config, _time,
                new Sanakan.Services.Shinden(null, null, null, new ListLogger(), _time), null, null, null));
        }

        public void Dispose()
        {
            _shindenServer.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(_path); } catch (IOException) { }
        }

        private static Card NewCard(ulong id, ulong character, string title)
            => new Card { Id = id, Character = character, Name = $"stara {character}", Title = title, Image = OldImage, Rarity = Rarity.A,
                Tags = new List<Tag>(), CreationDate = new DateTime(2026, 1, 1) };

        private Dictionary<ulong, Card> Cards()
        {
            using var db = new DatabaseContext(_config);
            return db.Cards.AsNoTracking().ToList().ToDictionary(x => x.Id);
        }

        private static string Describe(Card card) => $"{card.Character}|{card.Name}|{card.Title}|{card.Image}";

        [Fact]
        public async Task Sync_RefreshesImageAndName_KeepsTitle_OnlyForThatCharacter()
        {
            var before = Cards();

            var result = await _controller.SyncCharacterCardsAsync(100);

            Assert.Equal(200, result.StatusOf());
            var after = Cards();
            Assert.Equal($"100|Nowa Postać|Seria A|{NewImage}", Describe(after[1]));
            Assert.Equal($"100|Nowa Postać|Seria A|{NewImage}", Describe(after[2]));
            foreach (var id in new ulong[] { 3, 4, 5 })
                Assert.Equal(Describe(before[id]), Describe(after[id]));
        }

        [Fact]
        public async Task SyncFrom_MovesCardsOfTheDeletedCharacter_AndRefreshesThem()
        {
            var result = await _controller.SyncCharacterCardsAsync(100, from: 200);

            Assert.Equal(200, result.StatusOf());
            var after = Cards();
            Assert.Equal($"100|Nowa Postać|Seria B|{NewImage}", Describe(after[3]));
            Assert.Equal($"100|Nowa Postać|Seria A|{NewImage}", Describe(after[1]));
            Assert.Equal($"300|stara 300|Seria C|{OldImage}", Describe(after[4]));
        }

        [Fact]
        public async Task CharacterWithoutPicture_ClearsTheImage()
        {
            await _controller.SyncCharacterCardsAsync(101);

            Assert.Equal("101|Bez Obrazka|Seria D|", Describe(Cards()[5]));
        }

        [Fact]
        public async Task UnknownCharacter_Returns404_AndChangesNothing()
        {
            var before = Cards();

            var result = await _controller.SyncCharacterCardsAsync(999, from: 100);

            Assert.Equal(404, result.StatusOf());
            Assert.Equal(before.Values.Select(Describe), Cards().Values.Select(Describe));
        }

        [Theory]
        [InlineData(500ul)]
        [InlineData(429ul)]
        public async Task ShindenFailure_Returns503WithRetryAfter_AndChangesNothing(ulong id)
        {
            var before = Cards();

            var result = await _controller.SyncCharacterCardsAsync(id, from: 100);

            Assert.Equal(503, result.StatusOf());
            Assert.Equal("60", _controller.Response.Headers["Retry-After"].ToString());
            Assert.Equal(before.Values.Select(Describe), Cards().Values.Select(Describe));
        }

        // the site sends {} when it gives no data by hand
        [Fact]
        public async Task ShindenFailure_WithAnEmptyObject_StillReturns503()
        {
            var result = await _controller.SyncCharacterCardsAsync(500, 0, new CharacterCardInfoUpdate());

            Assert.Equal(503, result.StatusOf());
        }

        [Fact]
        public async Task UnreachableShinden_Returns503()
        {
            var controller = Controller("http://127.0.0.1:9/");

            Assert.Equal(503, (await controller.SyncCharacterCardsAsync(100)).StatusOf());
            Assert.Equal($"100|stara 100|Seria A|{OldImage}", Describe(Cards()[1]));
        }

        [Fact]
        public async Task OnlyChangedCards_LoseTheirSavedPictures()
        {
            using (var db = new DatabaseContext(_config))
            {
                var stale = NewCard(StaleCard, 100, "Seria A");
                stale.GameDeckId = 20;
                db.Cards.Add(stale);
                db.SaveChanges();
            }
            Sanakan.Services.Dir.Create();
            var saved = new[] { UpToDateCard, StaleCard }.ToDictionary(x => x, x => $"{Sanakan.Services.Dir.Cards}/{x}.png");
            foreach (var file in saved.Values)
                File.WriteAllText(file, "png");
            try
            {
                Assert.Equal(200, (await _controller.SyncCharacterCardsAsync(100)).StatusOf());

                Assert.True(File.Exists(saved[UpToDateCard]));
                Assert.False(File.Exists(saved[StaleCard]));
                Assert.Equal($"100|Nowa Postać|Seria A|{NewImage}", Describe(Cards()[StaleCard]));
            }
            finally
            {
                foreach (var file in saved.Values)
                    File.Delete(file);
            }
        }

        [Fact]
        public async Task MovingToTheSameCharacter_Returns400()
        {
            Assert.Equal(400, (await _controller.SyncCharacterCardsAsync(100, from: 100)).StatusOf());
        }

        [Fact]
        public async Task DataGivenByHand_WinsOverShinden_AndCanChangeTheTitle()
        {
            await _controller.SyncCharacterCardsAsync(100, 0, new CharacterCardInfoUpdate { ImageUrl = "https://i.imgur.com/x.png", CardSeriesTitle = "Nowa seria" });

            Assert.Equal("100|Nowa Postać|Nowa seria|https://i.imgur.com/x.png", Describe(Cards()[1]));
        }
    }
}
