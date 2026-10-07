using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Sanakan.Services;
using Xunit;

namespace Artifacts
{
    public class ImageFormatAllowlistTests
    {
        private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);

        [Fact]
        public void Png_IsAllowed()
            => Assert.True(ImageProcessing.IsAllowedImageSignature(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }));

        [Fact]
        public void Jpeg_IsAllowed()
            => Assert.True(ImageProcessing.IsAllowedImageSignature(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }));

        [Theory]
        [InlineData("GIF87a")]
        [InlineData("GIF89a")]
        public void Gif_IsAllowed(string signature)
            => Assert.True(ImageProcessing.IsAllowedImageSignature(Ascii(signature)));

        [Fact]
        public void Webp_IsAllowed()
            => Assert.True(ImageProcessing.IsAllowedImageSignature(Ascii("RIFF\u0000\u0000\u0000\u0000WEBP")));

        [Theory]
        [InlineData(new byte[] { 0x49, 0x49, 0x2A, 0x00 })]                 // TIFF little-endian
        [InlineData(new byte[] { 0x4D, 0x4D, 0x00, 0x2A })]                 // TIFF big-endian
        [InlineData(new byte[] { 0x76, 0x2F, 0x31, 0x01 })]                 // OpenEXR
        public void TiffAndExr_AreRejected(byte[] signature)
            => Assert.False(ImageProcessing.IsAllowedImageSignature(signature));

        [Fact]
        public void Bmp_IsRejected()
            => Assert.False(ImageProcessing.IsAllowedImageSignature(Ascii("BM")));

        [Fact]
        public void RiffButNotWebp_IsRejected()
            => Assert.False(ImageProcessing.IsAllowedImageSignature(Ascii("RIFF\u0000\u0000\u0000\u0000WAVE")));

        [Fact]
        public void TruncatedPng_IsRejected()
            => Assert.False(ImageProcessing.IsAllowedImageSignature(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A }));

        [Fact]
        public void Empty_IsRejected()
            => Assert.False(ImageProcessing.IsAllowedImageSignature(ReadOnlySpan<byte>.Empty));

        [Fact]
        public void RandomBytes_AreRejected()
            => Assert.False(ImageProcessing.IsAllowedImageSignature(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C }));

        // Sprawdza też, że odczyt strumienia nie gubi pozycji i że TIFF jest odrzucany.
        [Theory]
        [InlineData(new byte[] { 0x49, 0x49, 0x2A, 0x00 }, false)]
        [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, true)]
        public async Task StreamSignatureCheck_DetectsFormat(byte[] content, bool expected)
        {
            var method = typeof(ImageProcessing).GetMethod("HasAllowedImageSignatureAsync", BindingFlags.NonPublic | BindingFlags.Static);

            using var stream = new MemoryStream(content);
            var task = (Task<bool>)method.Invoke(null, new object[] { stream });

            Assert.Equal(expected, await task);
            Assert.Equal(0, stream.Position);
        }
    }
}
