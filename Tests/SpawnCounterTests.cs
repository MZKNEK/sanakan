using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Sanakan.Services.PocketWaifu;
using Xunit;

namespace Artifacts
{
    public class SpawnUserCounterTests
    {
        [Fact]
        public void FirstMessage_IsCounted_NotDropped()
        {
            var counter = new ConcurrentDictionary<ulong, long>();

            Assert.False(Spawn.ShouldSpawnUserPacket(counter, 1, 30, 100));
            Assert.Equal(30, counter[1]);
        }

        [Fact]
        public void UnderThreshold_Accumulates()
        {
            var counter = new ConcurrentDictionary<ulong, long>();

            Assert.False(Spawn.ShouldSpawnUserPacket(counter, 1, 40, 100));
            Assert.False(Spawn.ShouldSpawnUserPacket(counter, 1, 40, 100));
            Assert.Equal(80, counter[1]);
        }

        [Fact]
        public void OverThreshold_Spawns_AndCarriesRemainder()
        {
            var counter = new ConcurrentDictionary<ulong, long>();

            Assert.True(Spawn.ShouldSpawnUserPacket(counter, 1, 120, 100));
            Assert.Equal(20, counter[1]);
        }

        [Fact]
        public void ExactlyAtThreshold_DoesNotSpawn()
        {
            var counter = new ConcurrentDictionary<ulong, long>();

            Assert.False(Spawn.ShouldSpawnUserPacket(counter, 1, 100, 100));
            Assert.Equal(100, counter[1]);
        }

        [Fact]
        public void ConcurrentAdds_DoNotLoseIncrements()
        {
            var counter = new ConcurrentDictionary<ulong, long>();

            Parallel.For(0, 1000, _ => Spawn.ShouldSpawnUserPacket(counter, 1, 1, long.MaxValue));

            Assert.Equal(1000, counter[1]);
        }

        [Fact]
        public void ConcurrentAdds_ConserveTotal()
        {
            // zachowanie masy: suma dodanych = licznik + liczba spawnów * próg
            var counter = new ConcurrentDictionary<ulong, long>();
            long spawned = 0;
            const long charNeeded = 100;

            Parallel.For(0, 1000, _ =>
            {
                if (Spawn.ShouldSpawnUserPacket(counter, 1, 37, charNeeded))
                    Interlocked.Increment(ref spawned);
            });

            Assert.Equal(1000 * 37, counter[1] + spawned * charNeeded);
        }
    }
}
