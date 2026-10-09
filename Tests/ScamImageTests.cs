using System;
using System.IO;
using System.Linq;
using Sanakan.Services;
using Sanakan.Services.ScamImages;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Xunit;

namespace Artifacts
{
    public class ScamImageTests
    {
        private static byte[] Png(int w, int h, Func<int, int, int, Rgba32> f)
        {
            using var img = new Image<Rgba32>(w, h);
            img.ProcessPixelRows(acc =>
            {
                for (int y = 0; y < h; y++)
                {
                    var row = acc.GetRowSpan(y);
                    for (int x = 0; x < w; x++)
                        row[x] = f(x, y, w);
                }
            });

            using var ms = new MemoryStream();
            img.SaveAsPng(ms);
            return ms.ToArray();
        }

        private static Image<Rgba32> Load(byte[] bytes)
        {
            using var ms = new MemoryStream(bytes);
            return Image.Load<Rgba32>(ms);
        }

        private static byte[] Jpeg(int w, int h, Func<int, int, int, Rgba32> f)
        {
            using var img = Load(Png(w, h, f));
            using var ms = new MemoryStream();
            img.SaveAsJpeg(ms, new JpegEncoder { Quality = 85 });
            return ms.ToArray();
        }

        private static Rgba32 HorizontalGradient(int x, int y, int w)
        {
            var v = (byte)(x * 255 / Math.Max(1, w - 1));
            return new Rgba32(v, v, v);
        }

        private static Rgba32 Textured(int x, int y, int w)
        {
            double v = 128
                + 60 * Math.Sin(x * 12.0 / w)
                + 60 * Math.Cos(y * 12.0 / w);

            if (x < w / 4 && y < w / 4) v = 30;
            if (x > 3 * w / 4 && y > 3 * w / 4) v = 225;

            var b = (byte)Math.Clamp(v, 0, 255);
            return new Rgba32(b, b, b);
        }

        private static Rgba32 RadialBlob(int x, int y, int w)
        {
            double cx = w / 2.0, cy = w / 2.0;
            double dist = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
            var v = (byte)Math.Max(0, 255 - dist * 255 / (w / 2.0));
            return new Rgba32(v, v, v);
        }

        [Fact]
        public void SameImage_SameHash()
        {
            var bytes = Png(200, 200, HorizontalGradient);
            Assert.Equal(PerceptualHash.FromStream(new MemoryStream(bytes)), PerceptualHash.FromStream(new MemoryStream(bytes)));
            Assert.Equal(0, PerceptualHash.Distance(PerceptualHash.FromStream(new MemoryStream(bytes)), PerceptualHash.FromStream(new MemoryStream(bytes))));
        }

        [Fact]
        public void RecompressedImage_StaysClose()
        {
            var png = PerceptualHash.FromStream(new MemoryStream(Png(256, 256, Textured)));
            var jpg = PerceptualHash.FromStream(new MemoryStream(Jpeg(256, 256, Textured)));
            var distance = PerceptualHash.Distance(png, jpg);
            Assert.True(distance <= 8, $"recompressed distance={distance}");
        }

        [Fact]
        public void ResizedImage_StaysClose()
        {
            var big = PerceptualHash.FromStream(new MemoryStream(Png(512, 512, Textured)));
            var small = PerceptualHash.FromStream(new MemoryStream(Png(128, 128, Textured)));
            var distance = PerceptualHash.Distance(big, small);
            Assert.True(distance <= 8, $"resized distance={distance}");
        }

        [Fact]
        public void DifferentImages_AreFar()
        {
            var textured = PerceptualHash.FromStream(new MemoryStream(Png(256, 256, Textured)));
            var blob = PerceptualHash.FromStream(new MemoryStream(Png(256, 256, RadialBlob)));
            var recompressed = PerceptualHash.FromStream(new MemoryStream(Jpeg(256, 256, Textured)));

            Assert.True(PerceptualHash.Distance(textured, blob) > PerceptualHash.Distance(textured, recompressed));
        }

        [Fact]
        public void RotatedImage_MatchesOneOfOrientations()
        {
            using var original = Load(Png(200, 120, HorizontalGradient));
            var orientations = PerceptualHash.FromImageOrientations(original);

            using var rotated = original.Clone(x => x.Rotate(RotateMode.Rotate90));
            var rotatedHash = PerceptualHash.FromImage(rotated);

            Assert.Contains(rotatedHash, orientations);
        }

        [Fact]
        public void Store_AddContainsAddsOnlyOnce()
        {
            var path = TempFile();
            try
            {
                var store = new ScamImageStore(path);
                Assert.True(store.Add(0x0123456789ABCDEF));
                Assert.False(store.Add(0x0123456789ABCDEF));
                Assert.Equal(1, store.Count);
                Assert.True(store.Contains(0x0123456789ABCDEF, 0));
            }
            finally { Cleanup(path); }
        }

        [Fact]
        public void Store_ThresholdRespectsDistance()
        {
            var path = TempFile();
            try
            {
                var store = new ScamImageStore(path);
                store.Add(0UL);

                Assert.True(store.Contains(0UL, 0));
                Assert.False(store.Contains(1UL, 0));
                Assert.True(store.Contains(1UL, 1));
                Assert.Equal(1, store.MinDistance(1UL));
                Assert.Equal(2, store.MinDistance(3UL));
            }
            finally { Cleanup(path); }
        }

        [Fact]
        public void Store_MinDistanceEmpty_IsMinusOne()
        {
            var path = TempFile();
            try
            {
                Assert.Equal(-1, new ScamImageStore(path).MinDistance(0UL));
            }
            finally { Cleanup(path); }
        }

        [Fact]
        public void Store_PersistsAcrossInstances()
        {
            var path = TempFile();
            try
            {
                new ScamImageStore(path).Add(0xDEADBEEFCAFEBABE);

                var reopened = new ScamImageStore(path);
                Assert.Equal(1, reopened.Count);
                Assert.True(reopened.Contains(0xDEADBEEFCAFEBABE, 0));
            }
            finally { Cleanup(path); }
        }

        [Fact]
        public void Store_RemoveDeletesSignature()
        {
            var path = TempFile();
            try
            {
                var store = new ScamImageStore(path);
                store.Add(42UL);
                Assert.True(store.Remove(42UL));
                Assert.False(store.Remove(42UL));
                Assert.Equal(0, store.Count);
            }
            finally { Cleanup(path); }
        }

        [Theory]
        [InlineData("0123456789abcdef", true)]
        [InlineData("0123456789ABCDEF # comment", true)]
        [InlineData("# comment only", false)]
        [InlineData("", false)]
        [InlineData("xyz", false)]
        public void Store_ParsesHashLines(string line, bool expected)
        {
            Assert.Equal(expected, ScamImageStore.TryParseHash(line, out _));
        }

        [Fact]
        public void Store_FormatsAs16Hex()
        {
            Assert.Equal("000000000000002a", ScamImageStore.FormatHash(42));
            Assert.True(ScamImageStore.TryParseHash(ScamImageStore.FormatHash(123456789), out var parsed));
            Assert.Equal(123456789UL, parsed);
        }

        [Fact]
        public void Scanner_DetectsKnownSignature()
        {
            var path = TempFile();
            try
            {
                var store = new ScamImageStore(path);
                store.Add(PerceptualHash.FromStream(new MemoryStream(Png(200, 200, HorizontalGradient))));

                var scanner = new ScamImageScanner(new ImageProcessing(null, null, true), store, null);
                Assert.True(scanner.IsScamStream(new MemoryStream(Png(200, 200, HorizontalGradient))));
                Assert.False(scanner.IsScamStream(new MemoryStream(Png(200, 200, RadialBlob))));
            }
            finally { Cleanup(path); }
        }

        [Fact]
        public void Scanner_WithoutSignatures_DoesNotMatch()
        {
            var path = TempFile();
            try
            {
                var scanner = new ScamImageScanner(new ImageProcessing(null, null, true), new ScamImageStore(path), null);
                Assert.False(scanner.IsScamStream(new MemoryStream(Png(64, 64, HorizontalGradient))));
            }
            finally { Cleanup(path); }
        }

        [Fact]
        public void Store_CreatesFileWhenMissing()
        {
            var dir = Path.Combine(Path.GetTempPath(), $"sanakan-scam-{Guid.NewGuid():N}");
            var path = Path.Combine(dir, "nested", "hashes.txt");
            try
            {
                Assert.False(File.Exists(path));

                _ = new ScamImageStore(path);

                Assert.True(File.Exists(path));
            }
            finally
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void Store_TryFind_ReturnsClosestKnown()
        {
            var path = TempFile();
            try
            {
                var store = new ScamImageStore(path);
                store.Add(0UL);
                store.Add(ulong.MaxValue);

                Assert.True(store.TryFind(1UL, 4, out var known, out var distance));
                Assert.Equal(0UL, known);
                Assert.Equal(1, distance);
            }
            finally { Cleanup(path); }
        }

        [Fact]
        public void Scanner_MatchStream_ReturnsDetails()
        {
            var path = TempFile();
            try
            {
                var hash = PerceptualHash.FromStream(new MemoryStream(Png(200, 200, Textured)));
                var store = new ScamImageStore(path);
                store.Add(hash);

                var scanner = new ScamImageScanner(new ImageProcessing(null, null, true), store, null);
                var match = scanner.MatchStream(new MemoryStream(Png(200, 200, Textured)), "http://x/a.png");

                Assert.NotNull(match);
                Assert.Equal(hash, match.KnownHash);
                Assert.Equal(0, match.Distance);
                Assert.Equal("http://x/a.png", match.Url);
            }
            finally { Cleanup(path); }
        }

        private static string TempFile()
        {
            var dir = Path.Combine(Path.GetTempPath(), $"sanakan-scam-{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "hashes.txt");
        }

        private static void Cleanup(string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }
}
