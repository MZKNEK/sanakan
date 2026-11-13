using System;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Sanakan.Extensions;
using Xunit;

namespace Artifacts
{
    public class ImageSaveTests
    {
        private static string NewDir()
        {
            var dir = Path.Combine(Path.GetTempPath(), $"sanakan-save-{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            return dir;
        }

        [Fact]
        public void SaveToPath_ReplacesFileAndLeavesNoTemp()
        {
            var dir = NewDir();
            var path = Path.Combine(dir, "card.webp");
            try
            {
                using (var img = new Image<Rgba32>(10, 10))
                    img.SaveToPath(path);

                Assert.True(File.Exists(path));
                Assert.Empty(Directory.GetFiles(dir, "*.tmp"));

                using (var img = new Image<Rgba32>(25, 25))
                    img.SaveToPath(path);

                Assert.Empty(Directory.GetFiles(dir, "*.tmp"));

                using var read = Image.Load(path);
                Assert.Equal(25, read.Width);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        private static Image<Rgba32> Noise(int frames)
        {
            var rnd = new Random(3);
            var img = new Image<Rgba32>(64, 64);
            for (int f = 1; f < frames; f++)
                img.Frames.AddFrame(img.Frames.RootFrame);
            foreach (var frame in img.Frames)
                for (int y = 0; y < 64; y++)
                    for (int x = 0; x < 64; x++)
                        frame[x, y] = new Rgba32((byte)rnd.Next(256), (byte)rnd.Next(256), (byte)rnd.Next(256), 255);
            return img;
        }

        private static byte[] Encode(Image img, int quality)
        {
            using var ms = new MemoryStream();
            img.Save(ms, new SixLabors.ImageSharp.Formats.Webp.WebpEncoder
            {
                FileFormat = SixLabors.ImageSharp.Formats.Webp.WebpFileFormatType.Lossy,
                Quality = quality,
                Method = SixLabors.ImageSharp.Formats.Webp.WebpEncodingMethod.Level2,
            });
            return ms.ToArray();
        }

        [Theory]
        [InlineData(1, 88)]
        [InlineData(3, 85)]
        public void StillImagesAreSavedAtQuality88AndAnimationsAt85(int frames, int quality)
        {
            var dir = NewDir();
            var path = Path.Combine(dir, "card.webp");
            try
            {
                using var img = Noise(frames);
                img.SaveToPath(path);
                using var stream = (MemoryStream)img.ToWebpStream();

                var expected = Encode(img, quality);
                Assert.Equal(expected, File.ReadAllBytes(path));
                Assert.Equal(expected, stream.ToArray());
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void SaveToPath_CreatesMissingDirectory()
        {
            var dir = NewDir();
            var path = Path.Combine(dir, "nested", "deep", "card.webp");
            try
            {
                using (var img = new Image<Rgba32>(5, 5))
                    img.SaveToPath(path);

                Assert.True(File.Exists(path));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        // Każda podmiana musi dawać kompletny plik (bez częściowo zapisanych danych i bez plików tymczasowych).
        [Fact]
        public void SaveToPath_ManyReplaces_AlwaysCompleteFile()
        {
            var dir = NewDir();
            var path = Path.Combine(dir, "card.webp");
            try
            {
                for (int i = 1; i <= 20; i++)
                {
                    using (var img = new Image<Rgba32>(i, i))
                        img.SaveToPath(path);

                    using var read = Image.Load(path);
                    Assert.Equal(i, read.Width);
                    Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
                }
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
