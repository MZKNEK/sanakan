using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
    public class WishlistQueryTests : IDisposable
    {
        private const ulong Owner = 10, Active = 20, Inactive = 30, Giveaway = 1, Title = 77;

        private readonly string _path = Path.Combine(Path.GetTempPath(), $"sanakan-wishlist-{Guid.NewGuid():N}.db");
        private readonly FixedTime _time = new FixedTime();
        private readonly LocalHttpServer _shindenServer;

        private sealed class TestDatabase : DatabaseContext
        {
            private readonly string _path;
            public TestDatabase(string path) : base(new FakeConfig()) => _path = path;
            protected override void OnConfiguring(DbContextOptionsBuilder builder) => builder.UseSqlite($"Data Source={_path}");
        }

        public WishlistQueryTests()
        {
            // the title on the wishlist has character 700 (nobody of the owner's) and 800 (the owner has a card of it)
            _shindenServer = new LocalHttpServer(async ctx =>
            {
                var body = ctx.Request.Url.AbsolutePath.EndsWith($"/title/{Title}/characters")
                    ? @"{ ""relations"": [ { ""many_id"": ""1"", ""character_id"": ""700"" }, { ""many_id"": ""2"", ""character_id"": ""800"" } ] }"
                    : null;
                ctx.Response.StatusCode = body == null ? 404 : 200;
                if (body != null)
                {
                    var bytes = Encoding.UTF8.GetBytes(body);
                    await ctx.Response.OutputStream.WriteAsync(bytes);
                }
                ctx.Response.Close();
            });
        }

        public void Dispose()
        {
            _shindenServer.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(_path); } catch (IOException) { }
        }

        private User Player(ulong id, bool active)
        {
            var now = _time.Now();
            var user = new User().Default(id, now);
            user.MeasureDate = active ? now : now.AddMonths(-3);
            user.MessagesCnt = active ? 10ul : 0ul;
            user.MessagesCntAtDate = 0;
            user.GameDeck.LastSignificantActivity = active ? now : now.AddDays(-60);
            return user;
        }

        private static Card NewCard(ulong id, ulong character, bool tradable = true)
            => new Card { Id = id, Character = character, Name = $"postać {character}", IsTradable = tradable, Rarity = Rarity.A, Tags = new List<Tag>() };

        private async Task<(Waifu Waifu, ulong FavId)> SeedAsync()
        {
            using var db = new TestDatabase(_path);
            db.Database.EnsureCreated();
            var tags = await TagHelper.CreateAsync(db);
            var favId = tags.GetTagId(TagType.Favorite);
            var fav = await db.Tags.FirstAsync(x => x.Id == favId);

            var owner = Player(Owner, true);
            owner.GameDeck.Cards.Add(NewCard(1001, 500));
            owner.GameDeck.Cards.Add(NewCard(1002, 800));
            owner.GameDeck.Wishes.Add(new WishlistObject { Type = WishlistObjectType.Card, ObjectId = 3001, ObjectName = "karta" });
            owner.GameDeck.Wishes.Add(new WishlistObject { Type = WishlistObjectType.Character, ObjectId = 500, ObjectName = "postać 500" });
            owner.GameDeck.Wishes.Add(new WishlistObject { Type = WishlistObjectType.Character, ObjectId = 600, ObjectName = "postać 600" });
            owner.GameDeck.Wishes.Add(new WishlistObject { Type = WishlistObjectType.Title, ObjectId = Title, ObjectName = "seria" });

            var active = Player(Active, true);
            active.GameDeck.Cards.Add(NewCard(2001, 500));
            var favourite = NewCard(2002, 500);
            favourite.Tags.Add(fav);
            active.GameDeck.Cards.Add(favourite);
            active.GameDeck.Cards.Add(NewCard(2003, 600, tradable: false));
            active.GameDeck.Cards.Add(NewCard(4001, 999));

            var inactive = Player(Inactive, false);
            inactive.GameDeck.Cards.Add(NewCard(3001, 600));
            inactive.GameDeck.Cards.Add(NewCard(3002, 700));
            inactive.GameDeck.Cards.Add(NewCard(3003, 800));

            // the bot's own deck (cards to give away) is created by TagHelper together with the base tags
            var giveawayCard = NewCard(1501, 500);
            giveawayCard.GameDeckId = Giveaway;
            db.Cards.Add(giveawayCard);

            db.Users.AddRange(owner, active, inactive);
            await db.SaveChangesAsync();

            var shinden = Sanakan.ShindenCompat.ShindenClient.Create(new ConfigShinden { Token = "token", UserAgent = "tests",
                Marmolade = "marmolade", BaseUri = _shindenServer.BaseUrl }, null, null, TimeSpan.FromSeconds(5));
            var shindenCache = new Sanakan.Services.Shinden(shinden, null, null, new ListLogger(), _time);
            return (new Waifu(null, shinden, null, new ListLogger(), null, null, null, _time, shindenCache, tags, new FakeConfig()), favId);
        }

        private User LoadOwner(DatabaseContext db)
            => db.Users.AsQueryable().Include(x => x.GameDeck).ThenInclude(x => x.Cards).Include(x => x.GameDeck).ThenInclude(x => x.Wishes)
                .AsNoTracking().First(x => x.Id == Owner);

        // the wishlist commands before the change: everything loaded, then filtered in memory
        private async Task<List<Card>> OldAsync(Waifu waifu, DatabaseContext db, User owner, ulong favId, ulong onlyDeck, bool hideFavs, bool hideBlocked, bool hideInactive)
        {
            var cards = await waifu.GetCardsFromWishlistAsync(owner.GameDeck.GetCardsWishList(), owner.GameDeck.GetCharactersWishList(),
                owner.GameDeck.GetTitlesWishList(), db, owner.GameDeck.Cards);
            cards = onlyDeck != 0 ? cards.Where(x => x.GameDeckId == onlyDeck) : cards.Where(x => x.GameDeckId != owner.Id);
            if (hideFavs) cards = cards.Where(x => !x.Tags.Any(t => t.Id == favId));
            if (hideBlocked) cards = cards.Where(x => x.IsTradable);
            if (hideInactive) cards = cards.Where(x => x.GameDeck.IsUserActive(_time.Now()));
            return cards.ToList();
        }

        public static IEnumerable<object[]> Options()
            => from deck in new ulong[] { 0, Active, Giveaway }
               from favs in new[] { false, true }
               from blocked in new[] { false, true }
               from inactive in new[] { false, true }
               select new object[] { deck, favs, blocked, inactive };

        [Theory]
        [MemberData(nameof(Options))]
        public async Task NewQuery_ShowsTheSameCardsInTheSameOrderAsBefore(ulong onlyDeck, bool hideFavs, bool hideBlocked, bool hideInactive)
        {
            var (waifu, favId) = await SeedAsync();
            using var db = new TestDatabase(_path);
            var owner = LoadOwner(db);

            var expected = (await OldAsync(waifu, db, owner, favId, onlyDeck, hideFavs, hideBlocked, hideInactive))
                .GroupBy(x => x.Id).Select(x => x.First()).ToList();
            var actual = await waifu.GetWishlistCardsAsync(db, owner, owner.GameDeck.GetCardsWishList(), owner.GameDeck.GetCharactersWishList(),
                owner.GameDeck.GetTitlesWishList(), onlyDeck, hideFavs, hideBlocked, hideInactive);

            Assert.Equal(expected.Select(x => x.Id), actual.Select(x => x.Id));
            Assert.Equal(expected.Select(x => x.GameDeck.GetActiveMark(_time.Now())), actual.Select(x => x.GameDeck.GetActiveMark(_time.Now())));
            Assert.Equal(expected.Select(x => x.Tags.Count), actual.Select(x => x.Tags.Count));
        }

        [Fact]
        public async Task DefaultView_ListsEachCardOnce_AndSharesOneDeckPerOwner()
        {
            var (waifu, favId) = await SeedAsync();
            using var db = new TestDatabase(_path);
            var owner = LoadOwner(db);

            // życzenia without options: favourites hidden, untradable and inactive shown
            var old = await OldAsync(waifu, db, owner, favId, 0, true, false, false);
            var actual = await waifu.GetWishlistCardsAsync(db, owner, owner.GameDeck.GetCardsWishList(), owner.GameDeck.GetCharactersWishList(),
                owner.GameDeck.GetTitlesWishList(), 0, true, false, false);

            // 3001 is wished as a card and through its character; the old way listed it twice
            Assert.Equal(new ulong[] { 3001, 1501, 2001, 2003, 3001, 3002 }, old.Select(x => x.Id));
            Assert.Equal(new ulong[] { 3001, 1501, 2001, 2003, 3002 }, actual.Select(x => x.Id));
            Assert.Same(actual.Single(x => x.Id == 3001).GameDeck, actual.Single(x => x.Id == 3002).GameDeck);
            Assert.Equal(" ⚠️", actual.Single(x => x.Id == 3002).GameDeck.GetActiveMark(_time.Now()));
            Assert.Equal("", actual.Single(x => x.Id == 2001).GameDeck.GetActiveMark(_time.Now()));
        }
    }
}
