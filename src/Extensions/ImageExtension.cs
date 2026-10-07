#pragma warning disable 1591

using System;
using System.IO;
using System.Linq;
using System.Threading;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Sanakan.Extensions
{
    public static class ImageExtension
    {
        private static IImageEncoder _gifEncoder = new GifEncoder();
        private static IImageEncoder _webpEncoder = new WebpEncoder()
        {
            FileFormat = WebpFileFormatType.Lossy,
            Quality = 90
        };

        public static Stream ToWebpStream(this Image img)
        {
            var stream = new MemoryStream();
            img.Save(stream, _webpEncoder);
            stream.Seek(0, SeekOrigin.Begin);
            return stream;
        }

        public static Stream ToGifStream(this Image img)
        {
            var stream = new MemoryStream();
            img.Save(stream, _gifEncoder);
            stream.Seek(0, SeekOrigin.Begin);
            return stream;
        }

        public static string SaveToPath(this Image img, string path)
        {
            var extension = path.Split(".").Last().ToLower();
            var encoder = extension switch
            {
                "gif" => _gifEncoder,
                _ => _webpEncoder
            };

            // Zapis atomowy: piszemy do unikalnego pliku tymczasowego, a następnie podmieniamy go jednym
            // File.Move. Dzięki temu czytający (np. SendFileAsync) nigdy nie trafi na plik w trakcie zapisu
            // i nie wystąpi błąd współdzielenia ("being used by another process").
            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var tmp = $"{path}.{Guid.NewGuid():N}.tmp";
            try
            {
                img.Save(tmp, encoder);

                // na Windows podmiana może chwilowo kolidować z czytającym - ponawiamy
                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        File.Move(tmp, path, true);
                        break;
                    }
                    catch (IOException) when (attempt < 25)
                    {
                        Thread.Sleep(20);
                    }
                }
            }
            finally
            {
                if (File.Exists(tmp))
                {
                    try { File.Delete(tmp); } catch { }
                }
            }

            return path;
        }

        public static string SaveToPath(this Image img, string path, int width, int height = 0)
        {
            img.Mutate(x => x.Resize(new Size(width, height)));
            return SaveToPath(img, path);
        }

        public static Image<T> ResizeAsNew<T>(this Image<T> img, int width, int height = 0) where T : unmanaged, IPixel<T>
        {
            var nImg = img.Clone();
            nImg.Mutate(x => x.Resize(new Size(width, height)));
            return nImg;
        }

        public static void Round(this IImageProcessingContext img, float radius)
        {
            var size = img.GetCurrentSize();
            var gOptions = new DrawingOptions{ GraphicsOptions = new GraphicsOptions { Antialias = true, AlphaCompositionMode = PixelAlphaCompositionMode.DestOut } };
            img.Fill(gOptions, Color.Black, BuildCorners(size.Width, size.Height, radius));
        }

        private static IPathCollection BuildCorners(int imageWidth, int imageHeight, float cornerRadius)
        {
            var rect = new RectangularPolygon(-0.5f, -0.5f, cornerRadius, cornerRadius);

            IPath cornerToptLeft = rect.Clip(new EllipsePolygon(cornerRadius - 0.5f, cornerRadius - 0.5f, cornerRadius));

            float rightPos = imageWidth - cornerToptLeft.Bounds.Width + 1;
            float bottomPos = imageHeight - cornerToptLeft.Bounds.Height + 1;

            IPath cornerTopRight = cornerToptLeft.RotateDegree(90).Translate(rightPos, 0);
            IPath cornerBottomLeft = cornerToptLeft.RotateDegree(-90).Translate(0, bottomPos);
            IPath cornerBottomRight = cornerToptLeft.RotateDegree(180).Translate(rightPos, bottomPos);

            return new PathCollection(cornerToptLeft, cornerBottomLeft, cornerTopRight, cornerBottomRight);
        }
    }
}
