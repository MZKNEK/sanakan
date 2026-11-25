using System;
using System.Threading.Tasks;
using Sanakan.Services;
using Xunit;

namespace Artifacts
{
    [CollectionDefinition(nameof(ShutdownCleanupTests), DisableParallelization = true)]
    public class ShutdownCleanupCollection { }

    [Collection(nameof(ShutdownCleanupTests))]
    public class ShutdownCleanupTests : IDisposable
    {
        public ShutdownCleanupTests() => ShutdownCleanup.Reset();

        public void Dispose() => ShutdownCleanup.Reset();

        [Fact]
        public async Task RunsWhatIsStillRegisteredOnce()
        {
            var ran = 0;
            var takenBack = 0;
            ShutdownCleanup.Register(() => { ran++; return Task.CompletedTask; });
            using (ShutdownCleanup.Register(() => { takenBack++; return Task.CompletedTask; })) { }

            Assert.False(ShutdownCleanup.IsStopping);
            await ShutdownCleanup.RunAsync(null, TimeSpan.FromSeconds(1));
            await ShutdownCleanup.RunAsync(null, TimeSpan.FromSeconds(1));

            Assert.True(ShutdownCleanup.IsStopping);
            Assert.Equal(1, ran);
            Assert.Equal(0, takenBack);
        }

        [Fact]
        public async Task AFailingOrHangingTaskDoesNotHoldTheStop()
        {
            var ran = false;
            ShutdownCleanup.Register(() => throw new InvalidOperationException("gone"));
            ShutdownCleanup.Register(() => new TaskCompletionSource().Task);
            ShutdownCleanup.Register(() => { ran = true; return Task.CompletedTask; });

            var stop = ShutdownCleanup.RunAsync(null, TimeSpan.FromMilliseconds(200));
            Assert.Same(stop, await Task.WhenAny(stop, Task.Delay(TimeSpan.FromSeconds(5))));
            Assert.True(ran);
        }
    }
}
