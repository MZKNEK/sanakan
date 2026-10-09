#pragma warning disable 1591

using System;
using System.IO;
using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Sanakan.Services.ScamImages
{
    // DCT-based perceptual hash (pHash). Unlike a cryptographic hash it stays
    // close for the same picture after re-compression, resizing or small edits.
    public static class PerceptualHash
    {
        public const int HashBits = 64;

        private const int Size = 32;
        private const int LowFreq = 8;

        public static ulong FromFile(string path)
        {
            using var image = Image.Load<Rgba32>(path);
            return FromImage(image);
        }

        public static ulong FromStream(Stream stream)
        {
            using var image = Image.Load<Rgba32>(stream);
            return FromImage(image);
        }

        public static ulong[] FromStreamOrientations(Stream stream)
        {
            using var image = Image.Load<Rgba32>(stream);
            return FromImageOrientations(image);
        }

        public static ulong FromImage(Image<Rgba32> image) => Compute(image);

        public static ulong[] FromImageOrientations(Image<Rgba32> image)
        {
            var hashes = new ulong[4];
            hashes[0] = Compute(image);

            using (var r90 = image.Clone(x => x.Rotate(RotateMode.Rotate90)))
                hashes[1] = Compute(r90);

            using (var r180 = image.Clone(x => x.Rotate(RotateMode.Rotate180)))
                hashes[2] = Compute(r180);

            using (var r270 = image.Clone(x => x.Rotate(RotateMode.Rotate270)))
                hashes[3] = Compute(r270);

            return hashes;
        }

        public static int Distance(ulong left, ulong right) => BitOperations.PopCount(left ^ right);

        private static ulong Compute(Image<Rgba32> image)
        {
            using var small = image.Clone(x => x
                .Grayscale()
                .Resize(new ResizeOptions { Size = new Size(Size, Size), Mode = ResizeMode.Stretch }));

            var pixels = new double[Size, Size];
            small.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < Size; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < Size; x++)
                        pixels[y, x] = row[x].R;
                }
            });

            var coefficients = Dct2D(pixels);
            var values = new double[LowFreq * LowFreq];
            int index = 0;
            for (int v = 0; v < LowFreq; v++)
                for (int u = 0; u < LowFreq; u++)
                    values[index++] = coefficients[v, u];

            double median = Median(values);

            ulong hash = 0;
            for (int i = 0; i < values.Length; i++)
                if (values[i] > median)
                    hash |= 1UL << i;

            return hash;
        }

        private static double Median(double[] values)
        {
            var sorted = (double[])values.Clone();
            Array.Sort(sorted);
            return sorted[sorted.Length / 2];
        }

        private static double[,] Dct2D(double[,] input)
        {
            int n = Size;
            var rows = new double[n, n];
            var output = new double[n, n];
            var buffer = new double[n];

            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                    buffer[x] = input[y, x];

                Dct1D(buffer, rows, y, true);
            }

            for (int x = 0; x < n; x++)
            {
                for (int y = 0; y < n; y++)
                    buffer[y] = rows[y, x];

                Dct1D(buffer, output, x, false);
            }

            return output;
        }

        private static void Dct1D(double[] source, double[,] target, int index, bool writeRow)
        {
            int n = source.Length;
            for (int k = 0; k < n; k++)
            {
                double sum = 0;
                for (int i = 0; i < n; i++)
                    sum += source[i] * Math.Cos(Math.PI * (2 * i + 1) * k / (2.0 * n));

                double scale = k == 0 ? Math.Sqrt(1.0 / n) : Math.Sqrt(2.0 / n);
                double value = scale * sum;

                if (writeRow)
                    target[index, k] = value;
                else
                    target[k, index] = value;
            }
        }
    }
}
