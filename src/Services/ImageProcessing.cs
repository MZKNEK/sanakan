#pragma warning disable 1591

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services.PocketWaifu;
using Sanakan.ShindenCompat;
using Sanakan.ShindenCompat.Models;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Numerics;
using SixLabors.ImageSharp.Formats.Webp;

namespace Sanakan.Services
{
    public partial class ImageProcessing
    {
        private const long MaxImageBytes = 40 * 1024 * 1024;
        private const int MaxImageDimension = 8192;
        private const long MaxImagePixels = 64_000_000;
        // the default pool scales with the machine's total memory; this caps what stays reserved between images
        public const int MemoryPoolMegabytes = 1024;

        public static void LimitMemoryPool()
            => Configuration.Default.MemoryAllocator = SixLabors.ImageSharp.Memory.MemoryAllocator.Create(
                new SixLabors.ImageSharp.Memory.MemoryAllocatorOptions { MaximumPoolSizeMegabytes = MemoryPoolMegabytes });

        private FontFamily _digital = new FontCollection().Add("Fonts/Digital.ttf");
        private FontFamily _latoBold = new FontCollection().Add("Fonts/Lato-Bold.ttf");
        private FontFamily _latoLight = new FontCollection().Add("Fonts/Lato-Light.ttf");
        private FontFamily _latoRegular = new FontCollection().Add("Fonts/Lato-Regular.ttf");

        private readonly TagIcon _galleryTag;
        private readonly HttpClient _httpClient;
        private readonly ShindenClient _shclient;
        private ConcurrentDictionary<string, Color> _colors;
        private ConcurrentDictionary<(FontFamily, float), Font> _fonts;
        private readonly List<DomainData> _siteImageServices;
        private readonly string[] _extensions = new[] { "png", "jpg", "jpeg", "gif", "webp" };
        private readonly bool _allowPrivateHosts;
        private readonly ILogger _logger;

        private const int CharacterPictureAttempts = 3;
        private const int CharacterPictureRetryDelayMs = 400;
        private const int MaxMovedPictures = 10000;

        // set at startup: the character's picture on Shinden now, and saving a changed picture into that character's cards
        public Func<ulong, Task<string>> CurrentCharacterPicture { get; set; }
        public Func<ulong, string, string, Task> CharacterPictureMoved { get; set; }

        // old picture url -> the current one, so the other cards still holding the old url skip its failing download
        private readonly ConcurrentDictionary<string, string> _movedPictures = new ConcurrentDictionary<string, string>();

        public ImageProcessing(ShindenClient shinden, TagIcon gallery, bool allowPrivateHosts = false, ILogger logger = null, HttpMessageHandler handler = null)
        {
            _logger = logger;
            _shclient = shinden;
            _galleryTag = gallery;
            _allowPrivateHosts = allowPrivateHosts;
            _httpClient = new HttpClient(handler ?? new HttpClientHandler()) { Timeout = TimeSpan.FromSeconds(30) };
            _fonts = new ConcurrentDictionary<(FontFamily, float), Font>();
            _colors = new ConcurrentDictionary<string, Color>();
            _siteImageServices = new List<DomainData>
            {
                new DomainData("sanakan.pl", true, includeSubdomains: true),
                new DomainData("shinden.pl", includeSubdomains: true),
                new DomainData("shinden.eu", includeSubdomains: true),
                new DomainData("cdn.discordapp.com"),
                new DomainData("media.discordapp.net"),
                new DomainData("i.imgur.com", true),
                new DomainData("www.dropbox.com") { Transform = TransformDropboxAsync },
                new DomainData("dl.dropboxusercontent.com"),
            };
        }

        private async Task<string> TransformDropboxAsync(string url)
        {
            using var res = await _httpClient.GetAsync(url.Replace("www.dropbox", "dl.dropbox"), HttpCompletionOption.ResponseHeadersRead);
            if (res.IsSuccessStatusCode && (res.Content.Headers.ContentType?.MediaType?.StartsWith("image") ?? false))
            {
                return res.RequestMessage.RequestUri.AbsoluteUri;
            }
            return string.Empty;
        }

        public bool IsUrlFromHost(string url, IEnumerable<DomainData> hosts)
        {
            if (!Uri.IsWellFormedUriString(url, UriKind.Absolute))
                return false;

            var uri = new Uri(url);
            return hosts.Any(x => x.Matches(uri.Host));
        }

        // the image is downloaded and kept by the bot, so its host does not matter
        public Task<ImageCheckResult> CheckImageUrlAsync(string str) => CheckImageUrlAsync(str, null);

        // the link itself is stored and shown later, so only hosts that keep it working are allowed
        public Task<ImageCheckResult> CheckSiteImageUrlAsync(string str) => CheckImageUrlAsync(str, _siteImageServices);

        public async Task<ImageCheckResult> CheckImageUrlAsync(string str, IEnumerable<DomainData> allowedHosts)
        {
            if (!Uri.IsWellFormedUriString(str, UriKind.Absolute))
                return ImageCheckResult.From(ImageUrlCheckResult.NotUrl);

            var url = new Uri(str);
            var restricted = !allowedHosts.IsNullOrEmpty();
            var host = restricted
                ? allowedHosts.FirstOrDefault(x => x.Matches(url.Host))
                : _siteImageServices.FirstOrDefault(x => x.Transform != null && x.Matches(url.Host));

            if (restricted && host == null)
                return ImageCheckResult.From(ImageUrlCheckResult.BlacklistedHost, allowedHosts: allowedHosts.Select(x => x.Url));

            var transform = false;
            if (host?.Transform != null)
            {
                var newStr = await host.Transform(str);
                if (string.IsNullOrEmpty(newStr))
                    return ImageCheckResult.From(ImageUrlCheckResult.TransformError);

                str = newStr;
                transform = true;
            }

            if (restricted && host.CheckExt && !IsUrlToImageSimple(str))
                return ImageCheckResult.From(ImageUrlCheckResult.WrongExtension);

            var (isImage, ext) = await IsUrlToImageAsync(str);
            if (!isImage)
                return ImageCheckResult.From(ImageUrlCheckResult.WrongExtension);

            return ImageCheckResult.From(transform ? ImageUrlCheckResult.UrlTransformed : ImageUrlCheckResult.Ok, str, ext);
        }

        public bool IsUrlToImageSimple(string url)
        {
            if (!Uri.IsWellFormedUriString(url, UriKind.Absolute))
                return false;

            var ext = Path.GetExtension(url).Replace(".", "");
            if (string.IsNullOrEmpty(ext) || !_extensions.Any(x => x.Equals(ext, StringComparison.CurrentCultureIgnoreCase)))
                return false;

            return true;
        }

        // Prosta ochrona SSRF: dopuszczamy tylko http(s) i publiczne adresy IP (bez rozwiazywania DNS)
        public static bool IsPublicHttpUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return false;

            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                return false;

            var host = uri.DnsSafeHost;
            if (string.IsNullOrEmpty(host))
                return false;

            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!IPAddress.TryParse(host, out var ip))
                return true;

            if (ip.IsIPv4MappedToIPv6)
                ip = ip.MapToIPv4();

            if (IPAddress.IsLoopback(ip) || ip.IsIPv6LinkLocal)
                return false;

            var b = ip.GetAddressBytes();
            if (b.Length == 4)
            {
                if (b[0] == 0 || b[0] == 10 || b[0] == 127) return false;
                if (b[0] == 169 && b[1] == 254) return false;
                if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return false;
                if (b[0] == 192 && b[1] == 168) return false;
                if (b[0] >= 224) return false;
            }
            else if (b[0] == 0xfc || b[0] == 0xfd)
            {
                return false; // unique local fc00::/7
            }

            return true;
        }

        // Allowlista formatów obrazów po sygnaturze (magic bytes). Sprawdzana PRZED wywołaniem
        // dekodera ImageSharp, aby odrzucić m.in. TIFF/EXR/ICC/PSD i inne formaty podatne na
        // advisory z 2026 (CVE-2026-106113 i seria) bez uruchamiania ich dekoderów.
        public static bool IsAllowedImageSignature(ReadOnlySpan<byte> header)
        {
            // PNG: 89 50 4E 47 0D 0A 1A 0A
            if (header.Length >= 8 && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47
                && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
                return true;

            // JPEG: FF D8 FF
            if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
                return true;

            // GIF: "GIF87a" / "GIF89a"
            if (header.Length >= 6 && header[0] == (byte)'G' && header[1] == (byte)'I' && header[2] == (byte)'F'
                && header[3] == (byte)'8' && (header[4] == (byte)'7' || header[4] == (byte)'9') && header[5] == (byte)'a')
                return true;

            // WEBP: "RIFF" .... "WEBP"
            if (header.Length >= 12 && header[0] == (byte)'R' && header[1] == (byte)'I' && header[2] == (byte)'F' && header[3] == (byte)'F'
                && header[8] == (byte)'W' && header[9] == (byte)'E' && header[10] == (byte)'B' && header[11] == (byte)'P')
                return true;

            return false;
        }

        private static async Task<bool> HasAllowedImageSignatureAsync(Stream stream)
        {
            var header = new byte[12];
            stream.Position = 0;

            int read = 0;
            while (read < header.Length)
            {
                int n = await stream.ReadAsync(header.AsMemory(read, header.Length - read));
                if (n <= 0) break;
                read += n;
            }

            stream.Position = 0;
            return IsAllowedImageSignature(header.AsSpan(0, read));
        }

        public async Task<(bool, string)> IsUrlToImageAsync(string url)
        {
            if (!_allowPrivateHosts && !IsPublicHttpUrl(url))
                return (false, string.Empty);

            try
            {
                using var res = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                if (res.IsSuccessStatusCode && !IsTooLarge(res))
                {
                    var type = (res.Content.Headers.ContentType?.MediaType ?? "").Split("/");
                    return (type.First().Equals("image", StringComparison.CurrentCultureIgnoreCase), type.Last());
                }
                return (false, string.Empty);
            }
            catch (Exception)
            {
                return (false, string.Empty);
            }
        }

        public async Task<Stream> TryGetImageStreamAsync(string url)
        {
            var stream = await GetImageFromUrlAsync(url);
            return stream ?? Stream.Null;
        }

        private static bool IsTooLarge(HttpResponseMessage res)
            => res.Content.Headers.ContentLength > MaxImageBytes;

        internal sealed class DownloadFailure
        {
            public string Reason { get; private set; }
            public HttpStatusCode? StatusCode { get; private set; }
            // every request since StartAttempt answered 404 or 410, so the picture does not exist
            public bool OnlyMissing { get; private set; } = true;

            // a request since StartAttempt failed in a way that passes (no connection, timeout, 5xx, 429, 408), so the
            // picture may be there a moment later
            public bool Temporary { get; private set; }

            public void StartAttempt()
            {
                OnlyMissing = true;
                Temporary = false;
            }

            public void Set(string reason, HttpStatusCode? statusCode = null)
            {
                Reason = reason;
                StatusCode = statusCode;
                OnlyMissing &= statusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone;
                Temporary |= statusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout || (int?)statusCode >= 500;
            }

            public void Set(Exception ex)
            {
                Set(ex is OperationCanceledException ? "timeout" : $"{ex.GetType().Name}: {ex.Message}");
                // the network or a timeout; a picture that downloads but cannot be read stays broken
                Temporary |= ex is HttpRequestException or OperationCanceledException or IOException;
            }
        }

        private static Stream Fail(DownloadFailure failure, Stream result, string reason, HttpStatusCode? statusCode = null)
        {
            failure?.Set(reason, statusCode);
            return result;
        }

        private async Task<Stream> GetImageFromUrlAsync(string url, bool fixExt = false, DownloadFailure failure = null)
        {
            failure ??= new DownloadFailure();
            try
            {
                var stream = await DownloadImageAsync(url, failure);
                if (stream != null)
                    return stream;

                if (fixExt)
                {
                    var splited = url.Split(".");
                    var originalExt = splited[^1];
                    foreach (var ext in _extensions)
                    {
                        // the url with its own extension was just tried
                        if (string.Equals(ext, originalExt, StringComparison.OrdinalIgnoreCase))
                            continue;

                        // more requests to a host that is rate limiting us only prolong the limit
                        if (failure.StatusCode == HttpStatusCode.TooManyRequests)
                            return null;

                        splited[splited.Length - 1] = ext;
                        stream = await DownloadImageAsync(string.Join(".", splited), failure);

                        if (stream != null)
                            return stream;
                    }
                }
            }
            catch (Exception ex)
            {
                failure?.Set(ex);
                return Stream.Null;
            }

            return null;
        }

        private async Task<Stream> DownloadImageAsync(string url, DownloadFailure failure = null)
        {
            if (!_allowPrivateHosts && !IsPublicHttpUrl(url))
                return Fail(failure, null, "not a public address");

            using var timeout = new CancellationTokenSource(_httpClient.Timeout);
            using var res = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!res.IsSuccessStatusCode)
                return Fail(failure, null, $"HTTP {(int)res.StatusCode}", res.StatusCode);

            if (IsTooLarge(res))
                return Fail(failure, Stream.Null, "file too large");

            var buffer = new MemoryStream();
            try
            {
                using var source = await res.Content.ReadAsStreamAsync(timeout.Token);
                var chunk = new byte[81920];
                int read;
                while ((read = await source.ReadAsync(chunk, timeout.Token)) > 0)
                {
                    if (buffer.Length + read > MaxImageBytes)
                    {
                        buffer.Dispose();
                        return Fail(failure, Stream.Null, "file too large");
                    }
                    buffer.Write(chunk, 0, read);
                }
                buffer.Position = 0;
                if (!await HasAllowedImageSignatureAsync(buffer))
                {
                    buffer.Dispose();
                    return Fail(failure, Stream.Null, "not a supported image");
                }

                buffer.Position = 0;
                if (!await HasAcceptableDimensionsAsync(buffer))
                {
                    buffer.Dispose();
                    return Fail(failure, Stream.Null, "image dimensions too large");
                }

                buffer.Position = 0;
                return buffer;
            }
            catch
            {
                buffer.Dispose();
                throw;
            }
        }

        private static async Task<bool> HasAcceptableDimensionsAsync(Stream stream)
        {
            try
            {
                var info = await Image.IdentifyAsync(stream);
                var frames = Math.Max(1, info.FrameMetadataCollection.Count);
                return info.Width <= MaxImageDimension && info.Height <= MaxImageDimension
                    && (long)info.Width * info.Height * frames <= MaxImagePixels;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // One card generation renders the card more than once (full and profile version),
        // the remote picture is downloaded for the first render only. A failed download (null) is kept as well.
        public sealed class CardPictureCache
        {
            private readonly Dictionary<string, byte[]> _pictures = new Dictionary<string, byte[]>();

            internal bool TryGet(string url, out byte[] picture) => _pictures.TryGetValue(url, out picture);

            internal void Add(string url, Stream picture) => _pictures[url] = (picture as MemoryStream)?.ToArray();

            internal bool HasFailure => _pictures.ContainsValue(null);

            // a picture failed in a way that passes, the card drawn now is not kept (Waifu.GenerateAndSaveCardAsync)
            internal bool TemporaryFailure { get; set; }
        }

        private async Task<Image> GetImageFromUrlOrLocalAsync(string uri, CardPictureCache pictures = null)
        {
            if (Dir.IsLocal(uri))
            {
                if (File.Exists(uri))
                {
                    var detectedFormat = await Image.DetectFormatAsync(uri);
                    if (detectedFormat == WebpFormat.Instance)
                    {
                        return await LoadWebpFromDiskAsync(uri);
                    }
                    return await Image.LoadAsync(uri);
                }
                return new Image<Rgba32>(1, 1);
            }

            if (pictures != null && pictures.TryGet(uri, out var cached))
                return cached is null ? new Image<Rgba32>(1, 1) : Image.Load(cached);

            try
            {
                using var imageStream = await GetImageFromUrlAsync(uri, true);
                if (imageStream is null || imageStream == Stream.Null)
                {
                    pictures?.Add(uri, null);
                    return new Image<Rgba32>(1, 1);
                }

                var image = await Image.LoadAsync(imageStream);
                pictures?.Add(uri, imageStream);
                return image;
            }
            catch (Exception)
            {
                pictures?.Add(uri, null);
                return new Image<Rgba32>(1, 1);
            }
        }

        private Font GetOrCreateFont(FontFamily family, float size)
            => _fonts.GetOrAdd((family, size), key => new Font(key.Item1, key.Item2));

        private Color GetOrCreateColor(string hex)
            => _colors.GetOrAdd(hex, x => Color.ParseHex(x));

        private Font GetFontSize(FontFamily fontFamily, float size, string text, float maxWidth)
        {
            var font = GetOrCreateFont(fontFamily, size);
            var measured = TextMeasurer.MeasureBounds(text, new TextOptions(font));

            while (measured.Width > maxWidth)
            {
                if (--size < 1) break;
                font = GetOrCreateFont(fontFamily, size);
                measured = TextMeasurer.MeasureBounds(text, new TextOptions(font));
            }

            return font;
        }

        private void CheckImageSize(Image image, Size size, bool strech)
        {
            if (image.Width > size.Width || image.Height > size.Height)
            {
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = size
                }));

                return;
            }

            if (!strech)
                return;

            if (image.Width < size.Width || image.Height < size.Height)
            {
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Stretch,
                    Size = size
                }));
            }
        }

        public async Task SaveImageFromUrlAsync(string url, string path)
            => await SaveImageFromUrlAsync(url, path, Size.Empty);

        public async Task<string> SaveCardImageFromUrlAsync(string url, Card card, bool animated = false)
        {
            var size = card.FromFigure ? new Size(475, 667) : new Size(448, 650);
            var saveDir = $"{Dir.LocalCardData}/CI{card.Id}.{(animated ? Waifu.AnimatedCardExtension : Waifu.NormalCardExtension)}";
            await SaveImageFromUrlAsync(url, saveDir, size);
            var fileInfo = new FileInfo(saveDir);
            if (fileInfo.Length > 40000000)
            {
                File.Delete(saveDir);
                throw new Exception("Too big");
            }
            card.CustomImage = saveDir;
            return saveDir;
        }

        public async Task<string> SaveCardBorderImageFromUrlAsync(string url, Card card)
        {
            var saveDir = $"{Dir.LocalCardData}/CB{card.Id}.webp";
            await SaveImageFromUrlAsync(url, saveDir, new Size(475, 667));
            card.CustomBorder = saveDir;
            return saveDir;
        }

        public async Task SaveImageFromUrlAsync(string url, string path, Size size, bool strech = false)
        {
            using (var stream = await GetImageFromUrlAsync(url, true))
            {
                if (stream is null || stream == Stream.Null)
                    throw new InvalidDataException("Image could not be downloaded");

                var detectedFormat = await Image.DetectFormatAsync(stream);
                using var image = detectedFormat == WebpFormat.Instance ?
                    await LoadWebpFromStreamAsync(stream) : await Image.LoadAsync(stream);

                if (size.Height > 0 || size.Width > 0)
                    CheckImageSize(image, size, strech);

                image.SaveToPath(path);
            }
        }

        /// <summary>
        /// Ładuje awatar z URL, a gdy obraz jest niedostępny (null / puste dane) zwraca 1x1 placeholder,
        /// dzięki czemu generowanie profilu nie kończy się wyjątkiem.
        /// </summary>
        private async Task<Image> GetAvatarImageOrPlaceholderAsync(string url)
        {
            using (var stream = await GetImageFromUrlAsync(url))
            {
                if (stream is null || stream == Stream.Null)
                    return new Image<Rgba32>(1, 1);

                return Image.Load(stream);
            }
        }

        private IEnumerable<Card> GetOrdertedCardsForGallery(User user, int count)
            => user.GetProfileGalleryCards(_galleryTag.Id, count);

        private string GetAvailableLevelBorder(string dir, long userLevel)
        {
            if (!Directory.Exists(dir))
                return string.Empty;

            long selected = 0;
            foreach(var file in Directory.GetFiles(dir))
            {
                if (file.Length <= dir.Length)
                    continue;

                var filename = file.AsSpan().Slice(dir.Length);
                var dotIdx = filename.IndexOf('.');
                if (dotIdx == -1)
                    continue;

                if (long.TryParse(filename.Slice(0, dotIdx), out var num))
                {
                    if (num <= userLevel && num > selected)
                        selected = num;
                }
            }
            return selected > 0 ? $"{dir}{selected}.png": string.Empty;
        }

        private void Animate(Image staleImage, Image animation, Point point)
        {
            staleImage.Metadata.GetWebpMetadata().RepeatCount = 0;
            for (int i = 0; i < animation.Frames.Count; i++)
            {
                using var newFrame = staleImage.CloneAs<Rgba32>();
                using var oldFrame = animation.Frames.CloneFrame(i);
                TransferFrameData(newFrame, oldFrame);
                newFrame.Mutate(x => x.DrawImage(oldFrame, point, 1));
                staleImage.Frames.AddFrame(newFrame.Frames.RootFrame);
            }
            staleImage.Frames.RemoveFrame(0);
        }

        private static void TransferFrameData(Image newFrame, Image sourceFrame)
        {
            // gives the delay in milliseconds whatever format the frame came from (a gif counts in hundredths)
            var frameDelayMs = sourceFrame.Frames.RootFrame.Metadata.GetWebpMetadata().FrameDelay;

            var newFrameMetadata = newFrame.Frames.RootFrame.Metadata.GetWebpMetadata();
            newFrameMetadata.DisposalMode = FrameDisposalMode.DoNotDispose;
            newFrameMetadata.BlendMode = FrameBlendMode.Source;
            newFrameMetadata.FrameDelay = frameDelayMs;
        }

        public Image<Rgba32> GetFColorsView(CurrencyType currency)
        {
            var message = GetOrCreateFont(_latoRegular, 16);
            var firstColumnMaxLength = TextMeasurer.MeasureBounds("A", new TextOptions(message));
            var secondColumnMaxLength = TextMeasurer.MeasureBounds("A", new TextOptions(message));

            var arrayOfColours = Enum.GetValues(typeof(FColor));
            var inFirstColumn = arrayOfColours.Length / 2;

            for (int i = 0; i < arrayOfColours.Length; i++)
            {
                var val = (uint)arrayOfColours.GetValue(i);

                var thisColor = (FColor)val;
                if (thisColor == FColor.None) continue;

                var name = $"{thisColor} ({thisColor.Price(currency)} {currency})";
                var nLen = TextMeasurer.MeasureBounds(name, new TextOptions(message));

                if (i < inFirstColumn + 1)
                {
                    if (firstColumnMaxLength.Width < nLen.Width)
                        firstColumnMaxLength = nLen;
                }
                else
                {
                    if (secondColumnMaxLength.Width < nLen.Width)
                        secondColumnMaxLength = nLen;
                }
            }

            int posY = 2;
            int posX = 0;
            int realWidth = (int)(firstColumnMaxLength.Width + secondColumnMaxLength.Width + 20);
            int realHeight = (int)(firstColumnMaxLength.Height + 2) * (inFirstColumn + 1);

            var imgBase = new Image<Rgba32>(realWidth, realHeight);
            imgBase.Mutate(x => x.BackgroundColor(GetOrCreateColor("#313338")));
            imgBase.Mutate(x => x.Paint(c => c.DrawText(new RichTextOptions(message) { Origin = new Point(0, 0) }, "Lista:", Brushes.Solid(GetOrCreateColor("#000000")), null)));

            for (int i = 0; i < arrayOfColours.Length; i++)
            {
                if (inFirstColumn + 1 == i)
                {
                    posY = 2;
                    posX = (int)firstColumnMaxLength.Width + 10;
                }

                var val = (uint)arrayOfColours.GetValue(i);

                var thisColor = (FColor)val;
                if (thisColor == FColor.None) continue;

                posY += (int)firstColumnMaxLength.Height + 2;
                var tname = $"{thisColor} ({thisColor.Price(currency)} {currency.ToString().ToUpper()})";
                imgBase.Mutate(x => x.Paint(c => c.DrawText(new RichTextOptions(message) { Origin = new Point(posX, posY) }, tname, Brushes.Solid(GetOrCreateColor(val.ToString("X6"))), null)));
            }

            return imgBase;
        }

        // A failed download would be saved into the card file for good, so a remote picture gets a few attempts
        // and a final failure is logged. Returns null when the picture could not be loaded.
        internal async Task<Image> LoadCharacterPictureAsync(string url, ulong cardId, CardPictureCache pictures = null, ulong characterId = 0)
        {
            if (Dir.IsLocal(url))
                return await GetImageFromUrlOrLocalAsync(url);

            if (_movedPictures.TryGetValue(url, out var moved))
                url = moved;

            if (pictures != null && pictures.TryGet(url, out var cached))
                return cached is null ? null : Image.Load(cached);

            var failure = new DownloadFailure();
            for (int attempt = 1; attempt <= CharacterPictureAttempts; attempt++)
            {
                failure.StartAttempt();
                try
                {
                    using var stream = await GetImageFromUrlAsync(url, true, failure);
                    if (stream != null && stream != Stream.Null)
                    {
                        var image = await Image.LoadAsync(stream);
                        pictures?.Add(url, stream);
                        return image;
                    }
                }
                catch (Exception ex)
                {
                    failure.Set(ex);
                }

                // a missing picture will not appear on the next attempt
                if (failure.StatusCode == HttpStatusCode.TooManyRequests || failure.OnlyMissing)
                    break;

                if (attempt < CharacterPictureAttempts)
                    await Task.Delay(CharacterPictureRetryDelayMs * attempt);
            }

            if (failure.OnlyMissing && characterId != 0)
            {
                var current = await LoadCurrentCharacterPictureAsync(url, cardId, characterId, pictures);
                if (current != null)
                    return current;
            }

            pictures?.Add(url, null);
            if (pictures != null && failure.Temporary)
                pictures.TemporaryFailure = true;
            var message = $"Images: character picture for card {cardId} could not be downloaded ({failure.Reason ?? "unknown"}): {url}";
            // a rate limit on the image host is temporary and nothing can be done about it, and a picture that no longer exists
            // (on Shinden or set by the player) is missing data, not a bot error, so both stay off the log channel
            if (failure.StatusCode == HttpStatusCode.TooManyRequests || failure.OnlyMissing)
                _logger?.LogRoutine(message);
            else
                _logger?.LogError(message);
            return null;
        }

        // A card keeps the picture url from its creation; when the picture on Shinden was replaced, the old url is gone.
        // The current picture is used instead, remembered for the other cards and saved into the cards of that character.
        private async Task<Image> LoadCurrentCharacterPictureAsync(string url, ulong cardId, ulong characterId, CardPictureCache pictures)
        {
            if (CurrentCharacterPicture == null)
                return null;

            string current;
            try
            {
                current = await CurrentCharacterPicture(characterId);
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Images: current picture of character {characterId}: {ex.Message}");
                return null;
            }

            if (string.IsNullOrEmpty(current) || current == url)
                return null;

            var image = await LoadCharacterPictureAsync(current, cardId, pictures);
            if (image == null)
                return null;

            if (_movedPictures.Count >= MaxMovedPictures)
                _movedPictures.Clear();
            _movedPictures[url] = current;
            _logger?.LogRoutine($"Images: character {characterId} has a new picture, card {cardId}: {url} -> {current}");

            if (CharacterPictureMoved != null)
                _ = SaveMovedPictureAsync(characterId, url, current);

            return image;
        }

        private async Task SaveMovedPictureAsync(ulong characterId, string oldUrl, string newUrl)
        {
            try
            {
                await CharacterPictureMoved(characterId, oldUrl, newUrl);
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Images: saving the new picture of character {characterId}: {ex.Message}");
            }
        }

        private async Task<Image> GetCharacterPictureAsync(Card card, CardPictureCache pictures)
        {
            var characterImg = card.FromFigure ? new Image<Rgba32>(475, 667) : await Image.LoadAsync(Dir.GetResource("PW/empty.png"));
            // only the Shinden picture can be refreshed, a picture set by the player stays as it is
            using (var image = await LoadCharacterPictureAsync(card.GetImage() ?? "http://cdn.shinden.eu/cdn1/other/placeholders/title/225x350.jpg",
                card.Id, pictures, card.CustomImage == null ? card.Character : 0))
            {
                if (image is null)
                    return characterImg;

                int startY = 0;
                if (characterImg.Width != image.Width)
                {
                    image.Mutate(x => x.Resize(new ResizeOptions
                    {
                        Mode = ResizeMode.Max,
                        Size = new Size(characterImg.Width, 0)
                    }));

                    if (characterImg.Height > image.Height)
                        startY = (characterImg.Height / 2) - (image.Height / 2);
                }
                characterImg.Mutate(x => x.DrawImage(image, new Point(0, startY), 1));
            }
            return characterImg;
        }

        private bool HasDereString(Card card) => card.GetGraphicsQuality() switch
        {
            Quality.Beta => false,
            Quality.Gamma => false,
            Quality.Epsilon => false,
            Quality.Theta => false,
            _ => true,
        };

        private string GetCustomBorderString(Card card)
        {
            var quality = card.GetGraphicsQuality();
            switch (quality)
            {
                case Quality.Epsilon:
                case Quality.Gamma:
                case Quality.Beta:
                case Quality.Theta:
                    return Dir.GetResource($"PW/CG/{quality}/Border/{card.Dere}.png");

                default:
                    return Dir.GetResource($"PW/CG/{quality}/Border{card.GetCardVariantString()}.png");
            }
        }

        private Image GenerateBorder(Card card)
        {
            var borderStr = Dir.GetResource($"PW/{card.Rarity}.png");
            var dereStr = Dir.GetResource($"PW/{card.Dere}.png");

            if (card.FromFigure)
            {
                borderStr = GetCustomBorderString(card);
                dereStr = Dir.GetResource($"PW/CG/{card.GetGraphicsQuality()}/Dere/{card.Dere}{card.GetCardDereVariantString()}.png");
            }

            var img = Image.Load(borderStr);
            if (HasDereString(card))
            {
                using var dere = Image.Load(dereStr);
                img.Mutate(x => x.DrawImage(dere, new Point(0, 0), 1));
            }

            if (card.FromFigure && card.UsesSigmaFallback())
                img.Mutate(x => x.Rotate(RotateMode.Rotate180));

            return img;
        }

        private async Task<Image> LoadCustomBorderAsync(Card card)
        {
            if (!card.HasCustomBorder())
                return GenerateBorder(card);

            var borderImg = await GetImageFromUrlOrLocalAsync(card.CustomBorder);
            if (borderImg is null)
                return GenerateBorder(card);

            return borderImg;
        }

        private void ApplyAlphaStats(Image<Rgba32> image, Card card)
        {
            using var blurredImage = image.Clone();
            blurredImage.Mutate(x => x.GaussianBlur(2f));

            var polyhp = new SixLabors.ImageSharp.Drawing.Polygon(new PointF[]{
                    new PointF(158, 39),
                    new PointF(317, 39),
                    new PointF(293, 81),
                    new PointF(182, 81),
            });

            var polyatk = new SixLabors.ImageSharp.Drawing.Polygon(new PointF[]{
                    new PointF(43, 521),
                    new PointF(149, 573),
                    new PointF(149, 622),
                    new PointF(43, 570),
            });

            var polydef = new SixLabors.ImageSharp.Drawing.Polygon(new PointF[]{
                    new PointF(325, 573),
                    new PointF(431, 521),
                    new PointF(432, 571),
                    new PointF(325, 623),
            });

            // the operation works on the area of the path, so the blurred copy is moved back to its place on the card
            image.Mutate(x => x.Paint(c =>
            {
                foreach (var poly in new[] { polyhp, polyatk, polydef })
                {
                    var at = new Point(-(int)MathF.Floor(poly.Bounds.X), -(int)MathF.Floor(poly.Bounds.Y));
                    c.Apply(poly, y => y.DrawImage(blurredImage, at, 1));
                }
            }));

            using var statsImg = Image.Load(Dir.GetResource("PW/CG/Alpha/hStats.png"));
            image.Mutate(x => x.DrawImage(statsImg, 1));

            var font = GetOrCreateFont(_latoBold, 22);
            int hp = card.GetHealthWithPenalty();
            int def = card.GetDefenceWithBonus();
            int atk = card.GetAttackWithBonus();

            image.Mutate(x => x.Paint(c => c.DrawText(new RichTextOptions(font) { Origin = new Point(220, 48) }, $"{hp}", Brushes.Solid(GetOrCreateColor("#ffffff")), null)));

            using (var defImg = new Image<Rgba32>(120, 40))
            {
                defImg.Mutate(x => x.Paint(c => c.DrawText(new RichTextOptions(font) { Origin = new Point(1) }, $"{def}", Brushes.Solid(GetOrCreateColor("#ffffff")), null)));
                defImg.Mutate(x => x.Rotate(-25));

                image.Mutate(x => x.DrawImage(defImg, new Point(360, 516), 1));
            }

            using (var atkImg = new Image<Rgba32>(120, 40))
            {
                atkImg.Mutate(x => x.Paint(c => c.DrawText(new RichTextOptions(font) { Origin = new Point(1) }, $"{atk}", Brushes.Solid(GetOrCreateColor("#ffffff")), null)));
                atkImg.Mutate(x => x.Rotate(25));

                image.Mutate(x => x.DrawImage(atkImg, new Point(68, 553), 1));
            }
        }

        private void ApplyBetaStats(Image<Rgba32> image, Card card)
        {
            var font = GetOrCreateFont(_latoBold, 22);

            int hp = card.GetHealthWithPenalty();
            int def = card.GetDefenceWithBonus();
            int atk = card.GetAttackWithBonus();

            using (var hpImg = new Image<Rgba32>(120, 40))
            {
                hpImg.Mutate(x => x.Paint(c => c.DrawText(new RichTextOptions(font) { Origin = new Point(1) }, $"{hp}", Brushes.Solid(GetOrCreateColor("#ffffff")), null)));
                hpImg.Mutate(x => x.Rotate(-13));

                image.Mutate(x => x.DrawImage(hpImg, new Point(30, 419), 1));
            }

            using (var atkImg = new Image<Rgba32>(120, 40))
            {
                atkImg.Mutate(x => x.Paint(c => c.DrawText(new RichTextOptions(font) { Origin = new Point(1) }, $"{atk}", Brushes.Solid(GetOrCreateColor("#ffffff")), null)));
                atkImg.Mutate(x => x.Rotate(14));

                image.Mutate(x => x.DrawImage(atkImg, new Point(374, 430), 1));
            }

            using (var defImg = new Image<Rgba32>(120, 40))
            {
                defImg.Mutate(x => x.Paint(c => c.DrawText(new RichTextOptions(font) { Origin = new Point(1) }, $"{def}", Brushes.Solid(GetOrCreateColor("#ffffff")), null)));
                defImg.Mutate(x => x.Rotate(14));

                image.Mutate(x => x.DrawImage(defImg, new Point(358, 502), 1));
            }
        }

        private void ApplyGammaStats(Image<Rgba32> image, Card card)
        {
            var aphFont = GetOrCreateFont(_digital, 26);

            int hp = card.GetHealthWithPenalty();
            int def = card.GetDefenceWithBonus();
            int atk = card.GetAttackWithBonus();

            var ops = new RichTextOptions(aphFont)
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                KerningMode = KerningMode.Auto,
                Origin = new Point(60, 1),
                Dpi = 80,
            };

            using (var atkImg = new Image<Rgba32>(120, 40))
            {
                atkImg.Mutate(x => x.Paint(c => c.DrawText(ops, $"{atk}", Brushes.Solid(GetOrCreateColor("#c9282c")), null)));
                atkImg.Mutate(x => x.Rotate(22));

                image.Mutate(x => x.DrawImage(atkImg, new Point(40, 514), 1));
            }

            ops.Origin = new Point(238, 563);
            image.Mutate(x => x.Paint(c => c.DrawText(ops, $"{hp}", Brushes.Solid(GetOrCreateColor("#318b19")), null)));

            using (var defImg = new Image<Rgba32>(120, 40))
            {
                ops.Origin = new Point(60, 1);
                defImg.Mutate(x => x.Paint(c => c.DrawText(ops, $"{def}", Brushes.Solid(GetOrCreateColor("#00527f")), null)));
                defImg.Mutate(x => x.Rotate(-22));

                image.Mutate(x => x.DrawImage(defImg, new Point(311, 513), 1));
            }
        }

        private void ApplyDeltaStats(Image<Rgba32> image, Card card, bool rotated = false)
        {
            var hpFont = GetOrCreateFont(_latoBold, 34);
            var adFont = GetOrCreateFont(_latoBold, 26);

            int hp = card.GetHealthWithPenalty();
            int def = card.GetDefenceWithBonus();
            int atk = card.GetAttackWithBonus();

            var drOps = new DrawingOptions() { Transform = new Matrix4x4(Matrix3x2.CreateRotation(-0.46f)) };
            var hpOps = new RichTextOptions(hpFont) { HorizontalAlignment = HorizontalAlignment.Center, Origin = new Point(114, 630) };
            bool hasVariants = !string.IsNullOrEmpty(card.GetCardVariantString());
            var customColor = GetOrCreateColor("#000000");
            Brush hpBrush = Brushes.Solid(hasVariants ? customColor : GetOrCreateColor("#356231"));
            Brush atkBrush = Brushes.Solid(hasVariants ? customColor : GetOrCreateColor("#78261a"));
            Brush defBrush = Brushes.Solid(hasVariants ? customColor : GetOrCreateColor("#00527f"));
            if (card.BorderVariant == 8)
            {
                defBrush = atkBrush = hpBrush = new LinearGradientBrush(new PointF(0, 0), new PointF(50, 25), GradientRepetitionMode.Repeat,
                    new ColorStop[] { new ColorStop(0f, GetOrCreateColor("#a8833c")), new ColorStop(0.5f, GetOrCreateColor("#f9eaaf")), new ColorStop(1f, GetOrCreateColor("#a8833c")) });
            }

            if (rotated)
            {
                DrawTextOnRotatedCard(image, hpFont, $"{hp}", new PointF(114, 630), hpBrush, -0.46f);
                DrawTextOnRotatedCard(image, adFont, $"{atk}", new PointF(92, 597), atkBrush);
                DrawTextOnRotatedCard(image, adFont, $"{def}", new PointF(382, 597), defBrush);
                return;
            }

            image.Mutate(x => x.Paint(drOps, c => c.DrawText(hpOps, $"{hp}", hpBrush, null)));
            var ops = new RichTextOptions(adFont) { HorizontalAlignment = HorizontalAlignment.Center, Origin = new Point(92, 597)};
            image.Mutate(x => x.Paint(c => c.DrawText(ops, $"{atk}", atkBrush, null)));
            ops.Origin = new Point(382, 597);
            image.Mutate(x => x.Paint(c => c.DrawText(ops, $"{def}", defBrush, null)));
        }

        private static void DrawTextOnRotatedCard(Image<Rgba32> image, Font font, string text, PointF origin, Brush brush, float rotation = 0)
        {
            var ops = new RichTextOptions(font) { HorizontalAlignment = HorizontalAlignment.Center, Origin = origin };
            var bounds = TextMeasurer.MeasureBounds(text, ops);
            var offset = new Vector2(bounds.X + bounds.Width / 2 - origin.X, bounds.Y + bounds.Height / 2 - origin.Y);

            var center = Vector2.Transform(new Vector2(origin.X, origin.Y) + offset, Matrix3x2.CreateRotation(rotation));
            var target = new Vector2(image.Width - center.X, image.Height - center.Y);

            ops.Origin = target - offset;
            var drOps = new DrawingOptions() { Transform = new Matrix4x4(Matrix3x2.CreateRotation(rotation, target)) };
            image.Mutate(x => x.Paint(drOps, c => c.DrawText(ops, text, brush, null)));
        }

        private void ApplyEpsilonStats(Image<Rgba32> image, Card card)
        {
            var aphFont = GetOrCreateFont(_latoBold, 22);

            int hp = card.GetHealthWithPenalty();
            int def = card.GetDefenceWithBonus();
            int atk = card.GetAttackWithBonus();

            var ops = new RichTextOptions(aphFont)
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                KerningMode = KerningMode.Auto,
                Origin = new Point(60, 1),
            };

            using (var atkImg = new Image<Rgba32>(120, 40))
            {
                atkImg.Mutate(x => x.Paint(c => c.DrawText(ops, $"{atk}", Brushes.Solid(GetOrCreateColor("#ffffff")), null)));
                atkImg.Mutate(x => x.Rotate(28));

                image.Mutate(x => x.DrawImage(atkImg, new Point(52, 554), 1));
            }

            ops.Origin = new Point(238, 592);
            image.Mutate(x => x.Paint(c => c.DrawText(ops, $"{hp}", Brushes.Solid(GetOrCreateColor("#ffffff")), null)));

            using (var defImg = new Image<Rgba32>(120, 40))
            {
                ops.Origin = new Point(60, 1);
                defImg.Mutate(x => x.Paint(c => c.DrawText(ops, $"{def}", Brushes.Solid(GetOrCreateColor("#ffffff")), null)));
                defImg.Mutate(x => x.Rotate(-26));

                image.Mutate(x => x.DrawImage(defImg, new Point(300, 554), 1));
            }
        }

        private void ApplyZetaStats(Image<Rgba32> image, Card card)
        {
            var aphFont = GetOrCreateFont(_digital, 28);

            int hp = card.GetHealthWithPenalty();
            int def = card.GetDefenceWithBonus();
            int atk = card.GetAttackWithBonus();

            var ops = new RichTextOptions(aphFont)
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Dpi = 80,
                Origin = new Point(370, 532)
            };
            image.Mutate(x => x.Paint(c => c.DrawText(ops, atk.ToString("D4"), Brushes.Solid(GetOrCreateColor("#da4e00")), null)));
            ops.Origin = new Point(370, 559);
            image.Mutate(x => x.Paint(c => c.DrawText(ops, def.ToString("D4"), Brushes.Solid(GetOrCreateColor("#00a4ff")), null)));
            ops.Origin = new Point(363, 587);
            image.Mutate(x => x.Paint(c => c.DrawText(ops, hp.ToString("D5"), Brushes.Solid(GetOrCreateColor("#40ff40")), null)));
        }

        private void ApplyEtaStats(Image<Rgba32> image, Card card)
        {
            var aphFont = GetOrCreateFont(_latoBold, 22);

            int hp = card.GetHealthWithPenalty();
            int def = card.GetDefenceWithBonus();
            int atk = card.GetAttackWithBonus();

            var ops = new RichTextOptions(aphFont)
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                KerningMode = KerningMode.Auto,
                Origin = new Point(60, 1),
            };

            using (var atkImg = new Image<Rgba32>(120, 40))
            {
                atkImg.Mutate(x => x.Paint(c => c.DrawText(ops, $"{atk}", Brushes.Solid(GetOrCreateColor("#ffffff")), null)));
                atkImg.Mutate(x => x.Rotate(22));

                image.Mutate(x => x.DrawImage(atkImg, new Point(57, 535), 1));
            }

            ops.Origin = new Point(238, 577);
            image.Mutate(x => x.Paint(c => c.DrawText(ops, $"{hp}", Brushes.Solid(GetOrCreateColor("#ffffff")), null)));

            using (var defImg = new Image<Rgba32>(120, 40))
            {
                ops.Origin = new Point(60, 1);
                defImg.Mutate(x => x.Paint(c => c.DrawText(ops, $"{def}", Brushes.Solid(GetOrCreateColor("#ffffff")), null)));
                defImg.Mutate(x => x.Rotate(-22));

                image.Mutate(x => x.DrawImage(defImg, new Point(290, 535), 1));
            }
        }

        private String GetJotaStatColorString(Card card)
        {
            switch (card.Dere)
            {
                case Database.Models.Dere.Bodere:
                    return "#de1218";
                case Database.Models.Dere.Dandere:
                    return "#00ff7d";
                case Database.Models.Dere.Deredere:
                    return "#032ee0";
                case Database.Models.Dere.Kamidere:
                    return "#75d400";
                case Database.Models.Dere.Kuudere:
                    return "#008cff";
                case Database.Models.Dere.Mayadere:
                    return "#dc0090";
                case Database.Models.Dere.Raito:
                    return "#dfdfdf";
                case Database.Models.Dere.Tsundere:
                    return "#ff0056";
                case Database.Models.Dere.Yami:
                    return "#898989";
                case Database.Models.Dere.Yandere:
                    return "#f2c400";
                case Database.Models.Dere.Yato:
                    return "#5e5e5e";
                default:
                    return "#ffffff";
            }
        }

        private void ApplyJotaStats(Image<Rgba32> image, Card card)
        {
            var aphFont = GetOrCreateFont(_latoBold, 22);

            int hp = card.GetHealthWithPenalty();
            int def = card.GetDefenceWithBonus();
            int atk = card.GetAttackWithBonus();

            var jotaColor = GetJotaStatColorString(card);

            using (var atkImg = new Image<Rgba32>(120, 40))
            {
                atkImg.Mutate(x => x.Paint(c => c.DrawText(new RichTextOptions(aphFont) { Origin = new Point(1) }, $"{atk}", Brushes.Solid(GetOrCreateColor(jotaColor)), null)));
                atkImg.Mutate(x => x.Rotate(-10));

                image.Mutate(x => x.DrawImage(atkImg, new Point(106, 545), 1));
            }

            using (var defImg = new Image<Rgba32>(120, 40))
            {
                defImg.Mutate(x => x.Paint(c => c.DrawText(new RichTextOptions(aphFont) { Origin = new Point(1) }, $"{def}", Brushes.Solid(GetOrCreateColor(jotaColor)), null)));
                defImg.Mutate(x => x.Rotate(10));

                image.Mutate(x => x.DrawImage(defImg, new Point(310, 557), 1));
            }

            var ops = new RichTextOptions(aphFont) { HorizontalAlignment = HorizontalAlignment.Center, Origin = new Point(238, 585) };
            image.Mutate(x => x.Paint(c => c.DrawText(ops, $"{hp}", Brushes.Solid(GetOrCreateColor(jotaColor)), null)));
        }

        private void ApplyLambdaStats(Image<Rgba32> image, Card card)
        {
            var aphFont = GetOrCreateFont(_latoBold, 28);

            int hp = card.GetHealthWithPenalty();
            int def = card.GetDefenceWithBonus();
            int atk = card.GetAttackWithBonus();

            using (var hpImg = new Image<Rgba32>(120, 40))
            {
                hpImg.Mutate(x => x.Paint(c => c.DrawText(new RichTextOptions(aphFont) { Origin = new Point(1) }, $"{hp}", Brushes.Solid(GetOrCreateColor("#6bedc8")), null)));
                hpImg.Mutate(x => x.Rotate(-19));

                image.Mutate(x => x.DrawImage(hpImg, new Point(57, 552), 1));
            }

            using (var atkImg = new Image<Rgba32>(120, 40))
            {
                atkImg.Mutate(x => x.Paint(c => c.DrawText(new RichTextOptions(aphFont) { Origin = new Point(1) }, $"{atk}", Brushes.Solid(GetOrCreateColor("#fda9fd")), null)));
                atkImg.Mutate(x => x.Rotate(34));

                image.Mutate(x => x.DrawImage(atkImg, new Point(80, 482), 1));
            }

            image.Mutate(x => x.Paint(c => c.DrawText(new RichTextOptions(aphFont) { Origin = new Point(326, 573) }, $"{def}", Brushes.Solid(GetOrCreateColor("#49deff")), null)));
        }

        private String GetThetaStatColorString(Card card)
        {
            switch (card.Dere)
            {
                case Database.Models.Dere.Bodere:
                    return "#ff2700";
                case Database.Models.Dere.Dandere:
                    return "#00fd8b";
                case Database.Models.Dere.Deredere:
                    return "#003bff";
                case Database.Models.Dere.Kamidere:
                    return "#f6f901";
                case Database.Models.Dere.Kuudere:
                    return "#008fff";
                case Database.Models.Dere.Mayadere:
                    return "#ff00df";
                case Database.Models.Dere.Raito:
                    return "#ffffff";
                case Database.Models.Dere.Tsundere:
                    return "#ff0072";
                case Database.Models.Dere.Yami:
                    return "#565656";
                case Database.Models.Dere.Yandere:
                    return "#ffa100";
                case Database.Models.Dere.Yato:
                    return "#ffffff";
                default:
                    return "#ffffff";
            }
        }

        private void ApplyThetaStats(Image<Rgba32> image, Card card)
        {
            var aphFont = GetOrCreateFont(_digital, 28);

            int hp = card.GetHealthWithPenalty();
            int def = card.GetDefenceWithBonus();
            int atk = card.GetAttackWithBonus();

            var thetaColor = GetThetaStatColorString(card);

            var ops = new RichTextOptions(aphFont)
            {
                KerningMode = KerningMode.Auto,
                Dpi = 80,
                HorizontalAlignment = HorizontalAlignment.Right,
                Origin = new Point(410, 511)
            };
            image.Mutate(x => x.Paint(c => c.DrawText(ops, $"{atk}", Brushes.Solid(GetOrCreateColor(thetaColor)), null)));
            ops.Origin = new Point(410, 548);
            image.Mutate(x => x.Paint(c => c.DrawText(ops, $"{def}", Brushes.Solid(GetOrCreateColor(thetaColor)), null)));
            ops.Origin = new Point(410, 585);
            image.Mutate(x => x.Paint(c => c.DrawText(ops, $"{hp}", Brushes.Solid(GetOrCreateColor(thetaColor)), null)));
        }

        private string GetStatsString(Card card)
        {
            bool isSpecialDere = card.Dere == Dere.Yami || card.Dere == Dere.Yato || card.Dere == Dere.Raito;
            var quality = card.GetGraphicsQuality();
            switch (quality)
            {
                case Quality.Beta:
                case Quality.Epsilon:
                    return isSpecialDere
                        ? Dir.GetResource($"PW/CG/{quality}/Stats/{card.Dere}.png")
                        : Dir.GetResource($"PW/CG/{quality}/Stats.png");

                case Quality.Gamma:
                case Quality.Jota:
                case Quality.Theta:
                    return Dir.GetResource($"PW/CG/{quality}/Stats/{card.Dere}.png");

                default:
                    return Dir.GetResource($"PW/CG/{quality}/Stats{card.GetCardVariantString()}.png");
            }
        }

        private string GetBorderBackString(Card card)
        {
            var quality = card.GetGraphicsQuality();
            switch (quality)
            {
                case Quality.Jota: return Dir.GetResource($"PW/CG/{quality}/Border/{card.Dere}.png");
                default: return Dir.GetResource($"PW/CG/{quality}/BorderBack{card.GetCardVariantString()}.png");
            }
        }

        private void ApplyUltimateStats(Image<Rgba32> image, Card card)
        {
            var rotated = card.UsesSigmaFallback();
            var statsStr = GetStatsString(card);
            if (File.Exists(statsStr))
            {
                using var stats = Image.Load(statsStr);
                if (rotated)
                    stats.Mutate(x => x.Rotate(RotateMode.Rotate180));

                image.Mutate(x => x.DrawImage(stats, new Point(0, 0), 1));
            }

            switch (card.GetGraphicsQuality())
            {
                case Quality.Alpha:
                    ApplyAlphaStats(image, card);
                    break;
                case Quality.Beta:
                    ApplyBetaStats(image, card);
                    break;
                case Quality.Gamma:
                    ApplyGammaStats(image, card);
                    break;
                case Quality.Delta:
                    ApplyDeltaStats(image, card, rotated);
                    break;
                case Quality.Epsilon:
                    ApplyEpsilonStats(image, card);
                    break;
                case Quality.Zeta:
                    ApplyZetaStats(image, card);
                    break;
                case Quality.Eta:
                    ApplyEtaStats(image, card);
                    break;
                case Quality.Jota:
                    ApplyJotaStats(image, card);
                    break;
                case Quality.Lambda:
                    ApplyLambdaStats(image, card);
                    break;
                case Quality.Theta:
                    ApplyThetaStats(image, card);
                    break;

                default:
                    break;
            }
        }

        private bool AllowStatsOnNoStatsImage(Card card)
        {
            switch (card.Quality)
            {
                case Quality.Zeta:
                    if (card.HasCustomBorder())
                        return false;
                    return true;

                default:
                    return false;
            }
        }

        private void ApplyStats(Image<Rgba32> image, Card card, bool applyNegativeStats = false)
        {
            int health = card.GetHealthWithPenalty();
            int defence = card.GetDefenceWithBonus();
            int attack = card.GetAttackWithBonus();

            using (var shield = Image.Load(Dir.GetResource("PW/heart.png")))
            {
                image.Mutate(x => x.DrawImage(shield, new Point(0, 0), 1));
            }

            using (var shield = Image.Load(Dir.GetResource("PW/shield.png")))
            {
                image.Mutate(x => x.DrawImage(shield, new Point(0, 0), 1));
            }

            using (var fire = Image.Load(Dir.GetResource("PW/fire.png")))
            {
                image.Mutate(x => x.DrawImage(fire, new Point(0, 0), 1));
            }

            var starType = card.GetCardStarType();
            var starColor = (starType % 5) + 1;
            starType = (starType / 5) + 1;

            var starCnt = card.GetCardStarCount();

            var starX = 239 - (19 * starCnt);
            for (int i = 0; i < starCnt; i++)
            {
                using (var fire = Image.Load(Dir.GetResource($"PW/stars/{card.StarStyle}/{starType}_{starColor}.png")))
                {
                    image.Mutate(x => x.DrawImage(fire, new Point(starX, 34), 1));
                }

                starX += 38;
            }

            int startXDef = 390;
            if (defence < 10) startXDef += 15;
            if (defence > 99) startXDef -= 15;

            int startXAtk = 390;
            if (attack < 10) startXAtk += 15;
            if (attack > 99) startXAtk -= 15;

            int startXHp = 380;
            if (health < 10) startXHp += 15;
            if (health > 99) startXHp -= 15;

            var numFont = GetOrCreateFont(_latoBold, 54);
            image.Mutate(x => x.Paint(c => c.DrawText(new RichTextOptions(numFont) { Origin = new Point(startXHp, 182) }, $"{health}", Brushes.Solid(GetOrCreateColor("#000000")), null)));
            image.Mutate(x => x.Paint(c => c.DrawText(new RichTextOptions(numFont) { Origin = new Point(startXAtk, 314) }, $"{attack}", Brushes.Solid(GetOrCreateColor("#000000")), null)));
            image.Mutate(x => x.Paint(c => c.DrawText(new RichTextOptions(numFont) { Origin = new Point(startXDef, 434) }, $"{defence}", Brushes.Solid(GetOrCreateColor("#000000")), null)));

            if (applyNegativeStats)
            {
                using (var neg = Image.Load(Dir.GetResource("PW/neg.png")))
                {
                    image.Mutate(x => x.DrawImage(neg, new Point(0, 0), 1));
                }
            }
        }

        private void ApplyBorderBack(Image<Rgba32> image, Card card)
        {
            var isFromFigureOriginalBorder = !card.HasCustomBorder() && card.FromFigure;
            var backBorderStr = GetBorderBackString(card);

            if (isFromFigureOriginalBorder && File.Exists(backBorderStr))
            {
                using var back = Image.Load(backBorderStr);
                if (card.UsesSigmaFallback())
                    back.Mutate(x => x.Rotate(RotateMode.Rotate180));

                image.Mutate(x => x.DrawImage(back, new Point(0, 0), 1));
            }
        }

        private async Task<Image<Rgba32>> GetWaifuCardNoStatsAsync(Card card, CardPictureCache pictures)
        {
            var image = new Image<Rgba32>(475, 667);

            ApplyBorderBack(image, card);

            using (var chara = await GetCharacterPictureAsync(card, pictures))
            {
                var mov = card.FromFigure ? 0 : 13;
                image.Mutate(x => x.DrawImage(chara, new Point(mov, mov), 1));
            }

            using (var bord = GenerateBorder(card))
            {
                image.Mutate(x => x.DrawImage(bord, new Point(0, 0), 1));
            }

            if (AllowStatsOnNoStatsImage(card))
            {
                ApplyUltimateStats(image, card);
            }

            return image;
        }

        private async Task<Image> LoadOrGetNewWaifuProfileCardAsync(Card card)
        {
            string ext = card.IsCardAnimated() ? Waifu.AnimatedCardExtension : Waifu.NormalCardExtension;
            string imageLocation = $"{Dir.CardsInProfiles}/{card.Id}.{ext}";
            if (File.Exists(imageLocation))
                return await Image.LoadAsync(imageLocation);

            return await GetWaifuInProfileCardAsync(card);
        }

        public Task<Image> GetWaifuInProfileCardAsync(Card card) => GetWaifuInProfileCardAsync(card, null);

        public virtual async Task<Image> GetWaifuInProfileCardAsync(Card card, CardPictureCache pictures)
        {
            if (card.Quality == Quality.Omega)
                return await GetOmegaCard(card, true, pictures);

            if (card.IsAnimatedImage)
                return await GetAnimatedWaifuCardAsync(card, true, pictures);

            var image = new Image<Rgba32>(475, 667);

            ApplyBorderBack(image, card);

            using (var chara = await GetCharacterPictureAsync(card, pictures))
            {
                var mov = card.FromFigure ? 0 : 13;
                image.Mutate(x => x.DrawImage(chara, new Point(mov, mov), 1));
            }

            using (var bord = await LoadCustomBorderAsync(card))
            {
                image.Mutate(x => x.DrawImage(bord, new Point(0, 0), 1));
            }

            if (AllowStatsOnNoStatsImage(card))
            {
                ApplyUltimateStats(image, card);
            }

            return image;
        }

        public Image GetDuelCardImage(DuelInfo info, DuelImage image, Image<Rgba32> win, Image<Rgba32> los)
        {
            int Xiw = 76;
            int Yt = 780;
            int Yi = 131;
            int Xil = 876;

            if (info.Side == DuelInfo.WinnerSide.Right)
            {
                Xiw = 876;
                Xil = 76;
            }

            var nameFont = GetOrCreateFont(_latoBold, 34);
            var img = (image != null) ? Image.Load(image.Uri((int)info.Side)) : Image.Load((DuelImage.DefaultUri((int)info.Side)));

            win.Mutate(x => x.Resize(new ResizeOptions
            {
                Mode = ResizeMode.Max,
                Size = new Size(450, 0)
            }));

            los.Mutate(x => x.Resize(new ResizeOptions
            {
                Mode = ResizeMode.Max,
                Size = new Size(450, 0)
            }));

            if (info.Side != DuelInfo.WinnerSide.Draw)
                los.Mutate(x => x.Grayscale());

            img.Mutate(x => x.DrawImage(win, new Point(Xiw, Yi), 1));
            img.Mutate(x => x.DrawImage(los, new Point(Xil, Yi), 1));

            var options = new RichTextOptions(nameFont) { HorizontalAlignment = HorizontalAlignment.Center, WrappingLength = win.Width, Origin = new Point(Xiw, Yt)};
            img.Mutate(x => x.Paint(c => c.DrawText(options, info.Winner.Name, Brushes.Solid(GetOrCreateColor(image != null ? image.Color : DuelImage.DefaultColor())), null)));
            options.Origin = new Point(Xil, Yt);
            img.Mutate(x => x.Paint(c => c.DrawText(options, info.Loser.Name, Brushes.Solid(GetOrCreateColor(image != null ? image.Color : DuelImage.DefaultColor())), null)));

            return img;
        }

        public Image GetCatchThatWaifuImage(Image card, string pokeImg, int xPos, int yPos)
        {
            var image = Image.Load(pokeImg);
            image.Mutate(x => x.DrawImage(card, new Point(xPos, yPos), 1));
            return image;
        }

        public async Task<Image> GetWaifuCardAsync(string url, Card card)
        {
            if (url == null)
                return await GetWaifuCardAsync(card);

            return Image.Load(url);
        }

        public Task<Image> GetWaifuCardAsync(Card card) => GetWaifuCardAsync(card, null);

        public virtual async Task<Image> GetWaifuCardAsync(Card card, CardPictureCache pictures)
        {
            if (card.Quality == Quality.Omega)
                return await GetOmegaCard(card, false, pictures);

            if (card.IsAnimatedImage)
                return await GetAnimatedWaifuCardAsync(card, false, pictures);

            var image = await GetWaifuCardNoStatsAsync(card, pictures);

            if (card.FromFigure)
            {
                ApplyUltimateStats(image, card);
            }
            else
            {
                ApplyStats(image, card, !card.HasImage());
            }

            return image;
        }

        private async Task<Image> LoadWebpFromStreamAsync(Stream file)
        {
            var ops = new WebpDecoderOptions()
            {
                BackgroundColorHandling = BackgroundColorHandling.Ignore
            };

            return await WebpDecoder.Instance.DecodeAsync(ops, file);
        }

        private async Task<Image> LoadWebpFromDiskAsync(string path)
        {
            using var fileStream = File.OpenRead(path);
            return await LoadWebpFromStreamAsync(fileStream);
        }

        private async Task<Image> GetOmegaCard(Card card, bool noStatsImage, CardPictureCache pictures)
        {
            using (var image = await GetImageFromUrlOrLocalAsync(card.GetImage() ?? "http://cdn.shinden.eu/cdn1/other/placeholders/title/225x350.jpg", pictures))
            {
                using var dere = await Image.LoadAsync(Dir.GetResource($"PW/CG/{card.Quality}/Dere/{card.Dere}.png"));
                using var stats = await Image.LoadAsync(Dir.GetResource($"PW/CG/{card.Quality}/Stats.png"));
                using var bottomImg = await LoadWebpFromDiskAsync(Dir.GetResource($"PW/CG/{card.Quality}/BorderBack.webp"));
                using var topImg = await LoadWebpFromDiskAsync(Dir.GetResource($"PW/CG/{card.Quality}/Border.webp"));

                var animation = new Image<Rgba32>(475, 667, Color.Transparent.ToPixel<Rgba32>());
                animation.Metadata.GetWebpMetadata().RepeatCount = 0;

                bool allowAnimation = topImg.Frames.Count == (card.IsAnimatedImage ? image.Frames.Count : 1);
                for (int i = 0; i < topImg.Frames.Count; i++)
                {
                    using var topImageFrame = topImg.Frames.CloneFrame(i);
                    using var bottomImageFrame = bottomImg.Frames.CloneFrame(i);

                    using var newFrame = new Image<Rgba32>(475, 667, Color.Transparent.ToPixel<Rgba32>());
                    TransferFrameData(newFrame, topImageFrame);

                    using var charFrame = allowAnimation ? image.Frames.CloneFrame(i) : image.CloneAs<Rgba32>();
                    newFrame.Mutate(x => x.DrawImage(bottomImageFrame, new Point(0, 0), 1));
                    newFrame.Mutate(x => x.DrawImage(charFrame, new Point(0, 0), 1));
                    newFrame.Mutate(x => x.DrawImage(topImageFrame, new Point(0, 0), 1));
                    newFrame.Mutate(x => x.DrawImage(dere, new Point(0, 0), 1));

                    if (!noStatsImage)
                    {
                        newFrame.Mutate(x => x.DrawImage(stats, new Point(0, 0), 1));
                        ApplyDeltaStats(newFrame, card);
                    }

                    animation.Frames.AddFrame(newFrame.Frames.RootFrame);
                }

                animation.Frames.RemoveFrame(0);
                return animation;
            }
        }

        private async Task<Image> GetAnimatedWaifuCardAsync(Card card, bool noStatsImage, CardPictureCache pictures)
        {
            using var characterImg = card.FromFigure ? new Image<Rgba32>(475, 667) : Image.Load(Dir.GetResource("PW/empty.png"));
            using (var cardImg = await GetImageFromUrlOrLocalAsync(card.GetImage() ?? "http://cdn.shinden.eu/cdn1/other/placeholders/title/225x350.jpg", pictures))
            {
                using (var image = cardImg is null ? characterImg : cardImg)
                {
                    int startY = 0;
                    if (characterImg.Width != image.Width)
                    {
                        image.Mutate(x => x.Resize(new ResizeOptions
                        {
                            Mode = ResizeMode.Max,
                            Size = new Size(characterImg.Width, 0)
                        }));

                        if (characterImg.Height > image.Height)
                            startY = (characterImg.Height / 2) - (image.Height / 2);
                    }

                    var animation = new Image<Rgba32>(475, 667, Color.Transparent.ToPixel<Rgba32>());
                    animation.Metadata.GetWebpMetadata().RepeatCount = 0;

                    for (int i = 0; i < image.Frames.Count; i++)
                    {
                        using var newFrame = new Image<Rgba32>(475, 667, Color.Transparent.ToPixel<Rgba32>());
                        using var oldFrame = image.Frames.CloneFrame(i);
                        TransferFrameData(newFrame, oldFrame);

                        using var newFrameChar = characterImg.CloneAs<Rgba32>();
                        newFrameChar.Mutate(x => x.DrawImage(oldFrame, new Point(0, startY), 1));

                        ApplyBorderBack(newFrame, card);

                        var mov = card.FromFigure ? 0 : 13;
                        newFrame.Mutate(x => x.DrawImage(newFrameChar, new Point(mov, mov), 1));

                        using (var border = GenerateBorder(card))
                        {
                            newFrame.Mutate(x => x.DrawImage(border, new Point(0, 0), 1));
                        }

                        if (AllowStatsOnNoStatsImage(card))
                        {
                            ApplyUltimateStats(newFrame, card);
                        }
                        else if (!noStatsImage)
                        {
                            if (card.FromFigure)
                            {
                                ApplyUltimateStats(newFrame, card);
                            }
                            else
                            {
                                ApplyStats(newFrame, card, !card.HasImage());
                            }
                        }

                        animation.Frames.AddFrame(newFrame.Frames.RootFrame);
                    }

                    animation.Frames.RemoveFrame(0);
                    return animation;
                }
            }
        }
    }
}
