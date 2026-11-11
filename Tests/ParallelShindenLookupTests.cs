using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Sanakan.Config.Model;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services;
using Sanakan.Services.PocketWaifu;
using Sanakan.ShindenCompat;
using Xunit;

namespace Artifacts
{
    // lists of characters and titles are looked up on Shinden a few at a time instead of one by one, in the same order
    public class ParallelShindenLookupTests : IDisposable
    {
        private int _running, _maxRunning;
        private readonly LocalHttpServer _shindenServer;

        public ParallelShindenLookupTests()
        {
            _shindenServer = new LocalHttpServer(async ctx =>
            {
                var now = Interlocked.Increment(ref _running);
                lock (this) _maxRunning = Math.Max(_maxRunning, now);
                await Task.Delay(80);
                Interlocked.Decrement(ref _running);

                var id = ctx.Request.Url.AbsolutePath.Split('/')[^1];
                var body = $@"{{ ""character_id"": ""{id}"", ""first_name"": ""Postać"", ""last_name"": ""{id}"", ""picture_artifact_id"": null }}";
                await ctx.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(body));
                ctx.Response.Close();
            });
        }

        public void Dispose() => _shindenServer.Dispose();

        [Fact]
        public async Task BoundedParallel_KeepsTheOrder_AndTheLimit()
        {
            var running = 0;
            var max = 0;
            var results = await BoundedParallel.SelectAsync(Enumerable.Range(1, 20), 4, async x =>
            {
                var now = Interlocked.Increment(ref running);
                lock (this) max = Math.Max(max, now);
                await Task.Delay(20 - x);
                Interlocked.Decrement(ref running);
                return x * 10;
            });

            Assert.Equal(Enumerable.Range(1, 20).Select(x => x * 10), results);
            Assert.InRange(max, 2, 4);
        }

        [Fact]
        public async Task WishlistContent_LooksUpTheCharactersTogether_InTheirOrder()
        {
            var time = new FixedTime();
            var client = ShindenClient.Create(new ConfigShinden { Token = "token", UserAgent = "tests", Marmolade = "marmolade", BaseUri = _shindenServer.BaseUrl },
                null, null, TimeSpan.FromSeconds(5));
            var shinden = new Sanakan.Services.Shinden(client, null, null, new ListLogger(), time);
            var waifu = new Waifu(null, client, null, new ListLogger(), null, null, null, time, shinden, null, new FakeConfig());
            var ids = Enumerable.Range(1, 12).Select(x => (ulong)(100 + x)).ToList();

            var pages = (await waifu.GetContentOfWishlistAsync(new List<ulong>(), ids, new List<ulong>(), tldr: true)).ToList();

            var lines = string.Join("\n", pages.Select(x => x.Description)).Split('\n');
            Assert.Equal(ids.Select(x => $"P[{x}] Postać {x}"), lines.Select(x => string.Join(" ", x.Split(' ').Take(3))));
            Assert.InRange(_maxRunning, 2, BoundedParallel.ShindenLookups);
        }
    }

    // żdodaj looks the entries up together but still reports the first missing one and saves nothing then
    public class WishlistAddLookupTests : IDisposable
    {
        private const ulong Player = 10;
        private const ulong First = CustomCharacters.IdStart + 1, Second = CustomCharacters.IdStart + 2, Missing = CustomCharacters.IdStart + 9;

        private readonly WaifuCommandHarness _h = new WaifuCommandHarness();

        public WishlistAddLookupTests()
        {
            _h.Shinden.CustomCharacterInfo = id => Task.FromResult<Sanakan.ShindenCompat.Models.ICharacterInfo>(id == Missing ? null
                : new Sanakan.ShindenCompat.Models.Entities.CharacterInfo { Id = id, FirstName = "Postać", LastName = $"{id % 1000}" });
        }

        public void Dispose() => _h.Dispose();

        private async Task SeedAsync()
        {
            using var db = await _h.CreateAsync();
            db.Users.Add(new User().Default(Player, _h.Time.Now()));
            await db.SaveChangesAsync();
        }

        private List<WishlistObject> Wishes()
        {
            using var db = _h.Open();
            return db.Wishes.AsNoTracking().Where(x => x.GameDeckId == Player).ToList().OrderBy(x => x.ObjectId).ToList();
        }

        [Fact]
        public async Task SeveralCharacters_AreAllAddedWithTheirNames()
        {
            await SeedAsync();

            Assert.Contains("2 postaci", await _h.RunAsync(Player, x => x.AddToWishlistAsync(WishlistObjectType.Character, First, Second)));

            Assert.Equal(new[] { "Postać 1", "Postać 2" }, Wishes().Select(x => x.ObjectName));
        }

        [Fact]
        public async Task MissingCharacter_IsReported_AndNothingIsAdded()
        {
            await SeedAsync();

            var reply = await _h.RunAsync(Player, x => x.AddToWishlistAsync(WishlistObjectType.Character, First, Missing, Second));

            Assert.Contains(Missing.ToString(), reply);
            Assert.Empty(Wishes());
        }
    }
}
