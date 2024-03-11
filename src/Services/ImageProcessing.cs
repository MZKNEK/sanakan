#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services.PocketWaifu;
using Shinden;
using Shinden.Models;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Numerics;

namespace Sanakan.Services
{
    public class ImageProcessing
    {
        private FontFamily _digital = new FontCollection().Add("Fonts/Digital.ttf");
        private FontFamily _latoBold = new FontCollection().Add("Fonts/Lato-Bold.ttf");
        private FontFamily _latoLight = new FontCollection().Add("Fonts/Lato-Light.ttf");
        private FontFamily _latoRegular = new FontCollection().Add("Fonts/Lato-Regular.ttf");

        private readonly TagIcon _galleryTag;
        private readonly HttpClient _httpClient;
        private readonly ShindenClient _shclient;
        private Dictionary<string, Color> _colors;
        private Dictionary<(FontFamily, float), Font> _fonts;
        private readonly List<DomainData> _imageServices;
        private readonly string[] _extensions = new[] { "png", "jpg", "jpeg", "gif", "webp" };

        public ImageProcessing(ShindenClient shinden, TagIcon gallery)
        {
            _shclient = shinden;
            _galleryTag = gallery;
            _httpClient = new HttpClient();
            _fonts = new Dictionary<(FontFamily, float), Font>();
            _colors = new Dictionary<string, Color>();
            _imageServices = new List<DomainData>
            {
                new DomainData("sanakan.pl", true),
                new DomainData("i.imgur.com", true),
                new DomainData("cdn.imgchest.com", true),
                new DomainData("www.dropbox.com") { Transform = TransformDropboxAsync },
                new DomainData("dl.dropboxusercontent.com"),
                new DomainData("cdn.discordapp.com"),
                new DomainData("onedrive.live.com"),
            };
        }

        private async Task<string> TransformDropboxAsync(string url)
        {
            var res = await _httpClient.GetAsync(url.Replace("www.dropbox", "dl.dropbox"));
            if (res.IsSuccessStatusCode && res.Content.Headers.ContentType.MediaType.StartsWith("image"))
            {
                return res.RequestMessage.RequestUri.AbsoluteUri;
            }
            return string.Empty;
        }

        public bool IsUrlFromHost(string url, IEnumerable<DomainData> hosts)
        {
            if (!Uri.IsWellFormedUriString(url, UriKind.Absolute))
                return ImageCheckResult.From(ImageUrlCheckResult.NotUrl);

            var uri = new Uri(url);
            var host = hosts.FirstOrDefault(x => x.Url.Equals(uri.Host, StringComparison.CurrentCultureIgnoreCase));
            return host != null;
        }

        public Task<ImageCheckResult> CheckImageUrlAsync(string str) => CheckImageUrlAsync(str, _imageServices);

        public async Task<ImageCheckResult> CheckImageUrlAsync(string str, IEnumerable<DomainData> allowedHosts)
        {
            if (!Uri.IsWellFormedUriString(str, UriKind.Absolute))
                return ImageCheckResult.From(ImageUrlCheckResult.NotUrl);

            var url = new Uri(str);
            var transform = false;
            if (!allowedHosts.IsNullOrEmpty())
            {
                var host = allowedHosts.FirstOrDefault(x => x.Url.Equals(url.Host, StringComparison.CurrentCultureIgnoreCase));
                if (host == null)
                    return ImageCheckResult.From(ImageUrlCheckResult.BlacklistedHost);

                if (host.Transform != null)
                {
                    var newStr = await host.Transform(str);
                    if (string.IsNullOrEmpty(newStr))
                        return ImageCheckResult.From(ImageUrlCheckResult.TransformError);

                    str = newStr;
                    transform = true;
                }

                if (host.CheckExt && !IsUrlToImageSimple(str))
                    return ImageCheckResult.From(ImageUrlCheckResult.WrongExtension);
            }

            var (isImage, _) = await IsUrlToImageAsync(str);
            if (!isImage)
                return ImageCheckResult.From(ImageUrlCheckResult.WrongExtension);

            return ImageCheckResult.From(transform ? ImageUrlCheckResult.UrlTransformed : ImageUrlCheckResult.Ok, str);
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

        public async Task<(bool, string)> IsUrlToImageAsync(string url)
        {
            if (!Uri.IsWellFormedUriString(url, UriKind.Absolute))
                return (false, string.Empty);

            try
            {
                var res = await _httpClient.GetAsync(url);
                if (res.IsSuccessStatusCode)
                {
                    var type = res.Content.Headers.ContentType.MediaType.Split("/");
                    return (type.First().Equals("image", StringComparison.CurrentCultureIgnoreCase), type.Last());
                }
                return (false, string.Empty);
            }
            catch (Exception)
            {
                return (false, string.Empty);
            }
        }

        private async Task<Stream> GetImageFromUrlAsync(string url, bool fixExt = false)
        {
            try
            {
                var res = await _httpClient.GetAsync(url);
                if (res.IsSuccessStatusCode)
                    return await res.Content.ReadAsStreamAsync();

                if (fixExt)
                {
                    var splited = url.Split(".");
                    foreach (var ext in _extensions)
                    {
                        splited[splited.Length - 1] = ext;
                        res = await _httpClient.GetAsync(string.Join(".", splited));

                        if (res.IsSuccessStatusCode)
                            return await res.Content.ReadAsStreamAsync();
                    }
                }
            }
            catch (Exception)
            {
                return Stream.Null;
            }

            return null;
        }

        private async Task<Image> GetImageFromUrlOrLocalAsync(string uri)
        {
            if (Dir.IsLocal(uri))
                return File.Exists(uri) ? await Image.LoadAsync(uri) : new Image<Rgba32>(1, 1);

            try
            {
                using var imageStream = await GetImageFromUrlAsync(uri, true);
                return (imageStream is null || imageStream == Stream.Null)
                    ? new Image<Rgba32>(1, 1)
                    : await Image.LoadAsync(imageStream);
            }
            catch (Exception)
            {
                return new Image<Rgba32>(1, 1);
            }
        }

        private Font GetOrCreateFont(FontFamily family, float size)
        {
            if (_fonts.ContainsKey((family, size)))
                return _fonts[(family, size)];
            else
            {
                var font = new Font(family, size);
                _fonts.Add((family, size), font);
                return font;
            }
        }

        private Color GetOrCreateColor(string hex)
        {
            if (_colors.ContainsKey(hex))
                return _colors[hex];
            else
            {
                var color = Color.ParseHex(hex);
                _colors.Add(hex, color);
                return color;
            }
        }

        private Font GetFontSize(FontFamily fontFamily, float size, string text, float maxWidth)
        {
            var font = GetOrCreateFont(fontFamily, size);
            var measured = TextMeasurer.MeasureSize(text, new TextOptions(font));

            while (measured.Width > maxWidth)
            {
                if (--size < 1) break;
                font = GetOrCreateFont(fontFamily, size);
                measured = TextMeasurer.MeasureSize(text, new TextOptions(font));
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

        public async Task SplitImageToBackgroundAndStyleAsync(string url, string pathTop, Size sizeTop, string pathBot, Size sizeBot)
        {
            using var stream = await GetImageFromUrlAsync(url, true);
            using var image = Image.Load(stream);

            var width = Math.Max(sizeTop.Width, sizeBot.Width);
            var height = sizeTop.Height + sizeBot.Height;

            if (height > 0 || width > 0)
                CheckImageSize(image, new Size(width, height), true);

            var topImage = image.Clone(x => x.Crop(sizeTop.Width, sizeTop.Height));
            var botImage = image.Clone(x => x.Crop(new Rectangle(0, sizeTop.Height, sizeBot.Width, sizeBot.Height)));

            topImage.SaveToPath(pathTop);
            botImage.SaveToPath(pathBot);
        }

        public async Task<string> SaveCardImageFromUrlAsync(string url, Card card)
        {
            var size = card.FromFigure ? new Size(475, 667) : new Size(448, 650);
            var saveDir = $"{Dir.LocalCardData}/CI{card.Id}.webp";
            await SaveImageFromUrlAsync(url, saveDir, size);
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
                using (var image = Image.Load(stream))
                {
                    if (size.Height > 0 || size.Width > 0)
                        CheckImageSize(image, size, strech);

                    image.SaveToPath(path);
                }
            }
        }

        private Image<Rgba32> GetTopLevelImage(long topPos, User user)
        {
            var image = new Image<Rgba32>(195, 19, Color.Transparent);

            using var topImage = Image.Load(Dir.GetResource("np/mtop.png"));
            using var lvlImage = Image.Load(Dir.GetResource("np/mlvl.png"));

            var font = GetOrCreateFont(_latoRegular, 16);
            var topp = topPos > 999 ? $">1K" : ToShortSI(topPos);
            image.Mutate(x => x.DrawImage(topImage, new Point(0, 0), 1));
            image.Mutate(x => x.DrawText(topp, font, GetOrCreateColor("#a7a7a7"), new PointF(19.5f, 0)));
            image.Mutate(x => x.DrawImage(lvlImage, new Point(60, 0), 1));
            image.Mutate(x => x.DrawText(ToShortSI(user.Level), font, GetOrCreateColor("#a7a7a7"), new PointF(79.5f, 0)));

            var prevLvlExp = ExperienceManager.CalculateExpForLevel(user.Level);
            var nextLvlExp = ExperienceManager.CalculateExpForLevel(user.Level + 1);
            var expOnLvl = user.ExpCnt - prevLvlExp;
            var lvlExp = nextLvlExp - prevLvlExp;

            expOnLvl = expOnLvl < 0 ? 0 : expOnLvl;
            lvlExp = lvlExp < 0 ? expOnLvl + 1 : lvlExp;

            var barH = 14;
            using var backBar = new Image<Rgba32>(74, barH, GetOrCreateColor("#3f3f3f"));
            backBar.Mutate(x => x.Round(3));
            image.Mutate(x => x.DrawImage(backBar, new Point(114, 1), 1));

            int progressBarLength = (int)(72d * (expOnLvl / (double)lvlExp));
            if (progressBarLength > 0)
            {
                using var progressBar = new Image<Rgba32>(progressBarLength, barH - 2, GetOrCreateColor("#145DA0"));
                progressBar.Mutate(x => x.Round(3));
                image.Mutate(x => x.DrawImage(progressBar, new Point(115, 2), 1));
            }

            var expY = 3;
            var fontSmall = GetOrCreateFont(_latoBold, 10);
            string exp = $"{expOnLvl} / {lvlExp}";
            var mExp = TextMeasurer.MeasureSize(exp, new TextOptions(fontSmall));
            image.Mutate(x => x.DrawText(exp, fontSmall, GetOrCreateColor("#FFFFFF"), new Point(114 + ((int)(74 - mExp.Width) / 2), expY)));

            return image;
        }

        private Image<Rgba32> GetCurrencyImage(long currency, string coin)
        {
            var image = new Image<Rgba32>(74, 19, Color.Transparent);
            using var coinImage = Image.Load(coin);

            var font = GetOrCreateFont(_latoRegular, 16);
            image.Mutate(x => x.DrawImage(coinImage, new Point(0, 0), 1));
            image.Mutate(x => x.DrawText(ToShortSI(currency), font, GetOrCreateColor("#a7a7a7"), new PointF(coinImage.Width + 3.5f, 0)));
            return image;
        }

        private string ToShortSI(long num)
        {
            if (num >= 100000000)
                return (num / 1000000D).ToString("0.#M");

            if (num >= 1000000)
                return (num / 1000000D).ToString("0.##M");

            if (num >= 100000)
                return (num / 1000D).ToString("0.#K");

            if (num >= 10000)
                return (num / 1000D).ToString("0.##K");

            return num.ToString("#,0");
        }

        private Image<Rgba32> GetProfileBar(long topPos, User user)
        {
            var barTop = user.StatsStyleSettings.HasFlag(ProfileSettings.BarOnTop);
            var customBarOpacity = user.StatsStyleSettings.HasFlag(ProfileSettings.BarOpacity);

            var image = new Image<Rgba32>(750, 500, Color.Transparent);

            using var bar = new Image<Rgba32>(700, 40, GetOrCreateColor("#000000"));
            bar.Mutate(x => x.Round(10));
            image.Mutate(x => x.DrawImage(bar, new Point(25, barTop ? -16 : 476), customBarOpacity ? user.ProfileShadowsOpacity : 0.75f));

            var topBarY = barTop ? 3 : 480;
            using var userLevelImg = GetTopLevelImage(topPos, user);
            image.Mutate(x => x.DrawImage(userLevelImg, new Point(32, topBarY), 1));

            var coinX = 227;
            var coinOff = 84;
            using var msgImg = GetCurrencyImage((long)user.MessagesCnt, Dir.GetResource("np/mmsg.png"));
            image.Mutate(x => x.DrawImage(msgImg, new Point(coinX, topBarY), 1));
            coinX += coinOff;
            using var scImg = GetCurrencyImage(user.ScCnt, Dir.GetResource("np/msc.png"));
            image.Mutate(x => x.DrawImage(scImg, new Point(coinX, topBarY), 1));
            coinX += coinOff;
            using var tcImg = GetCurrencyImage(user.TcCnt, Dir.GetResource("np/mtc.png"));
            image.Mutate(x => x.DrawImage(tcImg, new Point(coinX, topBarY), 1));
            coinX += coinOff;
            using var acImg = GetCurrencyImage(user.AcCnt, Dir.GetResource("np/mac.png"));
            image.Mutate(x => x.DrawImage(acImg, new Point(coinX, topBarY), 1));
            coinX += coinOff;
            using var ctImg = GetCurrencyImage(user.GameDeck.CTCnt, Dir.GetResource("np/mct.png"));
            image.Mutate(x => x.DrawImage(ctImg, new Point(coinX, topBarY), 1));
            coinX += coinOff;
            using var pcImg = GetCurrencyImage(user.GameDeck.PVPCoins, Dir.GetResource("np/mpc.png"));
            image.Mutate(x => x.DrawImage(pcImg, new Point(coinX, topBarY), 1));

            return image;
        }

        private IEnumerable<Card> GetOrdertedCardsForGallery(User user, int count)
        {
            if (user.Id == 1)
                return user.GameDeck.Cards.OrderBy(x => x.Rarity).Take(count);

            if (string.IsNullOrEmpty(user.GameDeck.GalleryOrderedIds))
            {
                return user.GameDeck.GetOrderedGalleryCards(_galleryTag.Id).Take(count);
            }

            var ids = user.GameDeck.GalleryOrderedIds.Split(" ")
                .Select(x => Api.Models.UserSiteProfile.TryParseIds(x)).Distinct();

            var cards = new List<Card>();
            var cardsInGallery = user.GameDeck.GetOrderedGalleryCards(_galleryTag.Id).ToList();
            foreach (var id in ids)
            {
                if (id == 0)
                    continue;

                var card = cardsInGallery.FirstOrDefault(x => x.Id == id);
                if (card != null)
                {
                    cards.Add(card);
                    cardsInGallery.Remove(card);
                }
            }

            cards.AddRange(cardsInGallery);
            return cards.Take(count);
        }

        private async Task<Image> GetUserNewProfileStyleAsync(IUserInfo shindenUser, User botUser)
        {
            var shadowsOpacity = botUser.ProfileShadowsOpacity;
            var image = new Image<Rgba32>(750, 340, Color.Transparent);
            switch (botUser.ProfileType)
            {
                case ProfileType.Img:
                case ProfileType.StatsOnImg:
                case ProfileType.CardsOnImg:
                case ProfileType.MiniGalleryOnImg:
                {
                    if (!string.IsNullOrEmpty(botUser.StatsReplacementProfileUri))
                    {
                        using var usrImg = await GetImageFromUrlOrLocalAsync(botUser.StatsReplacementProfileUri);
                        if (usrImg.Width != 750 || usrImg.Height != 340)
                            usrImg.Mutate(x => x.Resize(750, 340));

                        image.Mutate(x => x.DrawImage(usrImg, new Point(0, 0), 1));
                    }

                    if (botUser.ProfileType == ProfileType.StatsOnImg || botUser.ProfileType == ProfileType.MiniGalleryOnImg)
                        goto case ProfileType.Stats;

                    if (botUser.ProfileType == ProfileType.CardsOnImg)
                        goto case ProfileType.Cards;
                }
                break;
                case ProfileType.Stats:
                case ProfileType.StatsWithImg:
                case ProfileType.MiniGallery:
                {
                    var isMiniGallery = botUser.ProfileType == ProfileType.MiniGallery || botUser.ProfileType == ProfileType.MiniGalleryOnImg;
                    var isOnImg = botUser.ProfileType == ProfileType.StatsOnImg || botUser.ProfileType == ProfileType.MiniGalleryOnImg;
                    var flip = botUser.StatsStyleSettings.HasFlag(ProfileSettings.Flip);
                    var statsX = flip ? 407 : 16;
                    var statsY = 24;

                    if (isMiniGallery)
                    {
                        if (botUser.StatsStyleSettings.HasFlag(ProfileSettings.ShowGallery))
                        {
                            if (isOnImg)
                            {
                                using var shadow = new Image<Rgba32>(331, 275, GetOrCreateColor("#000000"));
                                shadow.Mutate(x => x.Round(10));
                                image.Mutate(x => x.DrawImage(shadow, new Point(statsX - 3, statsY - 3), shadowsOpacity));
                            }

                            var isSmall = botUser.StatsStyleSettings.HasFlag(ProfileSettings.HalfGallery);
                            var startX = statsX + (isSmall ? 6 : 12);
                            var cardGap = isSmall ? 160 : 104;
                            var cardSize = isSmall ? 215 : 131;
                            var cardY = statsY + (isSmall ? 26 : 2);
                            var cardX = startX;

                            var cardsToShow = GetOrdertedCardsForGallery(botUser, isSmall ? 2 : 6);
                            foreach (var card in cardsToShow)
                            {
                                using var cardImage = await LoadOrGetNewWaifuProfileCardAsync(card);
                                cardImage.Mutate(x => x.Resize(new ResizeOptions { Mode = ResizeMode.Max, Size = new Size(0, cardSize) }));
                                image.Mutate(x => x.DrawImage(cardImage, new Point(cardX, cardY), 1));
                                cardX += cardGap;

                                if (cardX > ((cardGap * 2) + startX))
                                {
                                    cardX = startX;
                                    cardY += 135;
                                }
                            }
                        }
                    }
                    else
                    {
                        if (shindenUser != null)
                        {
                            using var shadow = new Image<Rgba32>(331, 128, GetOrCreateColor("#000000"));
                            shadow.Mutate(x => x.Round(10));

                            if (shindenUser?.ListStats?.AnimeStatus != null && botUser.StatsStyleSettings.HasFlag(ProfileSettings.ShowAnime))
                            {
                                if (isOnImg)
                                    image.Mutate(x => x.DrawImage(shadow, new Point(statsX - 3, statsY - 3), shadowsOpacity));

                                using var stats = GetRWStats(shindenUser?.ListStats?.AnimeStatus, Dir.GetResource("statsAnime.png"), shindenUser.GetMoreSeriesStats(false));
                                image.Mutate(x => x.DrawImage(stats, new Point(statsX, statsY), 1));
                                statsY += 147;
                            }
                            if (shindenUser?.ListStats?.MangaStatus != null && botUser.StatsStyleSettings.HasFlag(ProfileSettings.ShowManga))
                            {
                                if (isOnImg)
                                    image.Mutate(x => x.DrawImage(shadow, new Point(statsX - 3, statsY - 3), shadowsOpacity));

                                using var stats = GetRWStats(shindenUser?.ListStats?.MangaStatus, Dir.GetResource("statsManga.png"), shindenUser.GetMoreSeriesStats(true));
                                image.Mutate(x => x.DrawImage(stats, new Point(statsX, statsY), 1));
                            }
                        }
                    }

                    statsY = 24;
                    statsX += flip ? -391 : 352;

                    if (botUser.StatsStyleSettings.HasFlag(ProfileSettings.ShowCards))
                    {
                        if (isOnImg)
                        {
                            using var shadow = new Image<Rgba32>(370, 275, GetOrCreateColor("#000000"));
                            shadow.Mutate(x => x.Round(10));
                            image.Mutate(x => x.DrawImage(shadow, new Point(statsX - 3, statsY - 3), shadowsOpacity));
                        }

                        using var cards = await GetWaifuProfileStyleImage(botUser);
                        image.Mutate(x => x.DrawImage(cards, new Point(statsX, statsY), 1));
                    }

                    if (botUser.ProfileType == ProfileType.StatsWithImg)
                        goto case ProfileType.Img;
                }
                break;
                case ProfileType.Cards:
                {
                    var cardY = 4;
                    var cardX = 19;

                    var cardsToShow = GetOrdertedCardsForGallery(botUser, 12);
                    foreach (var card in cardsToShow)
                    {
                        using var cardImage = await LoadOrGetNewWaifuProfileCardAsync(card);
                        cardImage.Mutate(x => x.Resize(new ResizeOptions { Mode = ResizeMode.Max, Size = new Size(0, 150) }));
                        image.Mutate(x => x.DrawImage(cardImage, new Point(cardX, cardY), 1));
                        cardX += 121;

                        if (cardX > 730)
                        {
                            cardX = 19;
                            cardY += 155;
                        }
                    }
                }
                break;

                default:
                break;
            }
            return image;
        }

        private async Task<Image> GetWaifuProfileStyleImage(User botUser)
        {
            var image = new Image<Rgba32>(364, 269);

            if (botUser.GameDeck.Waifu != 0)
            {
                var waifuCard = botUser.GameDeck.GetWaifuCard();
                if (waifuCard != null)
                {
                    using var cardImage = await LoadOrGetNewWaifuProfileCardAsync(waifuCard);
                    cardImage.Mutate(x => x.Resize(new ResizeOptions { Mode = ResizeMode.Max, Size = new Size(0, 260) }));
                    image.Mutate(x => x.DrawImage(cardImage, new Point(10, 4), 1));
                }
            }

            var font = GetOrCreateFont(_latoBold, 16);
            var fontDetail = GetOrCreateFont(_latoBold, 9);
            var fontColor = GetOrCreateColor("#a7a7a7");
            var fontColorDetail = GetOrCreateColor("#7f7f7f");

            var oGap = 60;
            var startY = 18;
            var startX = 213;
            image.Mutate(x => x.DrawText($"Posiadane", fontDetail, fontColorDetail, new Point(startX, startY - 10)));
            image.Mutate(x => x.DrawText($"Limit", fontDetail, fontColorDetail, new Point(startX + oGap, startY - 10)));
            image.Mutate(x => x.DrawText($"{botUser.GameDeck.Cards.Count}", font, fontColor, new Point(startX, startY)));
            image.Mutate(x => x.DrawText($"{botUser.GameDeck.MaxNumberOfCards}", font, fontColor, new Point(startX + oGap, startY)));

            if (botUser.GameDeck.CanCreateDemon() || botUser.GameDeck.CanCreateAngel() || botUser.GameDeck.IsNeutral())
            {
                var karmaState = Dir.GetResource("np/kn.png");
                karmaState = botUser.GameDeck.CanCreateDemon() ? Dir.GetResource("np/kd.png") : karmaState;
                karmaState = botUser.GameDeck.CanCreateAngel() ? Dir.GetResource("np/kl.png") : karmaState;
                using var karmaImage = Image.Load(karmaState);
                image.Mutate(x => x.DrawImage(karmaImage, new Point(330, startY - 6), 1));
            }

            startY += 29;
            var cGap = 38;
            var jumpY = 24;

            var fontCards = GetOrCreateFont(_latoBold, 17);
            foreach (Rarity rarity in Enum.GetValues(typeof(Rarity)))
            {
                var cardCount = botUser.GameDeck.Cards.Count(x => x.Rarity == rarity);
                if (cardCount > 0)
                {
                    using var cimg = Image.Load(Dir.GetResource($"np/r{rarity}.png"));
                    image.Mutate(x => x.DrawImage(cimg, new Point(startX, startY), 1));
                    image.Mutate(x => x.DrawText(cardCount.ToString(), fontCards, fontColor, new Point(startX + cGap, startY + 1)));
                    startY += jumpY;
                }
            }

            startY = 246;
            var sGap = 84;

            var scalpelCount = botUser.GameDeck.Cards.Count(x => !string.IsNullOrEmpty(x.CustomImage));
            if (scalpelCount > 0)
            {
                using var scalpelImg = GetCurrencyImage(scalpelCount, Dir.GetResource("np/mscal.png"));
                image.Mutate(x => x.DrawImage(scalpelImg, new Point(startX, startY), 1));
            }

            var scissorsCount = botUser.GameDeck.Cards.Count(x => !string.IsNullOrEmpty(x.CustomBorder));
            if (scissorsCount > 0)
            {
                using var scissorsImg = GetCurrencyImage(scissorsCount, Dir.GetResource("np/mbor.png"));
                image.Mutate(x => x.DrawImage(scissorsImg, new Point(startX + sGap, startY), 1));
            }

            return image;
        }

        public async Task<Image<Rgba32>> GetUserProfileAsync(IUserInfo shindenUser, User botUser, string avatarUrl, long topPos, string nickname, Discord.Color color)
        {
            color = color == Discord.Color.Default ? Discord.Color.DarkerGrey : color;
            string rangName = botUser.Id == 1 ? "Safeguard" : (shindenUser?.Rank ?? "");
            string colorRank = color.RawValue.ToString("X6");

            var nickFont = GetFontSize(_latoBold, 28, nickname, 290);
            var rangFont = GetOrCreateFont(_latoRegular, 16);

            using var template = Image.Load(Dir.GetResource("np/pbase.png"));
            var profilePic = new Image<Rgba32>(template.Width, template.Height, GetOrCreateColor("#313338"));

            using (var userBg = await GetImageFromUrlOrLocalAsync(botUser.BackgroundProfileUri))
            {
                if (userBg.Width != 750 || userBg.Height != 160)
                    userBg.Mutate(x => x.Resize(750, 160));

                profilePic.Mutate(x => x.DrawImage(userBg, new Point(0, 0), 1));
                profilePic.Mutate(x => x.DrawImage(template, new Point(0, 0), 1));
            }

            using var profileStyle = await GetUserNewProfileStyleAsync(shindenUser, botUser);
            profilePic.Mutate(x => x.DrawImage(profileStyle, new Point(0, 160), 1));

            var nX = 27;
            var aX = 47 + 5;
            var aY = 68;

            var aSize = 80;

            var bSize = 2;
            var tSize = aSize + (bSize * 2);

            var gSize = 1;
            var gOff = gSize * 2;

            nX += aX + tSize;

            var nY = 103;
            var defFontColor = GetOrCreateColor("#7f7f7f");
            profilePic.Mutate(x => x.DrawText(nickname, nickFont, GetOrCreateColor("#a7a7a7"), new Point(nX, nY + (int)((30 - nickFont.Size) / 2))));
            profilePic.Mutate(x => x.DrawText(rangName, rangFont, defFontColor, new Point(nX, nY + 30)));

            if (botUser.StatsStyleSettings.HasFlag(ProfileSettings.ShowOverlay) && !string.IsNullOrEmpty(botUser.CustomProfileOverlayUrl))
            {
                using var overlayImg = await GetImageFromUrlOrLocalAsync(botUser.CustomProfileOverlayUrl);

                if (overlayImg.Width != 750 || overlayImg.Height != 402)
                    overlayImg.Mutate(x => x.Resize(750, 402));

                profilePic.Mutate(x => x.DrawImage(overlayImg, new Point(0, 98), 1));
            }

            var hasAvBorder = botUser.AvatarBorder != AvatarBorder.None;
            using (var avatar = Image.Load(await GetImageFromUrlAsync(avatarUrl)))
            {
                var hasRoundAvatar = botUser.StatsStyleSettings.HasFlag(ProfileSettings.RoundAvatar) || hasAvBorder;
                using var webpAvatarStream = avatar.ToWebpStream();
                using var userAvatar = Image.Load(webpAvatarStream);

                using var avBack = new Image<Rgba32>(tSize, tSize, GetOrCreateColor("#3f3f3f"));
                if (hasRoundAvatar) avBack.Mutate(x => x.Round(hasAvBorder ? 40 : 44));
                profilePic.Mutate(x => x.DrawImage(avBack, new Point(aX, aY), 1));

                using var rang = new Image<Rgba32>(aSize + gOff, aSize + gOff, GetOrCreateColor(colorRank));
                if (hasRoundAvatar) rang.Mutate(x => x.Round(hasAvBorder ? 40 : 44));
                profilePic.Mutate(x => x.DrawImage(rang, new Point(aX + gSize, aY + gSize), 1));

                userAvatar.Mutate(x => x.Resize(new Size(aSize, aSize)));
                if (hasRoundAvatar) userAvatar.Mutate(x => x.Round(hasAvBorder ? 40 : 44));
                profilePic.Mutate(x => x.DrawImage(userAvatar, new Point(aX + bSize, aY + bSize), 1));
            }

            if (botUser.GameDeck.Waifu != 0 && botUser.StatsStyleSettings.HasFlag(ProfileSettings.ShowWaifu))
            {
                var waifuCard = botUser.GameDeck.GetWaifuCard();
                if (waifuCard != null)
                {
                    using var cardImage = await LoadOrGetNewWaifuProfileCardAsync(waifuCard);
                    cardImage.Mutate(x => x.Resize(new ResizeOptions { Mode = ResizeMode.Max, Size = new Size(0, 120) }));
                    using var cardBg = new Image<Rgba32>(cardImage.Width + 8, cardImage.Height + 8, GetOrCreateColor("#000000"));
                    cardBg.Mutate(x => x.Round(10));

                    profilePic.Mutate(x => x.DrawImage(cardBg, new Point(618, 30), 0.32f));
                    profilePic.Mutate(x => x.DrawImage(cardImage, new Point(622, 34), 1));
                }
            }

            if (hasAvBorder)
            {
                var (xy, img) = GetProfileAvatarBorder(botUser);
                using var border = Image.Load(img);
                profilePic.Mutate(x => x.DrawImage(border, xy, 1));
            }

            if (botUser.StatsStyleSettings.HasFlag(ProfileSettings.ShowOverlayPro) && !string.IsNullOrEmpty(botUser.PremiumCustomProfileOverlayUrl))
            {
                using var overlayImg = await GetImageFromUrlOrLocalAsync(botUser.PremiumCustomProfileOverlayUrl);

                if (overlayImg.Width != 750 || overlayImg.Height != 500)
                    overlayImg.Mutate(x => x.Resize(750, 500));

                profilePic.Mutate(x => x.DrawImage(overlayImg, new Point(0, 0), 1));
            }

            using var profileBar = GetProfileBar(topPos, botUser);
            profilePic.Mutate(x => x.DrawImage(profileBar, new Point(0, 0), 1));

            return profilePic;
        }

        private (Point xy, string img) GetProfileAvatarBorder(User user)
        {
            var img = Dir.GetResource($"np/ab/{user.AvatarBorder}.png");
            if (user.StatsStyleSettings.HasFlag(ProfileSettings.BorderColor))
            {
                var lvlImg = GetAvailableLevelBorder(Dir.GetResource($"np/ab/{user.AvatarBorder}Lv/"), user.Level);
                img = string.IsNullOrEmpty(lvlImg) ? img : lvlImg;
            }

            var pos = user.AvatarBorder switch
            {
                AvatarBorder.Bow => new Point(26, 48),
                AvatarBorder.Dzedai => new Point(23, 38),
                AvatarBorder.Water => new Point(35, 58),
                AvatarBorder.Base => new Point(43, 59),
                AvatarBorder.Crows => new Point(25, 42),
                AvatarBorder.Metal => new Point(49, 65),
                AvatarBorder.RedThinLeaves => new Point(30, 44),
                AvatarBorder.Skull => new Point(28, 47),
                AvatarBorder.Fire => new Point(45, 60),
                AvatarBorder.Promium => new Point(38, 53),
                AvatarBorder.Ice => new Point(44, 61),
                AvatarBorder.Gold => new Point(18, 33),
                AvatarBorder.Red => new Point(40, 58),
                AvatarBorder.Rainbow => new Point(44, 60),
                AvatarBorder.Pink => new Point(38, 53),
                AvatarBorder.Simple => new Point(36, 53),
                _ => new Point(24, 39)
            };

            return (pos, img);
        }

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

        private async Task<Image<Rgba32>> GetSiteStatisticUserBadge(string avatarUrl, string name, string color)
        {
            var font = GetFontSize(_latoBold, 32, name, 360);

            var badge = new Image<Rgba32>(450, 65);
            badge.Mutate(x => x.DrawText(name, font, GetOrCreateColor("#A4A4A4"), new Point(72, 3 + (int)((58 - font.Size) / 2))));

            using (var border = new Image<Rgba32>(3, 57))
            {
                border.Mutate(x => x.BackgroundColor(GetOrCreateColor(color)));
                badge.Mutate(x => x.DrawImage(border, new Point(63, 5), 1));
            }

            using (var stream = await GetImageFromUrlAsync(avatarUrl))
            {
                if (stream == null)
                    return badge;

                using (var avatar = Image.Load(stream))
                {
                    avatar.Mutate(x => x.Resize(new ResizeOptions
                    {
                        Mode = ResizeMode.Crop,
                        Size = new Size(57, 57)
                    }));
                    badge.Mutate(x => x.DrawImage(avatar, new Point(6, 5), 1));
                }
            }

            return badge;
        }

        private Image GetRWStats(ISeriesStatus status, string path, MoreSeriesStatus more)
        {
            int startPointX = 7;
            int startPointY = 3;
            var baseImg = Image.Load(path);

            if (status.Total.HasValue && status.Total > 0)
            {
                using var bar = GetStatusBar(status.Total.Value, status.InProgress.Value, status.Completed.Value,
                     status.Skipped.Value, status.OnHold.Value, status.Dropped.Value, status.InPlan.Value);

                bar.Mutate(x => x.Round(5));
                baseImg.Mutate(x => x.DrawImage(bar, new Point(startPointX, startPointY), 1));
            }

            startPointY += 23;
            startPointX += 110;
            int ySecondStart = startPointY;
            int fontSizeAndInterline = 16;
            var font = GetOrCreateFont(_latoBold, 13);
            int xSecondRow = startPointX + 200;
            var fontColor = GetOrCreateColor("#a7a7a7");

            ulong?[] rowArr = { status?.InProgress, status?.Completed, status?.Skipped, status?.OnHold, status?.Dropped, status?.InPlan };
            for (int i = 0; i < rowArr.Length; i++)
            {
                baseImg.Mutate(x => x.DrawText($"{rowArr[i]}", font, fontColor, new Point(startPointX, startPointY)));
                startPointY += fontSizeAndInterline;
            }

            var gOptions = new RichTextOptions(font)
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                Origin = new Point(xSecondRow, ySecondStart)
            };

            baseImg.Mutate(x => x.DrawText(gOptions, $"{more?.Score?.Rating.Value:0.0}", fontColor));
            ySecondStart += fontSizeAndInterline;
            gOptions.Origin = new Point(xSecondRow, ySecondStart);

            baseImg.Mutate(x => x.DrawText(gOptions, $"{status?.Total}", fontColor));
            ySecondStart += fontSizeAndInterline;
            gOptions.Origin = new Point(xSecondRow, ySecondStart);

            baseImg.Mutate(x => x.DrawText(gOptions, $"{more?.Count}", fontColor));
            ySecondStart += fontSizeAndInterline;

            var listTime = new List<string>();
            if (more.Time != null)
            {
                if (more.Time.Years != 0) listTime.Add($"{more?.Time?.Years} lat");
                if (more.Time.Months != 0) listTime.Add($"{more?.Time?.Months} mies.");
                if (more.Time.Days != 0) listTime.Add($"{more?.Time?.Days} dni");
                if (more.Time.Hours != 0) listTime.Add($"{more?.Time?.Hours} h");
                if (more.Time.Minutes != 0) listTime.Add($"{more?.Time?.Minutes} m");
            }

            ySecondStart += fontSizeAndInterline;
            gOptions.Origin = new Point(xSecondRow, ySecondStart);

            if (listTime.Count > 2)
            {
                string fs = listTime.First(); listTime.Remove(fs);
                string sc = listTime.First(); listTime.Remove(sc);
                baseImg.Mutate(x => x.DrawText(gOptions, $"{fs} {sc}", fontColor));

                ySecondStart += fontSizeAndInterline;
                gOptions.Origin = new Point(xSecondRow, ySecondStart);
                baseImg.Mutate(x => x.DrawText(gOptions, $"{string.Join<string>(" ", listTime)}", fontColor));
            }
            else
            {
                baseImg.Mutate(x => x.DrawText(gOptions, $"{string.Join<string>(" ", listTime)}", fontColor));
            }

            return baseImg;
        }

        private Image<Rgba32> GetStatusBar(ulong all, ulong green, ulong blue, ulong purple, ulong yellow, ulong red, ulong grey)
        {
            int offset = 0;
            int length = 311;
            int fixedLength = 0;

            var arrLength = new int[6];
            var arrProcent = new double[6];
            double[] arrValues = { green, blue, purple, yellow, red, grey };
            var colors = new[] { "#2db039", "#26448f", "#9966ff", "#f9d457", "#a12f31", "#c3c3c3" };

            for (int i = 0; i < arrValues.Length; i++)
            {
                if (arrValues[i] != 0)
                {
                    arrProcent[i] = arrValues[i] / all;
                    arrLength[i] = (int)((length * arrProcent[i]) + 0.5);
                    fixedLength += arrLength[i];
                }
            }

            if (fixedLength > length)
            {
                var res = arrLength.OrderByDescending(x => x).FirstOrDefault();
                arrLength[arrLength.ToList().IndexOf(res)] -= fixedLength - length;
            }

            var bar = new Image<Rgba32>(length, 17);
            for (int i = 0; i < arrValues.Length; i++)
            {
                if (arrValues[i] != 0)
                {
                    using (var thisBar = new Image<Rgba32>(arrLength[i] < 1 ? 1 : arrLength[i], 17))
                    {
                        thisBar.Mutate(x => x.BackgroundColor(GetOrCreateColor(colors[i])));
                        bar.Mutate(x => x.DrawImage(thisBar, new Point(offset, 0), 1));
                        offset += arrLength[i];
                    }
                }
            }

            return bar;
        }

        private Image GetLastRWListCover(Stream imageStream)
        {
            if (imageStream == null) return null;

            var cover = Image.Load(imageStream);
            cover.Mutate(x => x.Resize(new ResizeOptions
            {
                Mode = ResizeMode.Max,
                Size = new Size(20, 50)
            }));

            return cover;
        }

        private async Task<Image<Rgba32>> GetLastRWList(List<ILastReaded> lastRead, List<ILastWatched> lastWatch)
        {
            var titleFont = GetOrCreateFont(_latoBold, 10);
            var nameFont = GetOrCreateFont(_latoBold, 16);
            var fColor = GetOrCreateColor("#9A9A9A");
            int startY = 24;

            var image = new Image<Rgba32>(175, 250);
            image.Mutate(x => x.DrawText($"Ostatnio obejrzane:", nameFont, fColor, new Point(0, 4)));
            if (lastWatch != null)
            {
                int max = -1;
                foreach (var last in lastWatch)
                {
                    if (++max >= 3) break;
                    using (var stream = await GetImageFromUrlAsync(last.AnimeCoverUrl, true))
                    {
                        using (var cover = GetLastRWListCover(stream))
                        {
                            if (cover != null)
                                image.Mutate(x => x.DrawImage(cover, new Point(0, startY + (35 * max)), 1));
                        }
                    }

                    image.Mutate(x => x.DrawText($"{last.AnimeTitle.TrimToLength(29)}", titleFont, fColor, new Point(25, startY + (35 * max))));
                    image.Mutate(x => x.DrawText($"{last.EpisodeNo} / {last.EpisodesCnt}", titleFont, fColor, new Point(25, startY + 11 + (35 * max))));
                }
            }

            startY += 128;
            image.Mutate(x => x.DrawText($"Ostatnio przeczytane:", nameFont, fColor, new Point(0, 131)));
            if (lastRead != null)
            {
                int max = -1;
                foreach (var last in lastRead)
                {
                    if (++max >= 3) break;
                    using (var stream = await GetImageFromUrlAsync(last.MangaCoverUrl, true))
                    {
                        using (var cover = GetLastRWListCover(stream))
                        {
                            if (cover != null)
                                image.Mutate(x => x.DrawImage(cover, new Point(0, startY + (35 * max)), 1));
                        }
                    }

                    image.Mutate(x => x.DrawText($"{last.MangaTitle.TrimToLength(29)}", titleFont, fColor, new Point(25, startY + (35 * max))));
                    image.Mutate(x => x.DrawText($"{last.ChapterNo} / {last.ChaptersCnt}", titleFont, fColor, new Point(25, startY + 11 + (35 * max))));
                }
            }

            return image;
        }

        public async Task<Image<Rgba32>> GetSiteStatisticAsync(IUserInfo shindenInfo, Discord.Color color, List<ILastReaded> lastRead = null, List<ILastWatched> lastWatch = null)
        {
            if (color == Discord.Color.Default)
                color = Discord.Color.DarkerGrey;

            var baseImg = new Image<Rgba32>(500, 320);
            baseImg.Mutate(x => x.BackgroundColor(GetOrCreateColor("#313338")));

            using (var template = Image.Load(Dir.GetResource("siteStatsBody.png")))
            {
                baseImg.Mutate(x => x.DrawImage(template, new Point(0, 0), 1));
            }

            using (var avatar = await GetSiteStatisticUserBadge(shindenInfo.AvatarUrl, shindenInfo.Name, color.RawValue.ToString("X6")))
            {
                baseImg.Mutate(x => x.DrawImage(avatar, new Point(0, 0), 1));
            }

            using (var image = new Image<Rgba32>(325, 248))
            {
                if (shindenInfo?.ListStats?.AnimeStatus != null)
                {
                    using var stats = GetRWStats(shindenInfo?.ListStats?.AnimeStatus, Dir.GetResource("statsAnime.png"), shindenInfo.GetMoreSeriesStats(false));
                    image.Mutate(x => x.DrawImage(stats, new Point(0, 0), 1));
                }
                if (shindenInfo?.ListStats?.MangaStatus != null)
                {
                    using var stats = GetRWStats(shindenInfo?.ListStats?.MangaStatus, Dir.GetResource("statsManga.png"), shindenInfo.GetMoreSeriesStats(true));
                    image.Mutate(x => x.DrawImage(stats, new Point(0, 128), 1));
                }
                baseImg.Mutate(x => x.DrawImage(image, new Point(5, 65), 1));
            }

            using (var image = await GetLastRWList(lastRead, lastWatch))
            {
                baseImg.Mutate(x => x.DrawImage(image, new Point(330, 63), 1));
            }

            return baseImg;
        }

        public async Task<Image<Rgba32>> GetLevelUpBadgeAsync(string name, long ulvl, string avatarUrl, Discord.Color color)
        {
            if (color == Discord.Color.Default)
                color = Discord.Color.DarkerGrey;

            var msgText1 = "POZIOM";
            var msgText2 = "Awansuje na:";

            var textFont = GetOrCreateFont(_latoRegular, 16);
            var nickNameFont = GetOrCreateFont(_latoBold, 22);
            var lvlFont = GetOrCreateFont(_latoBold, 36);

            var msgText1Length = TextMeasurer.MeasureSize(msgText1, new TextOptions(textFont));
            var msgText2Length = TextMeasurer.MeasureSize(msgText2, new TextOptions(textFont));
            var nameLength = TextMeasurer.MeasureSize(name, new TextOptions(nickNameFont));
            var lvlLength = TextMeasurer.MeasureSize($"{ulvl}", new TextOptions(lvlFont));

            var textLength = lvlLength.Width + msgText1Length.Width > nameLength.Width ? lvlLength.Width + msgText1Length.Width : nameLength.Width;
            var estimatedLength = 106 + (int)(textLength > msgText2Length.Width ? textLength : msgText2Length.Width);

            var nickNameColor = color.RawValue.ToString("X6");
            var baseImg = new Image<Rgba32>((int)estimatedLength, 100);

            baseImg.Mutate(x => x.BackgroundColor(GetOrCreateColor("#313338")));
            baseImg.Mutate(x => x.DrawText(msgText1, textFont, Color.Gray, new Point(98 + (int)lvlLength.Width, 75)));
            baseImg.Mutate(x => x.DrawText(name, nickNameFont, GetOrCreateColor(nickNameColor), new Point(98, 5)));
            baseImg.Mutate(x => x.DrawText(msgText2, textFont, Color.Gray, new Point(98, 30)));
            baseImg.Mutate(x => x.DrawText($"{ulvl}", lvlFont, Color.Gray, new Point(96, 55)));

            using (var colorRec = new Image<Rgba32>(82, 82))
            {
                colorRec.Mutate(x => x.BackgroundColor(GetOrCreateColor(nickNameColor)));
                baseImg.Mutate(x => x.DrawImage(colorRec, new Point(9, 9), 1));

                using (var stream = await GetImageFromUrlAsync(avatarUrl))
                {
                    if (stream == null)
                        return baseImg;

                    using (var avatar = Image.Load(stream))
                    {
                        avatar.Mutate(x => x.Resize(new ResizeOptions
                        {
                            Mode = ResizeMode.Crop,
                            Size = new Size(80, 80)
                        }));
                        baseImg.Mutate(x => x.DrawImage(avatar, new Point(10, 10), 1));
                    }
                }
            }

            return baseImg;
        }

        public Image<Rgba32> GetFColorsView(CurrencyType currency)
        {
            var message = GetOrCreateFont(_latoRegular, 16);
            var firstColumnMaxLength = TextMeasurer.MeasureSize("A", new TextOptions(message));
            var secondColumnMaxLength = TextMeasurer.MeasureSize("A", new TextOptions(message));

            var arrayOfColours = Enum.GetValues(typeof(FColor));
            var inFirstColumn = arrayOfColours.Length / 2;

            for (int i = 0; i < arrayOfColours.Length; i++)
            {
                var val = (uint)arrayOfColours.GetValue(i);

                var thisColor = (FColor)val;
                if (thisColor == FColor.None) continue;

                var name = $"{thisColor} ({thisColor.Price(currency)} {currency})";
                var nLen = TextMeasurer.MeasureSize(name, new TextOptions(message));

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
            imgBase.Mutate(x => x.DrawText("Lista:", message, GetOrCreateColor("#000000"), new Point(0, 0)));

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
                imgBase.Mutate(x => x.DrawText(tname, message, GetOrCreateColor(val.ToString("X6")), new Point(posX, posY)));
            }

            return imgBase;
        }

        private async Task<Image> GetCharacterPictureAsync(string characterUrl, bool ultimate)
        {
            var characterImg = ultimate ? new Image<Rgba32>(475, 667) : await Image.LoadAsync(Dir.GetResource("PW/empty.png"));
            using (var image = await GetImageFromUrlOrLocalAsync(characterUrl ?? "http://cdn.shinden.eu/cdn1/other/placeholders/title/225x350.jpg"))
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

        private bool HasDereString(Card card) => card.Quality switch
        {
            Quality.Beta => false,
            Quality.Gamma => false,
            Quality.Epsilon => false,
            Quality.Theta => false,
            _ => true,
        };

        private string GetCustomBorderString(Card card)
        {
            switch (card.Quality)
            {
                case Quality.Epsilon:
                case Quality.Gamma:
                case Quality.Beta:
                case Quality.Theta:
                    return Dir.GetResource($"PW/CG/{card.Quality}/Border/{card.Dere}.png");

                default:
                    return Dir.GetResource($"PW/CG/{card.Quality}/Border.png");
            }
        }

        private Image GenerateBorder(Card card)
        {
            var borderStr = Dir.GetResource($"PW/{card.Rarity}.png");
            var dereStr = Dir.GetResource($"PW/{card.Dere}.png");

            if (card.FromFigure)
            {
                borderStr = GetCustomBorderString(card);
                dereStr = Dir.GetResource($"PW/CG/{card.Quality}/Dere/{card.Dere}.png");
            }

            var img = Image.Load(borderStr);
            if (HasDereString(card))
            {
                using var dere = Image.Load(dereStr);
                img.Mutate(x => x.DrawImage(dere, new Point(0, 0), 1));
            }

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
            var adFont = GetOrCreateFont(_latoBold, 36);
            var hpFont = GetOrCreateFont(_latoBold, 32);

            int hp = card.GetHealthWithPenalty();
            int def = card.GetDefenceWithBonus();
            int atk = card.GetAttackWithBonus();

            using (var hpImg = new Image<Rgba32>(120, 40))
            {
                hpImg.Mutate(x => x.DrawText($"{hp}", hpFont, GetOrCreateColor("#356231"), new Point(1)));
                hpImg.Mutate(x => x.Rotate(-18));

                image.Mutate(x => x.DrawImage(hpImg, new Point(320, 522), 1));
            }

            image.Mutate(x => x.DrawText($"{atk}", adFont, GetOrCreateColor("#522b4d"), new Point(43, 597)));
            image.Mutate(x => x.DrawText($"{def}", adFont, GetOrCreateColor("#00527f"), new Point(337, 597)));
        }

        private void ApplyBetaStats(Image<Rgba32> image, Card card)
        {
            var font = GetOrCreateFont(_latoBold, 29);

            int hp = card.GetHealthWithPenalty();
            int def = card.GetDefenceWithBonus();
            int atk = card.GetAttackWithBonus();

            var ops = new RichTextOptions(font)
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Origin = new Point(320, 476)
            };
            image.Mutate(x => x.DrawText(ops, $"{def}", GetOrCreateColor("#000000")));
            ops.Origin = new Point(320, 520);
            image.Mutate(x => x.DrawText(ops, $"{atk}", GetOrCreateColor("#000000")));
            ops.Origin = new Point(320, 563);
            image.Mutate(x => x.DrawText(ops, $"{hp}", GetOrCreateColor("#000000")));
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
                atkImg.Mutate(x => x.DrawText(ops, $"{atk}", GetOrCreateColor("#c9282c")));
                atkImg.Mutate(x => x.Rotate(22));

                image.Mutate(x => x.DrawImage(atkImg, new Point(40, 514), 1));
            }

            ops.Origin = new Point(238, 563);
            image.Mutate(x => x.DrawText(ops, $"{hp}", GetOrCreateColor("#318b19")));

            using (var defImg = new Image<Rgba32>(120, 40))
            {
                ops.Origin = new Point(60, 1);
                defImg.Mutate(x => x.DrawText(ops, $"{def}", GetOrCreateColor("#00527f")));
                defImg.Mutate(x => x.Rotate(-22));

                image.Mutate(x => x.DrawImage(defImg, new Point(311, 513), 1));
            }
        }

        private void ApplyDeltaStats(Image<Rgba32> image, Card card)
        {
            var hpFont = GetOrCreateFont(_latoBold, 34);
            var adFont = GetOrCreateFont(_latoBold, 26);

            int hp = card.GetHealthWithPenalty();
            int def = card.GetDefenceWithBonus();
            int atk = card.GetAttackWithBonus();

            var drOps = new DrawingOptions() { Transform = Matrix3x2.CreateRotation(-0.46f) };
            var hpOps = new RichTextOptions(hpFont) { HorizontalAlignment = HorizontalAlignment.Center, Origin = new Point(114, 630) };

            var brush = Brushes.Solid(GetOrCreateColor("#356231"));
            var pen = Pens.Solid(GetOrCreateColor("#0000"), 0.1f);

            image.Mutate(x => x.DrawText(drOps, hpOps, $"{hp}", brush, pen));

            var ops = new RichTextOptions(adFont)
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Origin = new Point(92, 597)
            };
            image.Mutate(x => x.DrawText(ops, $"{atk}", GetOrCreateColor("#78261a")));
            ops.Origin = new Point(382, 597);
            image.Mutate(x => x.DrawText(ops, $"{def}", GetOrCreateColor("#00527f")));
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
                atkImg.Mutate(x => x.DrawText(ops, $"{atk}", GetOrCreateColor("#c9282c")));
                atkImg.Mutate(x => x.Rotate(28));

                image.Mutate(x => x.DrawImage(atkImg, new Point(52, 554), 1));
            }

            ops.Origin = new Point(238, 592);
            image.Mutate(x => x.DrawText(ops, $"{hp}", GetOrCreateColor("#318b19")));

            using (var defImg = new Image<Rgba32>(120, 40))
            {
                ops.Origin = new Point(60, 1);
                defImg.Mutate(x => x.DrawText(ops, $"{def}", GetOrCreateColor("#00527f")));
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
            image.Mutate(x => x.DrawText(ops, atk.ToString("D4"), GetOrCreateColor("#da4e00")));
            ops.Origin = new Point(370, 559);
            image.Mutate(x => x.DrawText(ops, def.ToString("D4"), GetOrCreateColor("#00a4ff")));
            ops.Origin = new Point(363, 587);
            image.Mutate(x => x.DrawText(ops, hp.ToString("D5"), GetOrCreateColor("#40ff40")));
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
                atkImg.Mutate(x => x.DrawText($"{atk}", aphFont, GetOrCreateColor(jotaColor), new Point(1)));
                atkImg.Mutate(x => x.Rotate(-10));

                image.Mutate(x => x.DrawImage(atkImg, new Point(106, 545), 1));
            }

            using (var defImg = new Image<Rgba32>(120, 40))
            {
                defImg.Mutate(x => x.DrawText($"{def}", aphFont, GetOrCreateColor(jotaColor), new Point(1)));
                defImg.Mutate(x => x.Rotate(10));

                image.Mutate(x => x.DrawImage(defImg, new Point(310, 557), 1));
            }

            var ops = new RichTextOptions(aphFont) { HorizontalAlignment = HorizontalAlignment.Center, Origin = new Point(238, 585) };
            image.Mutate(x => x.DrawText(ops, $"{hp}", GetOrCreateColor(jotaColor)));
        }

        private void ApplyLambdaStats(Image<Rgba32> image, Card card)
        {
            var aphFont = GetOrCreateFont(_latoBold, 28);

            int hp = card.GetHealthWithPenalty();
            int def = card.GetDefenceWithBonus();
            int atk = card.GetAttackWithBonus();

            using (var hpImg = new Image<Rgba32>(120, 40))
            {
                hpImg.Mutate(x => x.DrawText($"{hp}", aphFont, GetOrCreateColor("#6bedc8"), new Point(1)));
                hpImg.Mutate(x => x.Rotate(-19));

                image.Mutate(x => x.DrawImage(hpImg, new Point(57, 552), 1));
            }

            using (var atkImg = new Image<Rgba32>(120, 40))
            {
                atkImg.Mutate(x => x.DrawText($"{atk}", aphFont, GetOrCreateColor("#fda9fd"), new Point(1)));
                atkImg.Mutate(x => x.Rotate(34));

                image.Mutate(x => x.DrawImage(atkImg, new Point(80, 482), 1));
            }

            image.Mutate(x => x.DrawText($"{def}", aphFont, GetOrCreateColor("#49deff"), new Point(326, 573)));
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
            image.Mutate(x => x.DrawText(ops, $"{atk}", GetOrCreateColor(thetaColor)));
            ops.Origin = new Point(410, 548);
            image.Mutate(x => x.DrawText(ops, $"{def}", GetOrCreateColor(thetaColor)));
            ops.Origin = new Point(410, 585);
            image.Mutate(x => x.DrawText(ops, $"{hp}", GetOrCreateColor(thetaColor)));
        }

        private string GetStatsString(Card card)
        {
            switch (card.Quality)
            {
                case Quality.Beta:
                case Quality.Gamma:
                case Quality.Jota:
                case Quality.Theta:
                    return Dir.GetResource($"PW/CG/{card.Quality}/Stats/{card.Dere}.png");

                default:
                    return Dir.GetResource($"PW/CG/{card.Quality}/Stats.png");
            }
        }

        private string GetBorderBackString(Card card)
        {
            switch (card.Quality)
            {
                case Quality.Jota: return Dir.GetResource($"PW/CG/{card.Quality}/Border/{card.Dere}.png");
                default: return Dir.GetResource($"PW/CG/{card.Quality}/BorderBack.png");
            }
        }

        private void ApplyUltimateStats(Image<Rgba32> image, Card card)
        {
            var statsStr = GetStatsString(card);
            if (File.Exists(statsStr))
            {
                using var stats = Image.Load(statsStr);
                image.Mutate(x => x.DrawImage(stats, new Point(0, 0), 1));
            }

            switch (card.Quality)
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
                    ApplyDeltaStats(image, card);
                    break;
                case Quality.Epsilon:
                    ApplyEpsilonStats(image, card);
                    break;
                case Quality.Zeta:
                    ApplyZetaStats(image, card);
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
            var starCnt = card.GetCardStarCount();

            var starX = 239 - (18 * starCnt);
            for (int i = 0; i < starCnt; i++)
            {
                using (var fire = Image.Load(Dir.GetResource($"PW/stars/{starType}_{card.StarStyle}.png")))
                {
                    image.Mutate(x => x.DrawImage(fire, new Point(starX, 30), 1));
                }

                starX += 36;
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
            image.Mutate(x => x.DrawText($"{health}", numFont, GetOrCreateColor("#000000"), new Point(startXHp, 178)));
            image.Mutate(x => x.DrawText($"{attack}", numFont, GetOrCreateColor("#000000"), new Point(startXAtk, 308)));
            image.Mutate(x => x.DrawText($"{defence}", numFont, GetOrCreateColor("#000000"), new Point(startXDef, 428)));

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
                image.Mutate(x => x.DrawImage(back, new Point(0, 0), 1));
            }
        }

        private async Task<Image<Rgba32>> GetWaifuCardNoStatsAsync(Card card)
        {
            var image = new Image<Rgba32>(475, 667);

            ApplyBorderBack(image, card);

            using (var chara = await GetCharacterPictureAsync(card.GetImage(), card.FromFigure))
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
            string ext = card.IsAnimatedImage ? "gif" : "webp";
            string imageLocation = $"{Dir.CardsInProfiles}/{card.Id}.{ext}";
            if (File.Exists(imageLocation))
                return await Image.LoadAsync(imageLocation);

            return await GetWaifuInProfileCardAsync(card);
        }

        public async Task<Image> GetWaifuInProfileCardAsync(Card card)
        {
            if (card.IsAnimatedImage)
                return await GetAnimatedWaifuCardAsync(card, true);

            var image = new Image<Rgba32>(475, 667);

            ApplyBorderBack(image, card);

            using (var chara = await GetCharacterPictureAsync(card.GetImage(), card.FromFigure))
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
            img.Mutate(x => x.DrawText(options, info.Winner.Name, GetOrCreateColor(image != null ? image.Color : DuelImage.DefaultColor())));
            options.Origin = new Point(Xil, Yt);
            img.Mutate(x => x.DrawText(options, info.Loser.Name, GetOrCreateColor(image != null ? image.Color : DuelImage.DefaultColor())));

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

        public async Task<Image> GetWaifuCardAsync(Card card)
        {
            if (card.IsAnimatedImage)
                return await GetAnimatedWaifuCardAsync(card);

            var image = await GetWaifuCardNoStatsAsync(card);

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

        private async Task<Image> GetAnimatedWaifuCardAsync(Card card, bool noStatsImage = false)
        {
            var characterImg = card.FromFigure ? new Image<Rgba32>(475, 667) : Image.Load(Dir.GetResource("PW/empty.png"));
            using (var cardImg = await GetImageFromUrlOrLocalAsync(card.GetImage() ?? "http://cdn.shinden.eu/cdn1/other/placeholders/title/225x350.jpg"))
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

                    var animation = new Image<Rgba32>(475, 667);
                    var ometa = image.Metadata.GetGifMetadata();
                    var nmeta = animation.Metadata.GetGifMetadata();

                    nmeta.RepeatCount = ometa.RepeatCount;
                    nmeta.ColorTableMode = SixLabors.ImageSharp.Formats.Gif.GifColorTableMode.Local;

                    for (int i = 0; i < image.Frames.Count; i++)
                    {
                        using var oldFrame = image.Frames.CloneFrame(i);
                        using var newFrame = new Image<Rgba32>(475, 667);
                        using var newFrameChar = characterImg.CloneAs<Rgba32>();
                        var oldFrameMetadata = oldFrame.Frames.RootFrame.Metadata.GetGifMetadata();
                        var newFrameMetadata = newFrame.Frames.RootFrame.Metadata.GetGifMetadata();
                        newFrameMetadata.FrameDelay = oldFrameMetadata.FrameDelay;

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