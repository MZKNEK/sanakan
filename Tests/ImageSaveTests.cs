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
