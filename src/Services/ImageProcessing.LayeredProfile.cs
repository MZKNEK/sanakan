#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services.Profiles;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using EllipsePolygon = SixLabors.ImageSharp.Drawing.EllipsePolygon;
using IPath = SixLabors.ImageSharp.Drawing.IPath;
using LinearLineSegment = SixLabors.ImageSharp.Drawing.LinearLineSegment;
using Polygon = SixLabors.ImageSharp.Drawing.Polygon;
using RectanglePolygon = SixLabors.ImageSharp.Drawing.RectanglePolygon;
using PathExtensions = SixLabors.ImageSharp.Drawing.PathExtensions;

namespace Sanakan.Services
{
    // The profile drawn from a layout of layers (Services/Profiles): the header and style pictures in their fields,
    // the nick, the free layers bottom to top (overlays cut to their field), the ultra overlay, the bar and the avatar
    // with its frame on top.
    // At most one part moves: everything under it is drawn once, its frames go over copies of that,
    // and every part above it is drawn on each frame.
    public partial class ImageProcessing
    {
        public const int MaxProfileFrames = 150;

        private const string LpBackground = "#131217";
        private const string LpPanel = "#1B1A21";
        private const string LpLine = "#34303F";
        private const string LpTrack = "#2A2733";
        private const string LpText = "#F2F0F7";
        private const string LpDim = "#C4BFCE";
        private const string LpGold = "#F5C445";

        private static readonly string[] LpStatus = { "#A77BF3", "#6FD47E", "#8A8A8F", "#F5C445", "#F0525C", "#4FC3E8" };
        private static readonly string[] LpAnimeStatus = { "Oglądam", "Obejrzane", "Pomijam", "Wstrzymane", "Porzucone", "Planuję" };
        private static readonly string[] LpMangaStatus = { "Czytam", "Przeczytane", "Pomijam", "Wstrzymane", "Porzucone", "Planuję" };
        private static readonly (string Bg, string Fg)[] LpRarity =
        {
            (null, "#131217"), ("#F06292", "#131217"), ("#F5D547", "#131217"), ("#F0884A", "#131217"),
            ("#9A5BE0", "#FFFFFF"), ("#2B83C9", "#FFFFFF"), ("#3F7E26", "#FFFFFF"), ("#9A9A9A", "#131217"),
        };
        private static readonly Dictionary<string, string> LpCurrency = new Dictionary<string, string>
        {
            [BarLayout.Sc] = "#E8B83A", [BarLayout.Tc] = "#A77BF3", [BarLayout.Ac] = "#6FD47E", [BarLayout.Ct] = "#F0525C", [BarLayout.Pc] = "#4FC3E8",
        };

        // where each border goes for the avatar box at 52, 68 of size 84 (as the old profile put them)
        private static Point FrameAt(AvatarBorder border) => border switch
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
            _ => new Point(24, 39),
        };

        private class LayeredCanvas
        {
            public Image Image;
            public string Moving;
            public bool Moved;

            public bool Moves(string target) => !Moved && Moving == target;
        }

        // the cards of the player's profile gallery, in the order the player set
        public List<Card> ProfileGalleryCards(User user, int count) => GetOrdertedCardsForGallery(user, count).ToList();

        // a list on Shinden as the anime panel shows it, formatted as in the site statistics; null without the list
        public static ProfileListStats ToProfileAnime(ShindenCompat.Models.IListStats stats, IEnumerable<ShindenCompat.Models.ILastWatched> watched)
        {
            var status = stats?.AnimeStatus;
            if (status == null)
                return null;

            return ToProfileList(status, stats.EpisodesCount ?? 0, stats.AnimeTime, stats.AnimeMeanScore?.Rating,
                (watched ?? Enumerable.Empty<ShindenCompat.Models.ILastWatched>()).Select(x => new SiteStatsEntry(x.TitleId, x.AnimeTitle,
                    $"{x.EpisodeNo} / {(x.EpisodesCnt > 0 ? x.EpisodesCnt.ToString() : "?")}", x.EpisodesCnt > 0 ? (double)x.EpisodeNo / x.EpisodesCnt : null, x.AnimeCoverUrl)));
        }

        public static ProfileListStats ToProfileManga(ShindenCompat.Models.IListStats stats, IEnumerable<ShindenCompat.Models.ILastReaded> read)
        {
            var status = stats?.MangaStatus;
            if (status == null)
                return null;

            return ToProfileList(status, stats.ChaptersCount ?? 0, stats.MangaTime, stats.MangaMeanScore?.Rating,
                (read ?? Enumerable.Empty<ShindenCompat.Models.ILastReaded>()).Select(x => new SiteStatsEntry(x.TitleId, x.MangaTitle,
                    $"{x.ChapterNo.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',')} / {(x.ChaptersCnt > 0 ? x.ChaptersCnt.ToString() : "?")}",
                    x.ChaptersCnt > 0 ? (double)x.ChapterNo / x.ChaptersCnt : null, x.MangaCoverUrl)));
        }

        private static ProfileListStats ToProfileList(ShindenCompat.Models.ISeriesStatus status, ulong units, ShindenCompat.Models.IDedicatedTime time, double? rating, IEnumerable<SiteStatsEntry> recent)
            => new ProfileListStats
            {
                Statuses = new[] { status.InProgress ?? 0, status.Completed ?? 0, status.Skipped ?? 0, status.OnHold ?? 0, status.Dropped ?? 0, status.InPlan ?? 0 },
                Total = FormatSiteStatsShortNumber(status.Total ?? 0),
                Units = FormatSiteStatsShortNumber(units),
                Time = FormatSiteStatsTime(time),
                Mean = FormatSiteStatsRating(rating),
                Recent = GetSiteStatsUniqueEntries(recent).Take(ListPanelLayer.MaxRecent)
                    .Select(x => new ProfileRecentTitle { Title = x.Title, Progress = x.Progress, Ratio = x.Ratio, CoverUrl = x.CoverUrl }).ToList(),
            };

        public async Task<Image> GetLayeredProfileAsync(ProfileLayout layout, ProfileRenderData data)
        {
            var canvas = new LayeredCanvas
            {
                Image = new Image<Rgba32>(ProfileLayout.Width, ProfileLayout.Height, GetOrCreateColor(LpBackground).ToPixel<Rgba32>()),
                Moving = layout.Animate,
            };
            var accent = layout.Accent ?? data.RoleColor ?? "#546E7A";

            try
            {
                if (layout.Header == null)
                    await DrawDefaultHeaderAsync(canvas);
                else if (!layout.Header.Hidden)
                    await DrawSlotAsync(canvas, layout.Header, ProfileCatalog.HeaderSlot, data, ProfileLayout.AnimateHeader);

                if (layout.Shade == HeaderShade.Fade)
                    DrawHeaderFade(canvas.Image);
                else if (layout.Shade == HeaderShade.Band)
                    DrawHeaderBand(canvas.Image);

                if (layout.Style != null && !layout.Style.Hidden)
                    await DrawSlotAsync(canvas, layout.Style, ProfileCatalog.StyleSlot, data, ProfileLayout.AnimateStyle);
                else
                    canvas.Image.Mutate(x => x.Paint(c => c.Fill(Brushes.Solid(GetOrCreateColor(LpLine)), new RectanglePolygon(0, 159, 750, 1))));

                if (!layout.Nick.Hidden)
                    DrawNick(canvas.Image, layout.Nick, data.Nick);

                foreach (var layer in layout.Layers.Where(x => !x.Hidden))
                    await DrawLayerAsync(canvas, layout, layer, data, accent);

                if (layout.Ultra != null && !layout.Ultra.Hidden)
                    await DrawImageLayerAsync(canvas, layout.Ultra, data, ProfileLayout.AnimateUltra);

                DrawBar(canvas.Image, layout.Bar, data, accent);
                await DrawAvatarAsync(canvas, layout.Avatar, data, accent);
                return canvas.Image;
            }
            catch
            {
                canvas.Image.Dispose();
                throw;
            }
        }

        // ---- moving parts ----

        private static void LimitFrames(Image image)
        {
            while (image.Frames.Count > MaxProfileFrames)
                image.Frames.RemoveFrame(image.Frames.Count - 1);
        }

        // draws the piece; the first piece that is the moving part and has frames animates the whole canvas
        private void Place(LayeredCanvas canvas, Image piece, Point at, string target, float opacity = 1)
        {
            if (piece.Frames.Count > 1 && canvas.Moves(target))
            {
                LimitFrames(piece);
                if (opacity < 1)
                    piece.Mutate(x => x.Opacity(opacity));
                Animate(canvas.Image, piece, at);
                canvas.Moved = true;
                return;
            }

            if (piece.Frames.Count > 1)
            {
                using var still = piece.Frames.CloneFrame(0);
                canvas.Image.Mutate(x => x.DrawImage(still, at, opacity));
                return;
            }
            canvas.Image.Mutate(x => x.DrawImage(piece, at, opacity));
        }

        // ---- header and style ----

        private async Task DrawDefaultHeaderAsync(LayeredCanvas canvas)
        {
            using var header = await GetImageFromUrlOrLocalAsync(Dir.GetResource("np/pbg.png"));
            if (header.Width <= 1)
                return;

            header.Mutate(x => x.Resize(750, 160));
            canvas.Image.Mutate(x => x.DrawImage(header, new Point(0, 0), 1));
        }

        // as the old profile's template: 75% black from 98 px down, fading out to the right, a darker last row
        private static void DrawHeaderBand(Image image)
        {
            Brush Band(float alpha) => new LinearGradientBrush(new PointF(510, 0), new PointF(726, 0), GradientRepetitionMode.None,
                new ColorStop(0, Color.Black.WithAlpha(alpha)), new ColorStop(1, Color.Black.WithAlpha(0)));
            image.Mutate(x => x.Paint(c => c.Fill(Band(0.75f), new RectanglePolygon(0, 98, 750, 61))));
            image.Mutate(x => x.Paint(c => c.Fill(Band(1f), new RectanglePolygon(0, 159, 750, 1))));
        }

        private void DrawHeaderFade(Image image)
        {
            var bg = GetOrCreateColor(LpBackground);
            var fade = new LinearGradientBrush(new PointF(0, 56), new PointF(0, 160), GradientRepetitionMode.None,
                new ColorStop(0, bg.WithAlpha(0)), new ColorStop(1, bg.WithAlpha(0.92f)));
            image.Mutate(x => x.Paint(c => c.Fill(fade, new RectanglePolygon(0, 56, 750, 104))));
        }

        private async Task DrawSlotAsync(LayeredCanvas canvas, SlotImage slot, (int X, int Y, int W, int H) field, ProfileRenderData data, string target)
        {
            var path = data.ImagePath(slot.Image);
            if (path == null)
                return;

            using var picture = await GetImageFromUrlOrLocalAsync(path);
            if (picture.Width <= 1)
                return;

            var scale = Math.Max(slot.Scale, Math.Max(field.W / (double)picture.Width, field.H / (double)picture.Height));
            var w = Math.Max(field.W, (int)Math.Round(picture.Width * scale));
            var h = Math.Max(field.H, (int)Math.Round(picture.Height * scale));
            var left = Math.Clamp(field.X - slot.X, 0, w - field.W);
            var top = Math.Clamp(field.Y - slot.Y, 0, h - field.H);
            picture.Mutate(x => x.Resize(w, h).Crop(new Rectangle(left, top, field.W, field.H)));
            Place(canvas, picture, new Point(field.X, field.Y), target);
        }

        // ---- free layers ----

        private async Task DrawLayerAsync(LayeredCanvas canvas, ProfileLayout layout, ProfileLayer layer, ProfileRenderData data, string accent)
        {
            var target = ProfileLayout.AnimateLayer(layer.Id);
            switch (layer)
            {
                case ListPanelLayer list:
                    var stats = list.Manga ? data.Manga : data.Anime;
                    if (stats != null)
                        await DrawListPanelAsync(canvas.Image, layout, list, stats, accent);
                    break;

                case CardsPanelLayer cards:
                    await DrawCardsPanelAsync(canvas, layout, cards, data, accent, target);
                    break;

                case GalleryLayer gallery:
                    await DrawGalleryAsync(canvas, layout, gallery, data, target);
                    break;

                case BigGalleryLayer big:
                    await DrawBigGalleryAsync(canvas, layout, big, data, target);
                    break;

                case OverlayLayer overlay:
                    await DrawImageLayerAsync(canvas, overlay, data, target, ProfileCatalog.OverlayField);
                    break;

                case StickerLayer sticker:
                    await DrawStickerAsync(canvas, sticker, data, target);
                    break;

                case RankLayer rank:
                    DrawRank(canvas.Image, rank, data.Rank, accent);
                    break;
            }
        }

        private async Task DrawImageLayerAsync(LayeredCanvas canvas, ImageLayerBase layer, ProfileRenderData data, string target,
            (int X, int Y, int W, int H)? field = null)
        {
            var path = data.ImagePath(layer.Image);
            if (path == null)
                return;

            using var picture = await GetImageFromUrlOrLocalAsync(path);
            if (picture.Width <= 1)
                return;

            var w = Math.Max(1, (int)Math.Round(picture.Width * layer.Scale));
            var h = Math.Max(1, (int)Math.Round(picture.Height * layer.Scale));
            if (w != picture.Width || h != picture.Height)
                picture.Mutate(x => x.Resize(w, h));

            var at = new Point(layer.X, layer.Y);
            if (field is var (fx, fy, fw, fh))
            {
                var part = Rectangle.Intersect(new Rectangle(layer.X, layer.Y, w, h), new Rectangle(fx, fy, fw, fh));
                if (part.Width <= 0 || part.Height <= 0)
                    return;
                if (part.Width != w || part.Height != h)
                    picture.Mutate(x => x.Crop(new Rectangle(part.X - layer.X, part.Y - layer.Y, part.Width, part.Height)));
                at = part.Location;
            }
            Place(canvas, picture, at, target, (float)Math.Clamp(layer.Opacity, 0, 1));
        }

        private void PanelGround(Image image, ProfileLayout layout, int x, int y, int w, int h)
        {
            var panel = GetRoundedRectPath(x, y, w, h, 12);
            switch (layout.Panels)
            {
                case PanelLook.Glass:
                {
                    // the blur of what lies under the panel, from the first frame when the canvas already moves
                    var area = new Rectangle(x, y, w, h);
                    area.Intersect(new Rectangle(0, 0, image.Width, image.Height));
                    if (area.Width <= 0 || area.Height <= 0)
                        return;

                    using var under = image.Frames.CloneFrame(0);
                    under.Mutate(c => c.Crop(area).GaussianBlur(10));
                    using var glass = new Image<Rgba32>(w, h);
                    glass.Mutate(c => c.DrawImage(under, new Point(area.X - x, area.Y - y), 1).Round(12));
                    image.Mutate(c => c.DrawImage(glass, new Point(x, y), 1));
                    image.Mutate(c => c.Paint(p =>
                    {
                        p.Fill(Brushes.Solid(Color.ParseHex("#201E28").WithAlpha(0.55f)), panel);
                        p.Draw(Pens.Solid(Color.White.WithAlpha(0.16f), 1), GetRoundedRectPath(x + 0.5f, y + 0.5f, w - 1, h - 1, 12));
                    }));
                    break;
                }

                case PanelLook.Shadow:
                    image.Mutate(c => c.Paint(p => p.Fill(Brushes.Solid(Color.Black.WithAlpha(Math.Clamp(layout.ShadowOpacity, 0, 100) / 100f)), panel)));
                    break;

                default:
                    image.Mutate(c => c.Paint(p =>
                    {
                        p.Fill(Brushes.Solid(GetOrCreateColor(LpPanel)), panel);
                        p.Draw(Pens.Solid(GetOrCreateColor(LpLine), 1.5f), panel);
                    }));
                    break;
            }
        }

        // the blur of what lies under the shape (from the first frame when the canvas already moves), tinted, with a thin edge
        private static void DrawGlass(Image image, IPath shape, float tint)
        {
            var b = shape.Bounds;
            var area = new Rectangle((int)Math.Floor(b.X), (int)Math.Floor(b.Y), (int)Math.Ceiling(b.Width) + 2, (int)Math.Ceiling(b.Height) + 2);
            area.Intersect(new Rectangle(0, 0, image.Width, image.Height));
            if (area.Width <= 0 || area.Height <= 0)
                return;

            using var under = image.Frames.CloneFrame(0);
            under.Mutate(c => c.Crop(area).GaussianBlur(10));
            using var glass = under.CloneAs<Rgba32>();
            using var mask = new Image<Rgba32>(area.Width, area.Height);
            var local = PathExtensions.Translate(shape, -area.X, -area.Y);
            mask.Mutate(c => c.Paint(p => p.Fill(Brushes.Solid(Color.White), local)));
            glass.ProcessPixelRows(mask, (g, m) =>
            {
                for (int y = 0; y < g.Height; y++)
                {
                    var gr = g.GetRowSpan(y);
                    var mr = m.GetRowSpan(y);
                    for (int x = 0; x < gr.Length; x++)
                        gr[x].A = (byte)(gr[x].A * mr[x].A / 255);
                }
            });
            image.Mutate(c => c.DrawImage(glass, area.Location, 1));
            image.Mutate(c => c.Paint(p =>
            {
                p.Fill(Brushes.Solid(Color.ParseHex("#201E28").WithAlpha(tint)), shape);
                p.Draw(Pens.Solid(Color.White.WithAlpha(0.16f), 1), shape);
            }));
        }

        private float TextWidth(string text, Font font) => string.IsNullOrEmpty(text) ? 0 : TextMeasurer.MeasureAdvance(text, new TextOptions(font)).Width;

        private float LpWrite(Image image, string text, Font font, string color, float x, float y, HorizontalAlignment align = HorizontalAlignment.Left)
        {
            if (!string.IsNullOrEmpty(text))
                DrawSiteStatsText(image, text, font, color, x, y, align);
            return TextWidth(text, font);
        }

        // rounded to a tenth as Profilator does it; a thousand K is shown as 1M
        internal static string LpShort(long n)
        {
            var pl = System.Globalization.CultureInfo.InvariantCulture;
            var thousands = Math.Round(n / 100d, MidpointRounding.AwayFromZero) / 10;
            if (n >= 1_000_000 || thousands >= 1000)
                return (Math.Round(n / 100_000d, MidpointRounding.AwayFromZero) / 10).ToString("0.#", pl).Replace('.', ',') + "M";
            if (n >= 10_000) return thousands.ToString("0.#", pl).Replace('.', ',') + "K";
            return FormatSiteStatsNumber((ulong)Math.Max(0, n));
        }

        // ---- icons, drawn small in the accent ----

        private void IconClapper(Image image, float cx, float cy, Color color)
        {
            var pen = Pens.Solid(color, 1.6f);
            image.Mutate(x => x.Paint(c =>
            {
                c.Draw(pen, GetRoundedRectPath(cx - 9, cy - 2, 18, 10, 2));
                c.Draw(pen, new RectanglePolygon(cx - 9, cy - 7, 18, 4));
                c.DrawLine(pen, new PointF(cx - 6, cy - 2), new PointF(cx - 3, cy - 7));
                c.DrawLine(pen, new PointF(cx - 1, cy - 2), new PointF(cx + 2, cy - 7));
                c.DrawLine(pen, new PointF(cx + 4, cy - 2), new PointF(cx + 7, cy - 7));
            }));
        }

        private void IconBook(Image image, float cx, float cy, Color color)
        {
            var pen = Pens.Solid(color, 1.6f);
            var left = new Polygon(new LinearLineSegment(new PointF(cx - 1, cy - 5), new PointF(cx - 9, cy - 7), new PointF(cx - 9, cy + 5), new PointF(cx - 1, cy + 7)));
            var right = new Polygon(new LinearLineSegment(new PointF(cx + 1, cy - 5), new PointF(cx + 9, cy - 7), new PointF(cx + 9, cy + 5), new PointF(cx + 1, cy + 7)));
            image.Mutate(x => x.Paint(c => { c.Draw(pen, left); c.Draw(pen, right); }));
        }

        private void IconCards(Image image, float cx, float cy, Color color)
        {
            var pen = Pens.Solid(color, 1.6f);
            IPath back = GetRoundedRectPath(-9, -8, 11, 15, 2);
            IPath front = GetRoundedRectPath(-2, -8, 11, 15, 2);
            back = SixLabors.ImageSharp.Drawing.PathExtensions.Translate(SixLabors.ImageSharp.Drawing.PathExtensions.RotateDegree(back, -11.5f), cx, cy);
            front = SixLabors.ImageSharp.Drawing.PathExtensions.Translate(SixLabors.ImageSharp.Drawing.PathExtensions.RotateDegree(front, 11.5f), cx, cy);
            image.Mutate(x => x.Paint(c => { c.Draw(pen, back); c.Draw(pen, front); }));
        }

        private void IconClock(Image image, float cx, float cy, Color color)
        {
            var pen = Pens.Solid(color, 1.5f);
            image.Mutate(x => x.Paint(c =>
            {
                c.Draw(pen, new EllipsePolygon(cx, cy, 6.5f));
                c.DrawLine(pen, new PointF(cx, cy - 3.5f), new PointF(cx, cy), new PointF(cx + 2.5f, cy + 2));
            }));
        }

        private void IconCrown(Image image, float cx, float cy, Color color)
        {
            var crown = new Polygon(new LinearLineSegment(new PointF(cx - 7, cy + 5), new PointF(cx - 7, cy - 4), new PointF(cx - 3, cy),
                new PointF(cx, cy - 6), new PointF(cx + 3, cy), new PointF(cx + 7, cy - 4), new PointF(cx + 7, cy + 5)));
            image.Mutate(x => x.Paint(c => c.Fill(Brushes.Solid(color), crown)));
        }

        private void IconEnvelope(Image image, float x0, float cy, Color color)
        {
            var pen = Pens.Solid(color, 1.5f);
            image.Mutate(x => x.Paint(c =>
            {
                c.Draw(pen, GetRoundedRectPath(x0, cy - 6, 18, 12, 2));
                c.DrawLine(pen, new PointF(x0 + 1, cy - 5), new PointF(x0 + 9, cy + 1), new PointF(x0 + 17, cy - 5));
            }));
        }

        // ---- anime and manga ----

        private void LpSegments(Image image, ulong[] values, float x, float y, int w, int h)
        {
            using var bar = new Image<Rgba32>(w, h, GetOrCreateColor(LpTrack).ToPixel<Rgba32>());
            var total = values.Aggregate(0UL, (a, b) => a + b);
            if (total > 0)
            {
                const int gap = 2;
                var used = values.Count(v => v > 0);
                var available = w - gap * (used - 1);
                var lengths = values.Select(v => v == 0 ? 0 : Math.Max(3, (int)Math.Round(available * (double)v / total))).ToArray();
                var longest = Array.IndexOf(lengths, lengths.Max());
                lengths[longest] += available - lengths.Sum();
                bar.Mutate(b => b.Paint(c =>
                {
                    c.Clear(Brushes.Solid(Color.Transparent));
                    var offset = 0;
                    for (int i = 0; i < lengths.Length; i++)
                    {
                        if (lengths[i] <= 0) continue;
                        c.Fill(Brushes.Solid(GetOrCreateColor(LpStatus[i])), new RectanglePolygon(offset, 0, lengths[i], h));
                        offset += lengths[i] + gap;
                    }
                }));
            }
            bar.Mutate(b => b.Round(h / 2f));
            image.Mutate(c => c.DrawImage(bar, new Point((int)x, (int)y), 1));
        }

        private void LpDot(Image image, float cx, float cy, string color)
            => image.Mutate(x => x.Paint(c => c.Fill(Brushes.Solid(GetOrCreateColor(color)), new EllipsePolygon(cx, cy, 4))));

        private void LpLegend(Image image, ProfileListStats stats, string[] labels, float x, float y, int w, LegendMode mode)
        {
            var bold13 = GetOrCreateFont(_latoBold, 13);
            if (mode == LegendMode.All)
            {
                for (int i = 0; i < 6; i++)
                {
                    var lx = x + 14 + i * ((w - 28) / 6f);
                    LpDot(image, lx + 4, y, LpStatus[i]);
                    LpWrite(image, FormatSiteStatsNumber(stats.Statuses[i]), bold13, LpText, lx + 12, y);
                }
                return;
            }

            var regular12 = GetOrCreateFont(_latoRegular, 12);
            var main = new[] { 1, 0, 5 };
            for (int i = 0; i < main.Length; i++)
            {
                var s = main[i];
                var lx = x + 14 + (w - 28) / 3f * i;
                LpDot(image, lx + 4, y, LpStatus[s]);
                var tw = LpWrite(image, labels[s], regular12, LpDim, lx + 12, y);
                LpWrite(image, FormatSiteStatsNumber(stats.Statuses[s]), bold13, LpText, lx + 16 + tw, y);
            }
        }

        private async Task DrawListPanelAsync(Image image, ProfileLayout layout, ListPanelLayer list, ProfileListStats stats, string accent)
        {
            const int w = ProfileCatalog.ListWidth;
            var large = list.Size == ListPanelSize.Large;
            var h = large ? ProfileCatalog.LargeListHeight : ProfileCatalog.SmallListHeight;
            int x = list.X, y = list.Y;
            var labels = list.Manga ? LpMangaStatus : LpAnimeStatus;
            var accentColor = GetOrCreateColor(accent);

            PanelGround(image, layout, x, y, w, h);
            if (list.Manga) IconBook(image, x + 22, y + 19, accentColor);
            else IconClapper(image, x + 22, y + 19, accentColor);
            LpWrite(image, list.Manga ? "Manga" : "Anime", GetOrCreateFont(_latoBold, 16), LpText, x + 38, y + 19);

            var meanInRow = large && list.Shows(ListPanelLayer.Numbers);
            if (list.Shows(ListPanelLayer.Mean) && !meanInRow)
            {
                var mw = LpWrite(image, stats.Mean, GetOrCreateFont(_latoBold, 15), LpText, x + w - 14, y + 19, HorizontalAlignment.Right);
                LpWrite(image, "średnia", GetOrCreateFont(_latoRegular, 12), LpDim, x + w - 20 - mw, y + 20, HorizontalAlignment.Right);
            }

            float cy = y + 38;
            if (list.Shows(ListPanelLayer.Numbers))
            {
                var columns = new List<(string Label, string Value)> { ("Tytuły", stats.Total), (list.Manga ? "Rozdziały" : "Odcinki", stats.Units), ("Czas", stats.Time) };
                if (meanInRow && list.Shows(ListPanelLayer.Mean))
                    columns.Add(("Średnia", stats.Mean));

                var columnWidth = (w - 28) / (float)columns.Count;
                for (int i = 0; i < columns.Count; i++)
                {
                    var center = x + 14 + columnWidth * (i + 0.5f);
                    var valueFont = GetFontSize(_latoBold, columns.Count > 3 ? 18 : 20, columns[i].Value, columnWidth - 8);
                    LpWrite(image, columns[i].Value, valueFont, LpText, center, cy + 14, HorizontalAlignment.Center);
                    LpWrite(image, columns[i].Label, GetOrCreateFont(_latoRegular, 12), LpDim, center, cy + 33, HorizontalAlignment.Center);
                    if (i > 0)
                    {
                        var divider = (int)(x + 14 + columnWidth * i);
                        image.Mutate(c => c.Paint(p => p.Fill(Brushes.Solid(GetOrCreateColor(LpLine)), new RectanglePolygon(divider, cy + 4, 1.5f, 38))));
                    }
                }
                cy += 49;
            }

            if (list.Shows(ListPanelLayer.StatusBar))
            {
                LpSegments(image, stats.Statuses, x + 14, cy, w - 28, 8);
                cy += 16;
            }

            var recent = (stats.Recent ?? new List<ProfileRecentTitle>()).Take(large ? list.Recent : Math.Min(1, list.Recent)).ToList();
            if (!large)
            {
                if (recent.Count > 0)
                {
                    var r = recent[0];
                    var progress = r.Progress ?? "";
                    var pw = TextWidth(progress, GetOrCreateFont(_latoRegular, 12));
                    IconClock(image, x + 20, cy + 8, accentColor);
                    var titleFont = GetOrCreateFont(_latoBold, 13);
                    LpWrite(image, TrimSiteStatsText(r.Title, titleFont, w - 58 - pw), titleFont, LpText, x + 32, cy + 8);
                    LpWrite(image, progress, GetOrCreateFont(_latoRegular, 12), LpDim, x + w - 14, cy + 8, HorizontalAlignment.Right);
                }
                else if (list.Shows(ListPanelLayer.LegendRow))
                {
                    LpLegend(image, stats, labels, x, cy + 8, w, list.Legend);
                }
                return;
            }

            if (list.Shows(ListPanelLayer.LegendRow))
            {
                if (list.Legend == LegendMode.All)
                {
                    var regular12 = GetOrCreateFont(_latoRegular, 12);
                    var bold12 = GetOrCreateFont(_latoBold, 12);
                    for (int i = 0; i < 6; i++)
                    {
                        var lx = x + 14 + (i % 3) * ((w - 28) / 3f);
                        var ly = cy + 8 + (i / 3) * 20;
                        LpDot(image, lx + 4, ly, LpStatus[i]);
                        var tw = LpWrite(image, labels[i], regular12, LpDim, lx + 12, ly);
                        LpWrite(image, FormatSiteStatsNumber(stats.Statuses[i]), bold12, LpText, lx + 15 + tw, ly);
                    }
                    cy += 44;
                }
                else
                {
                    LpLegend(image, stats, labels, x, cy + 8, w, LegendMode.Main);
                    cy += 22;
                }
            }

            if (recent.Count == 0)
                return;

            image.Mutate(c => c.Paint(p => p.Fill(Brushes.Solid(GetOrCreateColor(LpLine)), new RectanglePolygon(x + 14, cy + 1, w - 28, 1.5f))));
            IconClock(image, x + 21, cy + 16, accentColor);
            LpWrite(image, list.Manga ? "Ostatnio czytane" : "Ostatnio oglądane", GetOrCreateFont(_latoBold, 13), LpText, x + 33, cy + 16);

            var rowY = cy + 29;
            var fits = Math.Max(0, (int)((y + h - 6 - rowY) / 30));
            var rows = recent.Take(fits).ToList();
            var covers = await Task.WhenAll(rows.Select(r => GetSiteStatsImageAsync(r.CoverUrl, new Size(26, 26), 5, true)));
            try
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    var r = rows[i];
                    var ry = rowY + i * 30;
                    if (covers[i] != null)
                        image.Mutate(c => c.DrawImage(covers[i], new Point(x + 14, (int)ry), 1));
                    else
                        image.Mutate(c => c.Paint(p => p.Fill(Brushes.Solid(GetOrCreateColor(LpTrack)), GetRoundedRectPath(x + 14, ry, 26, 26, 5))));

                    var titleFont = GetOrCreateFont(_latoBold, 13);
                    LpWrite(image, TrimSiteStatsText(r.Title, titleFont, w - 128), titleFont, LpText, x + 48, ry + 8);
                    LpWrite(image, r.Progress ?? "", GetOrCreateFont(_latoRegular, 11), LpDim, x + 48, ry + 21);
                    if (r.Ratio.HasValue)
                    {
                        var ratio = Math.Clamp(r.Ratio.Value, 0, 1);
                        var color = GetOrCreateColor(ratio >= 1 ? LpStatus[1] : LpStatus[0]);
                        image.Mutate(c => c.Paint(p =>
                        {
                            p.Fill(Brushes.Solid(GetOrCreateColor(LpTrack)), GetRoundedRectPath(x + w - 74, ry + 18, 60, 5, 2.5f));
                            p.Fill(Brushes.Solid(color), GetRoundedRectPath(x + w - 74, ry + 18, Math.Max(5, 60 * (float)ratio), 5, 2.5f));
                        }));
                    }
                }
            }
            finally
            {
                foreach (var cover in covers)
                    cover?.Dispose();
            }
        }

        // ---- cards ----

        private async Task<Image> LoadProfileCardAsync(Card card, int height)
        {
            var image = await LoadOrGetNewWaifuProfileCardAsync(card);
            image.Mutate(x => x.Resize(new ResizeOptions { Mode = ResizeMode.Max, Size = new Size(0, height) }));
            return image;
        }

        private void LpChip(Image image, Rarity rarity, float x, float y, float opacity)
        {
            const int w = 34, h = 18;
            using var chip = new Image<Rgba32>(w, h);
            var (bg, fg) = LpRarity[(int)rarity];
            Brush brush = bg == null
                ? new LinearGradientBrush(new PointF(0, 0), new PointF(w, 0), GradientRepetitionMode.None,
                    new ColorStop(0, Color.ParseHex("#F0525C")), new ColorStop(0.25f, Color.ParseHex("#F5C445")), new ColorStop(0.5f, Color.ParseHex("#6FD47E")),
                    new ColorStop(0.75f, Color.ParseHex("#4FC3E8")), new ColorStop(1, Color.ParseHex("#A77BF3")))
                : Brushes.Solid(GetOrCreateColor(bg));
            chip.Mutate(c => c.Paint(p => p.Fill(brush, GetRoundedRectPath(0, 0, w, h, 5))));
            LpWrite(chip, rarity.ToString(), GetOrCreateFont(_latoBold, 11), fg, w / 2f, h / 2f + 0.5f, HorizontalAlignment.Center);
            image.Mutate(c => c.DrawImage(chip, new Point((int)x, (int)y), opacity));
        }

        private async Task DrawCardsPanelAsync(LayeredCanvas canvas, ProfileLayout layout, CardsPanelLayer panel, ProfileRenderData data, string accent, string target)
        {
            const int w = ProfileCatalog.CardsPanelWidth;
            int x = panel.X, y = panel.Y;
            var image = canvas.Image;

            PanelGround(image, layout, x, y, w, ProfileCatalog.PanelHeight);
            IconCards(image, x + 22, y + 19, GetOrCreateColor(accent));
            LpWrite(image, "Karcianka", GetOrCreateFont(_latoBold, 16), LpText, x + 38, y + 19);

            if (panel.Shows(CardsPanelLayer.Karma) && data.Karma != KarmaLook.None)
            {
                var (name, color) = data.Karma switch
                {
                    KarmaLook.Angel => ("anioł", LpGold),
                    KarmaLook.Demon => ("demon", "#F0525C"),
                    _ => ("neutralna", "#8A8A8F"),
                };
                var font = GetOrCreateFont(_latoBold, 12);
                var text = $"Karma: {name}";
                var kw = TextWidth(text, font) + 18;
                var pill = GetRoundedRectPath(x + w - 14 - kw, y + 9, kw, 20, 10);
                image.Mutate(c => c.Paint(p =>
                {
                    p.Fill(Brushes.Solid(GetOrCreateColor(color).WithAlpha(0.18f)), pill);
                    p.Draw(Pens.Solid(GetOrCreateColor(color), 1.2f), pill);
                }));
                LpWrite(image, text, font, LpText, x + w - 14 - kw / 2, y + 19.5f, HorizontalAlignment.Center);
            }

            float rx = x + 14;
            if (panel.Shows(CardsPanelLayer.Waifu))
            {
                var cw = ProfileCatalog.StickerWidth(226);
                if (data.Waifu != null)
                {
                    using var card = await LoadProfileCardAsync(data.Waifu, 226);
                    Place(canvas, card, new Point(x + 12, y + 40), target);
                    image = canvas.Image;
                }
                rx = x + 12 + cw + 14;
            }

            var rw = x + w - 14 - rx;
            float cy = y + 40;
            if (panel.Shows(CardsPanelLayer.Count))
            {
                LpWrite(image, "Karty", GetOrCreateFont(_latoRegular, 12), LpDim, rx, cy + 7);
                var vw = LpWrite(image, FormatSiteStatsNumber((ulong)Math.Max(0, data.Cards)), GetOrCreateFont(_latoBold, 20), LpText, rx, cy + 27);
                LpWrite(image, $" / {FormatSiteStatsNumber((ulong)Math.Max(0, data.Limit))}", GetOrCreateFont(_latoRegular, 14), LpDim, rx + vw, cy + 28);
                var ratio = data.Limit > 0 ? Math.Clamp(data.Cards / (double)data.Limit, 0, 1) : 0;
                image.Mutate(c => c.Paint(p =>
                {
                    p.Fill(Brushes.Solid(GetOrCreateColor(LpTrack)), GetRoundedRectPath(rx, cy + 41, rw, 6, 3));
                    p.Fill(Brushes.Solid(GetOrCreateColor(accent)), GetRoundedRectPath(rx, cy + 41, Math.Max(6, rw * (float)ratio), 6, 3));
                }));
                cy += 60;
            }

            if (panel.Shows(CardsPanelLayer.Rarities))
            {
                var columns = panel.Shows(CardsPanelLayer.Waifu) ? 2 : 4;
                var rarities = Enum.GetValues<Rarity>();
                for (int i = 0; i < rarities.Length; i++)
                {
                    var gx = rx + (i % columns) * (rw / columns);
                    var gy = cy + (i / columns) * 25;
                    var count = data.Rarities.TryGetValue(rarities[i], out var n) ? n : 0;
                    LpChip(image, rarities[i], gx, gy, count > 0 ? 1 : 0.35f);
                    LpWrite(image, FormatSiteStatsNumber((ulong)count), GetOrCreateFont(_latoBold, 14), count > 0 ? LpText : LpDim, gx + 42, gy + 9.5f);
                }
                cy += (int)Math.Ceiling(rarities.Length / (double)columns) * 25 + 4;
            }

            var extras = new List<(string Label, int Value)>();
            if (panel.Shows(CardsPanelLayer.Ultimates)) extras.Add(("Ultimate", data.Ultimates));
            if (panel.Shows(CardsPanelLayer.CustomImages)) extras.Add(("Własne obrazki", data.CustomImages));
            if (panel.Shows(CardsPanelLayer.CustomBorders)) extras.Add(("Własne ramki", data.CustomBorders));
            if (extras.Count == 0)
                return;

            if (cy > y + 40)
            {
                var lineY = cy;
                image.Mutate(c => c.Paint(p => p.Fill(Brushes.Solid(GetOrCreateColor(LpLine)), new RectanglePolygon(rx, lineY, rw, 1.5f))));
                cy += 17;
            }
            else
            {
                cy += 7;
            }

            for (int i = 0; i < extras.Count; i++)
            {
                LpWrite(image, extras[i].Label, GetOrCreateFont(_latoRegular, 12), LpDim, rx, cy + i * 20);
                LpWrite(image, FormatSiteStatsNumber((ulong)Math.Max(0, extras[i].Value)), GetOrCreateFont(_latoBold, 13), extras[i].Value > 0 ? LpText : LpDim, rx + rw, cy + i * 20, HorizontalAlignment.Right);
            }
        }

        private async Task DrawGalleryAsync(LayeredCanvas canvas, ProfileLayout layout, GalleryLayer gallery, ProfileRenderData data, string target)
        {
            const int w = ProfileCatalog.ListWidth, h = ProfileCatalog.PanelHeight;
            int x = gallery.X, y = gallery.Y;
            if (gallery.Panel)
                PanelGround(canvas.Image, layout, x, y, w, h);

            var two = gallery.Count == 2;
            var cards = data.Gallery.Take(two ? 2 : 6).ToList();
            var cardHeight = two ? 214 : 124;
            var cardWidth = ProfileCatalog.StickerWidth(cardHeight);
            for (int i = 0; i < cards.Count; i++)
            {
                Point at;
                if (two)
                {
                    const int gap = 8;
                    var left = x + (w - 2 * cardWidth - gap) / 2;
                    at = new Point(left + i * (cardWidth + gap), y + (h - cardHeight) / 2);
                }
                else
                {
                    var gap = (w - 24 - 3 * cardWidth) / 2;
                    at = new Point(x + 12 + (i % 3) * (cardWidth + gap), y + 12 + (i / 3) * (cardHeight + 4));
                }

                if (cards[i] == null)
                    continue;
                using var card = await LoadProfileCardAsync(cards[i], cardHeight);
                Place(canvas, card, at, target);
            }
        }

        private async Task DrawBigGalleryAsync(LayeredCanvas canvas, ProfileLayout layout, BigGalleryLayer big, ProfileRenderData data, string target)
        {
            int x = big.X, y = big.Y;
            if (big.Panel)
                PanelGround(canvas.Image, layout, x, y, ProfileCatalog.BigGalleryWidth, ProfileCatalog.BigGalleryHeight);

            const int cardHeight = 140;
            var cardWidth = ProfileCatalog.StickerWidth(cardHeight);
            var gap = (ProfileCatalog.BigGalleryWidth - 24 - 6 * cardWidth) / 5;
            var cards = data.Gallery.Take(12).ToList();
            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i] == null)
                    continue;
                using var card = await LoadProfileCardAsync(cards[i], cardHeight);
                Place(canvas, card, new Point(x + 12 + (i % 6) * (cardWidth + gap), y + 12 + (i / 6) * (cardHeight + 4)), target);
            }
        }

        private async Task DrawStickerAsync(LayeredCanvas canvas, StickerLayer sticker, ProfileRenderData data, string target)
        {
            var card = data.Card(sticker.Card);
            if (card == null)
                return;

            using var picture = await LoadProfileCardAsync(card, sticker.H);
            int w = picture.Width, h = picture.Height;
            const int margin = 14;
            var center = new PointF(sticker.X + w / 2f, sticker.Y + h / 2f);

            // the panel or the glass lies on the profile itself, tilted with the card
            if (sticker.Ground is StickerGround.Panel or StickerGround.Glass)
            {
                var pad = Math.Clamp(sticker.Pad, 0, StickerLayer.MaxPad);
                var ground = GetRoundedRectPath(center.X - w / 2f - pad, center.Y - h / 2f - pad, w + 2 * pad, h + 2 * pad, 10);
                if (sticker.Tilt != 0)
                    ground = ground.Transform(new System.Numerics.Matrix4x4(System.Numerics.Matrix3x2.CreateRotation(sticker.Tilt * MathF.PI / 180f, center)));
                if (sticker.Ground == StickerGround.Glass)
                    DrawGlass(canvas.Image, ground, sticker.GroundAlpha);
                else
                    canvas.Image.Mutate(c => c.Paint(p =>
                    {
                        p.Fill(Brushes.Solid(GetOrCreateColor(LpBackground).WithAlpha(sticker.GroundAlpha)), ground);
                        p.Draw(Pens.Solid(Color.White.WithAlpha(0.16f), 1), ground);
                    }));
            }

            // the card on its own canvas with room for its shadow, then tilted as a whole
            using var piece = new Image<Rgba32>(w + 2 * margin, h + 2 * margin);
            if (sticker.Ground == StickerGround.Shadow)
            {
                using var shadow = new Image<Rgba32>(w + 2 * margin, h + 2 * margin);
                shadow.Mutate(c => c.Paint(p => p.Fill(Brushes.Solid(Color.Black.WithAlpha(sticker.GroundAlpha)), GetRoundedRectPath(margin, margin + 3, w, h, 8))).GaussianBlur(6));
                piece.Mutate(c => c.DrawImage(shadow, new Point(0, 0), 1));
            }

            Image moving = piece;
            Image framed = null;
            try
            {
                if (picture.Frames.Count > 1 && canvas.Moves(target))
                {
                    LimitFrames(picture);
                    framed = piece.Clone(c => { });
                    Animate(framed, picture, new Point(margin, margin));
                    moving = framed;
                }
                else
                {
                    using var still = picture.Frames.CloneFrame(0);
                    piece.Mutate(c => c.DrawImage(still, new Point(margin, margin), 1));
                }

                if (sticker.Tilt != 0)
                    moving.Mutate(c => c.Rotate(sticker.Tilt));

                var at = new Point((int)Math.Round(center.X - moving.Width / 2f), (int)Math.Round(center.Y - moving.Height / 2f));
                Place(canvas, moving, at, target);
            }
            finally
            {
                framed?.Dispose();
            }
        }

        // ---- rank, nick, bar ----

        private void DrawRank(Image image, RankLayer rank, string text, string accent)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            var font = GetOrCreateFont(_latoBold, 13);
            text = TrimSiteStatsText(text.Trim(), font, 300);
            var w = TextWidth(text, font) + 22;
            var x = rank.Align == NickAlign.Center ? rank.X - w / 2 : rank.X;
            var pill = GetRoundedRectPath(x, rank.Y, w, ProfileCatalog.RankHeight, 11);
            image.Mutate(c => c.Paint(p =>
            {
                p.Fill(Brushes.Solid(GetOrCreateColor(LpBackground).WithAlpha(0.72f)), pill);
                p.Draw(Pens.Solid(GetOrCreateColor(accent), 1.5f), pill);
            }));
            LpWrite(image, text, font, LpText, x + w / 2, rank.Y + 11.5f, HorizontalAlignment.Center);
        }

        private void DrawNick(Image image, NickLayer nick, string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            var font = GetFontSize(_latoBold, 28, text, nick.W);
            var align = nick.Align == NickAlign.Center ? HorizontalAlignment.Center : HorizontalAlignment.Left;
            var x = nick.Align == NickAlign.Center ? nick.X + nick.W / 2f : nick.X;
            var cy = nick.Y + NickLayer.Height / 2f;

            // a soft shadow under the letters, so the nick reads on any background
            const int pad = 12;
            using var shadow = new Image<Rgba32>(nick.W + 2 * pad, NickLayer.Height + 2 * pad);
            var sx = nick.Align == NickAlign.Center ? shadow.Width / 2f : pad;
            LpWrite(shadow, text, font, "#000000", sx, shadow.Height / 2f + 1, align);
            shadow.Mutate(c => c.GaussianBlur(3));
            image.Mutate(c => c.DrawImage(shadow, new Point(nick.X - pad, nick.Y - pad), 0.8f));

            LpWrite(image, text, font, LpText, x, cy, align);
        }

        // the gap between the currencies (14 down to 8) and the experience bar (90 down to 36) that let both sides fit
        internal static (float Gap, float Track) BarSpacing(float left, float currencies, int gaps, bool exp)
        {
            float gap = 14, track = exp ? 90 : 0;
            var overflow = left + track + currencies + gap * gaps - 726;
            if (overflow > 0 && gaps > 0)
            {
                var less = Math.Min(overflow, (gap - 8) * gaps);
                gap -= less / gaps;
                overflow -= less;
            }
            if (overflow > 0 && track > 0)
                track = Math.Max(36, track - overflow);
            return (gap, track);
        }

        private void DrawBar(Image image, BarLayout bar, ProfileRenderData data, string accent)
        {
            if (bar.At == BarPlace.Hidden)
                return;

            var currencies = new List<(string Key, long Value)>
            {
                (BarLayout.Messages, (long)Math.Min(data.Messages, long.MaxValue)), (BarLayout.Sc, data.Sc), (BarLayout.Tc, data.Tc),
                (BarLayout.Ac, data.Ac), (BarLayout.Ct, data.Ct), (BarLayout.Pc, data.Pc),
            }.Where(x => bar.Shows(x.Key)).ToList();

            if (!bar.Shows(BarLayout.Top) && !bar.Shows(BarLayout.Level) && !bar.Shows(BarLayout.Exp) && currencies.Count == 0)
                return;

            var y0 = bar.At == BarPlace.Top ? 4 : 468;
            float cy = y0 + 13;
            var strip = GetRoundedRectPath(12, y0, 726, 26, 13);
            if (bar.Look == BarLook.Glass)
                DrawGlass(image, strip, Math.Clamp(bar.Opacity, 20, 100) / 100f);
            else
                image.Mutate(c => c.Paint(p =>
                {
                    p.Fill(Brushes.Solid(GetOrCreateColor(LpBackground).WithAlpha(Math.Clamp(bar.Opacity, 20, 100) / 100f)), strip);
                    p.Draw(Pens.Solid(Color.White.WithAlpha(0.08f), 1), strip);
                }));

            var bold15 = GetOrCreateFont(_latoBold, 15);
            var regular12 = GetOrCreateFont(_latoRegular, 12);
            var top = $"#{(data.Top > 9999 ? ">10K" : FormatSiteStatsNumber((ulong)Math.Max(0, data.Top)))}";
            var progress = (float)Math.Clamp(data.LevelProgress, 0, 1);
            var percent = $"{(int)Math.Floor(progress * 100)}%";
            var widths = currencies.Select(item => (item.Key == BarLayout.Messages ? 18 : 24) + 5 + TextWidth(LpShort(item.Value), bold15)).ToList();

            // large numbers: the currencies close up first, then the experience bar gets shorter
            var left = 24 + 12f
                + (bar.Shows(BarLayout.Top) ? 18 + TextWidth(top, bold15) + 14 : 0)
                + (bar.Shows(BarLayout.Level) ? TextWidth("Poz.", regular12) + 5 + TextWidth(LpShort(data.Level), bold15) + 8 : 0)
                + (bar.Shows(BarLayout.Exp) ? 6 + TextWidth(percent, regular12) : 0);
            var gaps = Math.Max(0, currencies.Count - 1);
            var (gap, track) = BarSpacing(left, widths.Sum(), gaps, bar.Shows(BarLayout.Exp));

            float x = 24;
            if (bar.Shows(BarLayout.Top))
            {
                IconCrown(image, x + 7, cy, GetOrCreateColor(LpGold));
                x += 18;
                x += LpWrite(image, top, bold15, LpText, x, cy) + 14;
            }
            if (bar.Shows(BarLayout.Level))
            {
                x += LpWrite(image, "Poz.", regular12, LpDim, x, cy) + 5;
                x += LpWrite(image, LpShort(data.Level), bold15, LpText, x, cy) + 8;
            }
            if (bar.Shows(BarLayout.Exp))
            {
                var bx = x;
                image.Mutate(c => c.Paint(p =>
                {
                    p.Fill(Brushes.Solid(GetOrCreateColor(LpTrack)), GetRoundedRectPath(bx, cy - 4, track, 8, 4));
                    p.Fill(Brushes.Solid(GetOrCreateColor(accent)), GetRoundedRectPath(bx, cy - 4, Math.Max(8, track * progress), 8, 4));
                }));
                x += track + 6;
                LpWrite(image, percent, regular12, LpDim, x, cy);
            }

            var tagFont = GetOrCreateFont(_latoBold, 10);
            var ix = 726 - widths.Sum() - gap * gaps;
            for (int i = 0; i < currencies.Count; i++)
            {
                var (key, value) = currencies[i];
                if (key == BarLayout.Messages)
                {
                    IconEnvelope(image, ix, cy, GetOrCreateColor(LpDim));
                }
                else
                {
                    var tx = ix;
                    image.Mutate(c => c.Paint(p => p.Fill(Brushes.Solid(GetOrCreateColor(LpCurrency[key])), GetRoundedRectPath(tx, cy - 8, 24, 16, 4))));
                    LpWrite(image, key.ToUpperInvariant(), tagFont, "#131217", ix + 12, cy + 0.5f, HorizontalAlignment.Center);
                }
                LpWrite(image, LpShort(value), bold15, LpText, ix + (key == BarLayout.Messages ? 18 : 24) + 5, cy);
                ix += widths[i] + gap;
            }
        }

        // ---- avatar ----

        private async Task DrawAvatarAsync(LayeredCanvas canvas, AvatarLayer avatar, ProfileRenderData data, string accent)
        {
            var s = avatar.Size;
            var shape = avatar.HasFrame ? AvatarShape.Circle : avatar.Shape;
            float radius = shape switch { AvatarShape.Circle => s / 2f, AvatarShape.Rounded => s * 0.21f, _ => 5 };
            var inset = (int)Math.Round(3 * s / 84.0);

            using (var shadow = new Image<Rgba32>(s + 24, s + 24))
            {
                shadow.Mutate(c => c.Paint(p => p.Fill(Brushes.Solid(Color.Black.WithAlpha(0.45f)), GetRoundedRectPath(12, 12, s, s, radius))).GaussianBlur(5));
                canvas.Image.Mutate(c => c.DrawImage(shadow, new Point(avatar.X - 12, avatar.Y - 12), 1));
            }
            canvas.Image.Mutate(c => c.Paint(p => p.Fill(Brushes.Solid(GetOrCreateColor(accent)), GetRoundedRectPath(avatar.X, avatar.Y, s, s, radius))));

            if (!string.IsNullOrEmpty(data.AvatarUrl))
            {
                using var picture = await GetAvatarImageOrPlaceholderAsync(data.AvatarUrl);
                if (picture.Width > 1)
                {
                    var inner = s - 2 * inset;
                    picture.Mutate(c => c.Resize(new ResizeOptions { Mode = ResizeMode.Crop, Size = new Size(inner, inner) }).Round(Math.Max(2, radius - inset)));
                    Place(canvas, picture, new Point(avatar.X + inset, avatar.Y + inset), ProfileLayout.AnimateAvatar);
                }
            }

            var k = s / 84.0;
            if (avatar.HasCustomFrame)
            {
                var path = data.ImagePath(avatar.CustomImage);
                if (path == null)
                    return;

                using var frame = await GetImageFromUrlOrLocalAsync(path);
                if (frame.Width <= 1)
                    return;

                var fw = Math.Max(1, (int)Math.Round(s * 1.45 * avatar.CustomScale));
                var fh = Math.Max(1, (int)Math.Round(fw * frame.Height / (double)frame.Width));
                frame.Mutate(c => c.Resize(fw, fh));
                var at = new Point((int)Math.Round(avatar.X + s / 2.0 - fw / 2.0 + avatar.CustomX * k), (int)Math.Round(avatar.Y + s / 2.0 - fh / 2.0 + avatar.CustomY * k));
                Place(canvas, frame, at, ProfileLayout.AnimateFrame);
                return;
            }

            if (!ProfileCatalog.TryParseFrame(avatar.Frame, out var border) || border == AvatarBorder.None)
                return;

            var file = Dir.GetResource($"np/ab/{border}.png");
            if (avatar.LevelFrame)
            {
                var level = GetAvailableLevelBorder(Dir.GetResource($"np/ab/{border}Lv/"), data.Level);
                if (!string.IsNullOrEmpty(level))
                    file = level;
            }
            if (!System.IO.File.Exists(file))
                return;

            using var borderImage = Image.Load(file);
            if (Math.Abs(k - 1) > 0.001)
                borderImage.Mutate(c => c.Resize((int)Math.Round(borderImage.Width * k), (int)Math.Round(borderImage.Height * k)));
            var spot = FrameAt(border);
            canvas.Image.Mutate(c => c.DrawImage(borderImage, new Point((int)Math.Round(avatar.X + (spot.X - 52) * k), (int)Math.Round(avatar.Y + (spot.Y - 68) * k)), 1));
        }
    }
}
