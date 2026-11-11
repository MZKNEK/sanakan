using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Sanakan;
using Sanakan.Api.Controllers;
using Sanakan.Api.Models;
using Sanakan.Config.Model;
using Sanakan.Database;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services.Cache;
using Xunit;

namespace Artifacts
{
    // a card list page can have dozens of owners: their Shinden names are looked up a few at a time, stale ones are served at once
    [Collection(nameof(CachedCardListTests))]
    public class OwnerNameLookupTests : IDisposable
    {
        private const int Owners = 12;

        private readonly string _path = Path.Combine(Path.GetTempPath(), $"sanakan-ownernames-{Guid.NewGuid():N}.db");
        private readonly FakeConfig _config = new FakeConfig();
        private readonly FixedTime _time = new FixedTime();
        private readonly LocalHttpServer _shindenServer;
        private int _running, _maxRunning, _requests;

        public OwnerNameLookupTests()
        {
            _config.Model.DatabaseProvider = DatabaseProvider.Sqlite;
            _config.Model.ConnectionString = $"Data Source={_path}";
            QueryCache.ExpireTag(CacheTags.UltimateCards);
            using (var db = new DatabaseContext(_config))
            {
                db.EnsureReady();
                for (ulong i = 1; i <= Owners; i++)
                {
                    var user = new User().Default(100 + i, _time.Now());
                    user.Shinden = 2000 + i;
                    user.GameDeck.Cards.Add(new Card { Id = i, Character = i, Name = $"karta {i}", Rarity = Rarity.A, FromFigure = true, Tags = new List<Tag>(), CreationDate = new DateTime(2026, 1, 1) });
                    db.Users.Add(user);
                }
                db.SaveChanges();
            }

            _shindenServer = new LocalHttpServer(async ctx =>
            {
                var path = ctx.Request.Url.AbsolutePath;
                Interlocked.Increment(ref _requests);
                var now = Interlocked.Increment(ref _running);
                lock (this) _maxRunning = Math.Max(_maxRunning, now);
                await Task.Delay(100);
                Interlocked.Decrement(ref _running);

                var id = path.Split('/')[^2];
                await ctx.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes($@"{{ ""user_id"": ""{id}"", ""name"": ""gracz{id}"" }}"));
                ctx.Response.Close();
            });
        }

        public void Dispose()
        {
            _shindenServer.Dispose();
            QueryCache.ExpireTag(CacheTags.UltimateCards);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(_path); } catch (IOException) { }
        }

        private WaifuController Controller()
        {
            var client = Sanakan.ShindenCompat.ShindenClient.Create(new ConfigShinden { Token = "token", UserAgent = "tests",
                Marmolade = "marmolade", BaseUri = _shindenServer.BaseUrl }, null, null, TimeSpan.FromSeconds(5));
            var shinden = new Sanakan.Services.Shinden(client, null, null, new ListLogger(), _time);
            return ControllerHarness.Attach(new WaifuController(client, null, new FakeExecutor(), null, _config, _time, shinden, null, null, null));
        }

        [Fact]
        public async Task CardList_LooksUpTheOwnersAFewAtATime()
        {
            var page = (await Controller().GetUltimateCardsWithOffsetAndFilterAsync(0, 50, new CardsQueryFilter { OrderBy = OrderType.IdDes })).Value;

            Assert.Equal(Owners, page.Cards.Count());
            Assert.All(page.Cards, x => Assert.Equal($"gracz{2000 + x.Id}", x.Username));
            Assert.Equal(Owners, _requests);
            Assert.InRange(_maxRunning, 2, 5);
        }

        [Fact]
        public async Task StaleName_IsServedAtOnce_AndRefreshedOnceInTheBackground()
        {
            var cache = new KeyedCache<ulong, string>(TimeSpan.FromHours(24), TimeSpan.FromMinutes(5), TimeSpan.FromDays(30));
            var now = new DateTime(2026, 1, 1);
            await cache.GetAsync(1, () => Task.FromResult("stara"), now);
            var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var fetches = 0;
            Task<string> Fetch() { Interlocked.Increment(ref fetches); return release.Task; }

            var later = now.AddDays(2);
            Assert.Equal("stara", await cache.GetStaleAndRefreshAsync(1, Fetch, later));
            Assert.Equal("stara", await cache.GetStaleAndRefreshAsync(1, Fetch, later));
            release.SetResult("nowa");
            for (int i = 0; i < 100 && cache.Peek(1) != "nowa"; i++)
                await Task.Delay(10);

            Assert.Equal(1, fetches);
            Assert.Equal("nowa", await cache.GetStaleAndRefreshAsync(1, Fetch, later));
        }

        [Fact]
        public async Task UnknownName_WaitsForTheLookup()
        {
            var cache = new KeyedCache<ulong, string>(TimeSpan.FromHours(24), TimeSpan.FromMinutes(5), TimeSpan.FromDays(30));

            Assert.Equal("nowy", await cache.GetStaleAndRefreshAsync(7, () => Task.FromResult("nowy"), new DateTime(2026, 1, 1)));
        }
    }
}
