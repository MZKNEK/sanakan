using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Sanakan.Config.Model;
using Sanakan.Database;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services.PocketWaifu;
using Xunit;

namespace Artifacts
{
    // użyj loads only the target card instead of the whole profile; the result and the saved state must stay the same
    public class ItemUseLoadingTests : IDisposable
    {
        private const ulong Player = 10, Waifu = 77;

        private readonly FixedTime _time = new FixedTime();
        private readonly List<string> _paths = new List<string>();
        private readonly LocalHttpServer _shindenServer = new LocalHttpServer(ctx =>
        {
            ctx.Response.StatusCode = 404;
            ctx.Response.Close();
            return Task.CompletedTask;
        });

        private sealed class TestDatabase : DatabaseContext
        {
            private readonly string _path;
            public TestDatabase(string path) : base(new FakeConfig()) => _path = path;
            protected override void OnConfiguring(DbContextOptionsBuilder builder) => builder.UseSqlite($"Data Source={_path}");
        }

        public void Dispose()
        {
            _shindenServer.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var path in _paths)
            {
                try { File.Delete(path); } catch (IOException) { }
            }
        }

        private static Card NewCard(ulong id, ulong character, Rarity rarity, double affection)
            => new Card { Id = id, Character = character, Name = $"postać {character}", Rarity = rarity, Affection = affection, Attack = 50, Defence = 20,
                Health = 100, MarketValue = 3, UpgradesCnt = 1, Quality = Quality.Broken, IsTradable = true, Tags = new List<Tag>(), CreationDate = new DateTime(2026, 1, 1) };

        private async Task<(string Path, Waifu Waifu)> SeedAsync()
        {
            var path = Path.Combine(Path.GetTempPath(), $"sanakan-itemuse-{Guid.NewGuid():N}.db");
            _paths.Add(path);
            using var db = new TestDatabase(path);
            db.Database.EnsureCreated();
            var tags = await TagHelper.CreateAsync(db, _time);

            var user = new User().Default(Player, _time.Now());
            user.GameDeck.Waifu = Waifu;
            user.GameDeck.Cards.Add(NewCard(501, Waifu, Rarity.A, 10));
            user.GameDeck.Cards.Add(NewCard(502, 88, Rarity.SSS, 2));
            user.GameDeck.Cards.Add(NewCard(503, 99, Rarity.B, 1));
            foreach (var (type, count) in new[] { (ItemType.AffectionRecoveryBig, 3), (ItemType.IncreaseExpBig, 2), (ItemType.ResetCardValue, 1),
                (ItemType.ChangeStarType, 1), (ItemType.IncreaseUpgradeCnt, 1), (ItemType.GiveTagSlot, 2) })
                user.GameDeck.Items.Add(type.ToItem(count));
            user.GameDeck.Items.Add(ItemType.FigureSkeleton.ToItem(1, Quality.Alpha));
            db.Users.Add(user);
            await db.SaveChangesAsync();

            var shinden = Sanakan.ShindenCompat.ShindenClient.Create(new ConfigShinden { Token = "token", UserAgent = "tests",
                Marmolade = "marmolade", BaseUri = _shindenServer.BaseUrl }, null, null, TimeSpan.FromSeconds(5));
            var cache = new Sanakan.Services.Shinden(shinden, null, null, new ListLogger(), _time);
            return (path, new Waifu(null, shinden, null, new ListLogger(), null, null, null, _time, cache, tags, new FakeConfig()));
        }

        private async Task<(bool Ok, string Message, string State)> UseAsync(bool narrow, ItemType type, ulong wid, string detail)
        {
            var (path, waifu) = await SeedAsync();
            using (var db = new TestDatabase(path))
            {
                var user = narrow ? await db.GetUserOrCreateWithCardsAsync(Player, _time, wid) : await db.GetUserOrCreateAsync(Player, _time);
                var number = user.GetAllItems().ToList().FindIndex(x => x.Type == type) + 1;
                var res = await waifu.UseItemAsync(user, "gracz", number, wid, detail);
                user.MarkActivity(_time.Now());
                await db.SaveChangesAsync();

                using var check = new TestDatabase(path);
                return (res.IsOk(), res.Message, Snapshot(check));
            }
        }

        private static string Snapshot(DatabaseContext db)
        {
            var deck = db.GameDecks.AsNoTracking().Include(x => x.Items).Include(x => x.Figures).Include(x => x.Cards).First(x => x.Id == Player);
            var statuses = db.TimeStatuses.AsNoTracking().Where(x => x.UserId == Player).ToList();
            return string.Join("\n", new[] { $"tags {deck.MaxNumberOfTags} karma {deck.Karma}" }
                .Concat(deck.Cards.OrderBy(x => x.Id).Select(x => $"card {x.Id} aff {x.Affection:0.###} exp {x.ExpCnt:0.###} value {x.MarketValue} star {x.StarStyle} up {x.UpgradesCnt}"))
                .Concat(deck.Items.OrderBy(x => x.Type).ThenBy(x => x.Quality).Select(x => $"item {x.Type} {x.Quality} x{x.Count}"))
                .Concat(deck.Figures.Select(x => $"figure {x.Character} {x.SkeletonQuality}"))
                .Concat(statuses.OrderBy(x => x.Type).Select(x => $"status {x.Type} {x.IValue}")));
        }

        public static IEnumerable<object[]> Uses() => new[]
        {
            new object[] { ItemType.AffectionRecoveryBig, 501ul, "2", true },
            new object[] { ItemType.AffectionRecoveryBig, 503ul, "1", true },
            new object[] { ItemType.IncreaseExpBig, 501ul, "2", true },
            new object[] { ItemType.ResetCardValue, 503ul, "1", true },
            new object[] { ItemType.ChangeStarType, 501ul, "pusta", true },
            new object[] { ItemType.IncreaseUpgradeCnt, 501ul, "1", true },
            new object[] { ItemType.IncreaseUpgradeCnt, 503ul, "1", false },
            new object[] { ItemType.FigureSkeleton, 502ul, "1", true },
            new object[] { ItemType.FigureSkeleton, 501ul, "1", false },
            new object[] { ItemType.GiveTagSlot, 0ul, "2", true },
            new object[] { ItemType.AffectionRecoveryBig, 999ul, "1", false },
        };

        [Theory]
        [MemberData(nameof(Uses))]
        public async Task NarrowLoad_GivesTheSameResultAndSavedState(ItemType type, ulong wid, string detail, bool expectedOk)
        {
            var full = await UseAsync(false, type, wid, detail);
            var narrow = await UseAsync(true, type, wid, detail);

            Assert.True(full.Ok == expectedOk, full.Message);
            Assert.Equal(full.Ok, narrow.Ok);
            Assert.Equal(full.Message, narrow.Message);
            Assert.Equal(full.State, narrow.State);
        }

        [Fact]
        public async Task NarrowLoad_HasOnlyTheTargetCard()
        {
            var (path, _) = await SeedAsync();
            using var db = new TestDatabase(path);

            var user = await db.GetUserOrCreateWithCardsAsync(Player, _time, 502);

            Assert.Equal(new ulong[] { 502 }, user.GameDeck.Cards.Select(x => x.Id));
            Assert.Equal(7, user.GameDeck.Items.Count);
            Assert.Null(user.GameDeck.PvPStats);
            Assert.Null(user.GameDeck.Wishes);
        }
    }
}
