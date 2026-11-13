using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sanakan.Database;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services;
using Xunit;

namespace Artifacts
{
    public class OrphanFilesTests : IDisposable
    {
        private const ulong Player = 61001;
        private const ulong GonePlayer = 61002;

        private readonly SqliteConnection _connection = new SqliteConnection("Data Source=:memory:");
        private readonly string _root = Path.Combine(Path.GetTempPath(), "sanakan-orphans-" + Guid.NewGuid().ToString("N"));
        private readonly DateTime _limit = DateTime.UtcNow.AddDays(-7);
        private ulong _card;

        private sealed class TestDatabase : DatabaseContext
        {
            private readonly SqliteConnection _connection;
            public TestDatabase(SqliteConnection connection) : base(new FakeConfig()) => _connection = connection;
            protected override void OnConfiguring(DbContextOptionsBuilder builder) => builder.UseSqlite(_connection);
        }

        public OrphanFilesTests()
        {
            _connection.Open();
            SqliteText.RegisterPow(_connection);
            using var db = Open();
            db.Database.EnsureCreated();
            var user = new User().Default(Player, DateTime.Now);
            db.Users.Add(user);
            db.SaveChanges();
            var card = new Card { Character = 1, Name = "a", Title = "t", Rarity = Rarity.A, Dere = Dere.Deredere, GameDeckId = Player, Tags = new List<Tag>() };
            db.Cards.Add(card);
            db.SaveChanges();
            _card = card.Id;
        }

        public void Dispose()
        {
            _connection.Dispose();
            if (Directory.Exists(_root))
                Directory.Delete(_root, true);
        }

        private TestDatabase Open() => new TestDatabase(_connection);

        private string File(string folder, string name, int bytes = 10, bool old = true)
        {
            var dir = Path.Combine(_root, folder);
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, name);
            System.IO.File.WriteAllBytes(path, new byte[bytes]);
            System.IO.File.SetLastWriteTimeUtc(path, old ? _limit.AddDays(-30) : DateTime.UtcNow);
            return path;
        }

        private OrphanFiles.Location Cards() => OrphanFiles.Default().First(x => x.Name == "karty") with { Folder = Path.Combine(_root, "Cards") };
        private OrphanFiles.Location Profiles() => OrphanFiles.Default().First(x => x.Name == "obrazki profili graczy") with { Folder = Path.Combine(_root, "Saved") };

        [Fact]
        public async Task CountsOnlyOldFilesWhoseCardOrUserIsGone()
        {
            File("Cards", $"{_card}.webp", 100);
            File("Cards", "999999.webp", 200);
            File("Cards", "999998.gif", 300);
            File("Cards", "999997.webp", 400, old: false);
            File("Cards", "notes.txt");
            File("Saved", $"BG{Player}.webp", 50);
            File("Saved", $"UOV{GonePlayer}.png", 60);

            var results = await OrphanFiles.ScanAsync(Open, _limit, new[] { Cards(), Profiles() });

            var cards = results[0];
            Assert.Equal(5, cards.Files);
            Assert.Equal(2, cards.Orphans);
            Assert.Equal(500, cards.OrphanBytes);
            Assert.Equal(1, cards.Recent);
            Assert.Equal(1, cards.Unknown);

            var profiles = results[1];
            Assert.Equal(1, profiles.Orphans);
            Assert.Equal(60, profiles.OrphanBytes);
        }

        [Fact]
        public async Task NothingIsRemoved()
        {
            var orphan = File("Cards", "999999.webp");

            await OrphanFiles.ScanAsync(Open, _limit, new[] { Cards() });

            Assert.True(System.IO.File.Exists(orphan));
        }

        [Fact]
        public async Task AMissingFolderIsReported()
        {
            var results = await OrphanFiles.ScanAsync(Open, _limit, new[] { Cards() });

            Assert.True(results.Single().Missing);
            Assert.Contains("brak katalogu", OrphanFiles.Describe(results, 7));
        }

        [Fact]
        public async Task ProgressNamesEachFolder()
        {
            File("Cards", "1.webp");
            var seen = new List<(string, long)>();

            await OrphanFiles.ScanAsync(Open, _limit, new[] { Cards(), Profiles() }, (name, files) => seen.Add((name, files)));

            Assert.Equal(new[] { ("karty", 0L), ("obrazki profili graczy", 0L) }, seen);
        }

        [Theory]
        [InlineData("własne obrazki i ramki kart", "CI123.webp", true)]
        [InlineData("własne obrazki i ramki kart", "CB123.webp", true)]
        [InlineData("własne obrazki i ramki kart", "XX123.webp", false)]
        [InlineData("obrazki profili graczy", "BG123.webp", true)]
        [InlineData("obrazki profili graczy", "SR123.png", true)]
        [InlineData("obrazki profili graczy", "OV123.webp", true)]
        [InlineData("obrazki profili graczy", "UOV123.webp", true)]
        [InlineData("karty", "123.webp", true)]
        [InlineData("karty", "123.WEBP", true)]
        [InlineData("karty", "123.webp.abc.tmp", false)]
        [InlineData("karty", "small", false)]
        [InlineData("profile (Prof)", "123.webp", true)]
        [InlineData("statystyki strony", "123.webp", true)]
        public void NamesTheBotMakesAreRecognised(string location, string name, bool known)
        {
            var match = OrphanFiles.Default().First(x => x.Name == location).Pattern.Match(name);
            Assert.Equal(known, match.Success);
            if (known)
                Assert.Equal("123", match.Groups["id"].Value);
        }

        [Fact]
        public void DefaultFoldersAreTheBotsOwn()
        {
            var folders = OrphanFiles.Default().Select(x => x.Folder).ToList();
            Assert.Contains(Dir.Cards, folders);
            Assert.Contains(Dir.LocalCardData, folders);
            Assert.Contains(Dir.SavedData, folders);
            Assert.DoesNotContain(Dir.TemporaryCards, folders);
            Assert.DoesNotContain(Dir.ShindenCache, folders);
        }
    }
}
