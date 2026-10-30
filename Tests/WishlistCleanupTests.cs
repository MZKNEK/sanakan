using System;
using System.Linq;
using System.Threading.Tasks;
using Sanakan.Database.Models;
using Sanakan.Services.PocketWaifu;
using Xunit;

namespace Artifacts
{
    public class WishlistCleanupTests
    {
        private static WishlistObject Wish(ulong id, WishlistObjectType type, ulong objectId, WishlistEntryType entry = WishlistEntryType.Normal)
            => new WishlistObject { Id = id, Type = type, ObjectId = objectId, Entry = entry };

        [Fact]
        public void Duplicates_KeepOneEntryPerObject()
        {
            var wishes = new[]
            {
                Wish(1, WishlistObjectType.Character, 5),
                Wish(2, WishlistObjectType.Character, 5),
                Wish(3, WishlistObjectType.Character, 5),
                Wish(4, WishlistObjectType.Title, 5),
                Wish(5, WishlistObjectType.Character, 6),
            };

            Assert.Equal(new ulong[] { 2, 3 }, WishlistCleanup.Duplicates(wishes).Select(x => x.Id).OrderBy(x => x));
        }

        [Fact]
        public void Duplicates_PreferPersistentEntry()
        {
            var wishes = new[]
            {
                Wish(1, WishlistObjectType.Character, 5),
                Wish(2, WishlistObjectType.Character, 5, WishlistEntryType.Persistent),
            };

            Assert.Equal(1UL, WishlistCleanup.Duplicates(wishes).Single().Id);
        }

        [Fact]
        public async Task OnlyConfirmedMissingEntries_AreReturned()
        {
            var wishes = new[]
            {
                Wish(1, WishlistObjectType.Character, 10),
                Wish(2, WishlistObjectType.Character, 20),
                Wish(3, WishlistObjectType.Title, 30),
                Wish(4, WishlistObjectType.Title, 40),
            };

            var (missing, unknown) = await WishlistCleanup.FindMissingAsync(wishes, wish => wish.ObjectId switch
            {
                10 => Task.FromResult(WishCheck.Exists),
                20 => Task.FromResult(WishCheck.Missing),
                30 => Task.FromResult(WishCheck.Unknown),
                _ => throw new Exception("connection lost"),
            });

            Assert.Equal(2UL, missing.Single().Id);
            Assert.Equal(2, unknown);
        }

        [Fact]
        public async Task Checks_RespectParallelLimit()
        {
            int running = 0, max = 0;
            var wishes = Enumerable.Range(1, 20).Select(x => Wish((ulong)x, WishlistObjectType.Character, (ulong)x));

            await WishlistCleanup.FindMissingAsync(wishes, async _ =>
            {
                var now = System.Threading.Interlocked.Increment(ref running);
                lock (wishes) max = Math.Max(max, now);
                await Task.Delay(10);
                System.Threading.Interlocked.Decrement(ref running);
                return WishCheck.Exists;
            }, parallel: 3);

            Assert.InRange(max, 1, 3);
        }
    }
}
