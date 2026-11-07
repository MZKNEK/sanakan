using Sanakan.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Xunit;

namespace Artifacts
{
    public class MemorySettingsTests
    {
        [Fact]
        public void LimitedImagePool_StillProcessesImages()
        {
            ImageProcessing.LimitMemoryPool();

            using var image = new Image<Rgba32>(2000, 1500);
            image.Mutate(x => x.Resize(200, 150));

            Assert.Equal(200, image.Width);
            Assert.NotNull(Configuration.Default.MemoryAllocator);
        }
    }
}
