#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Sanakan.Extensions;
using Sanakan.ShindenCompat.Models;
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

namespace Sanakan.Services
{
    public partial class ImageProcessing
    {
        private const int SiteStatsWidth = 1024;
        private const int SiteStatsHeaderHeight = 92;
        private const int SiteStatsPanelY = 104;
        private const int SiteStatsPanelWidth = 490;
        private const int SiteStatsListHeaderY = 268;
        private const int SiteStatsListOffset = 18;
        private const int SiteStatsRowHeight = 60;
        private const int SiteStatsEmptyPanelHeight = 130;
        private const int SiteStatsWideMinHeight = 256;
        private const int SiteStatsListRows = 5;

        private const string SiteStatsBackground = "#131217";
        private const string SiteStatsAccent = "#A77BF3";
        private const string SiteStatsCompleted = "#6FD47E";
        private const string SiteStatsTextBright = "#F2F0F7";
        private const string SiteStatsTextDim = "#C4BFCE";
        private const string SiteStatsLine = "#34303F";
        private const string SiteStatsTrack = "#2A2733";

        // order matches ISeriesStatus: in progress, completed, skipped, on hold, dropped, in plan
        private static readonly string[] SiteStatsStatusColors = { SiteStatsAccent, SiteStatsCompleted, "#8A8A8F", "#F5C445", "#F0525C", "#4FC3E8" };
        private static readonly string[] SiteStatsAnimeLabels = { "Oglądam:", "Obejrzane:", "Pomijam:", "Wstrzymane:", "Porzucone:", "Planuję:" };
        private static readonly string[] SiteStatsMangaLabels = { "Czytam:", "Przeczytane:", "Pomijam:", "Wstrzymane:", "Porzucone:", "Planuję:" };

        private record SiteStatsEntry(ulong TitleId, string Title, string Progress, double? Ratio, string CoverUrl);

        private record SiteStatsPanel(string Title, bool Manga, ISeriesStatus Status, (string Value, string Label)[] Summary,
            string[] StatusLabels, string ListTitle, string EmptyList, List<SiteStatsEntry> Entries, Image[] Covers)
        {
            public bool HasData => (Status?.Total ?? 0) > 0 || Entries.Count > 0;
        }

        private static string FormatSiteStatsNumber(ulong value)
            => value.ToString("#,0", System.Globalization.CultureInfo.InvariantCulture).Replace(',', ' ');

        private static string FormatSiteStatsShortNumber(ulong value)
            => value < 1_000_000 ? FormatSiteStatsNumber(value)
                : $"{(value / 1_000_000.0).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',')} mln";

        private static string FormatSiteStatsRating(double? rating)
            => rating > 0 ? rating.Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',') : "—";

        private static string FormatSiteStatsTime(IDedicatedTime time)
        {
            var minutes = time?.TotalMinutes;
            if (minutes is null) return "—";

            var days = minutes.Value / (24 * 60);
            if (days == 1) return "1 dzień";
            if (days > 1) return $"{FormatSiteStatsNumber(days)} dni";
            return $"{minutes.Value / 60} h";
        }

        // Shinden lists every episode/chapter separately, only the newest entry of each title is shown
        private static List<SiteStatsEntry> GetSiteStatsUniqueEntries(IEnumerable<SiteStatsEntry> entries)
            => entries.GroupBy(x => x.TitleId != 0 ? x.TitleId.ToString() : x.Title).Select(x => x.First()).Take(SiteStatsListRows).ToList();

        private static IPath GetRoundedRectPath(float x, float y, float width, float height, float radius)
        {
            radius = Math.Min(radius, Math.Min(width, height) / 2);
            var points = new List<PointF>();

            void AddCorner(float cx, float cy, float startAngle)
            {
                for (int i = 0; i <= 8; i++)
                {
                    var angle = (startAngle + 90f * i / 8) * MathF.PI / 180f;
                    points.Add(new PointF(cx + radius * MathF.Cos(angle), cy + radius * MathF.Sin(angle)));
                }
            }

            AddCorner(x + width - radius, y + radius, 270);
            AddCorner(x + width - radius, y + height - radius, 0);
            AddCorner(x + radius, y + height - radius, 90);
            AddCorner(x + radius, y + radius, 180);

            return new Polygon(new LinearLineSegment(points.ToArray()));
        }

        private void DrawSiteStatsText(Image image, string text, Font font, string color, float x, float y, HorizontalAlignment alignment = HorizontalAlignment.Left)
        {
            var options = new RichTextOptions(font)
            {
                Origin = new PointF(x, y),
                HorizontalAlignment = alignment,
                VerticalAlignment = VerticalAlignment.Center
            };
            image.Mutate(x => x.Paint(c => c.DrawText(options, text, Brushes.Solid(GetOrCreateColor(color)), null)));
        }

        private string TrimSiteStatsText(string text, Font font, float maxWidth)
        {
            text ??= "";
            if (TextMeasurer.MeasureBounds(text, new TextOptions(font)).Width <= maxWidth)
                return text;

            var length = text.Length;
            while (length > 1 && TextMeasurer.MeasureBounds($"{text.Substring(0, length).TrimEnd()}…", new TextOptions(font)).Width > maxWidth)
                --length;

            return $"{text.Substring(0, length).TrimEnd()}…";
        }

        private async Task<Image> GetSiteStatsImageAsync(string url, Size size, float radius, bool fixExt)
        {
            if (string.IsNullOrEmpty(url))
                return null;

            try
            {
                using var stream = await GetImageFromUrlAsync(url, fixExt);
                if (stream is null || stream == Stream.Null)
                    return null;

                using var loaded = await Image.LoadAsync<Rgba32>(stream);
                var still = loaded.Frames.CloneFrame(0);
                still.Mutate(x =>
                {
                    x.Resize(new ResizeOptions { Mode = ResizeMode.Crop, Size = size });
                    x.Round(radius);
                });
                return still;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void DrawSiteStatsClapperIcon(Image image, float cx, float cy, Color color)
        {
            var pen = Pens.Solid(color, 2f);
            image.Mutate(x => x.Paint(c =>
            {
                c.Draw(pen, GetRoundedRectPath(cx - 13, cy - 4, 26, 14, 2));
                c.Draw(pen, new RectanglePolygon(cx - 13, cy - 10, 26, 6));
                c.DrawLine(pen, new PointF(cx - 8, cy - 4), new PointF(cx - 4, cy - 10));
                c.DrawLine(pen, new PointF(cx - 1, cy - 4), new PointF(cx + 3, cy - 10));
                c.DrawLine(pen, new PointF(cx + 6, cy - 4), new PointF(cx + 10, cy - 10));
                c.DrawLine(pen, new PointF(cx - 7, cy + 4), new PointF(cx + 2, cy + 4));
            }));
        }

        private void DrawSiteStatsBookIcon(Image image, float cx, float cy, Color color)
        {
            var left = new Polygon(new LinearLineSegment(new PointF(cx - 1, cy - 7), new PointF(cx - 13, cy - 10),
                new PointF(cx - 13, cy + 7), new PointF(cx - 1, cy + 10)));
            var right = new Polygon(new LinearLineSegment(new PointF(cx + 1, cy - 7), new PointF(cx + 13, cy - 10),
                new PointF(cx + 13, cy + 7), new PointF(cx + 1, cy + 10)));

            var pen = Pens.Solid(color, 2f);
            image.Mutate(x => x.Paint(c =>
            {
                c.Draw(pen, left);
                c.Draw(pen, right);
            }));
        }

        private void DrawSiteStatsClockIcon(Image image, float cx, float cy, Color color)
        {
            var pen = Pens.Solid(color, 2f);
            image.Mutate(x => x.Paint(c =>
            {
                c.Draw(pen, new EllipsePolygon(cx, cy, 11));
                c.DrawLine(pen, new PointF(cx, cy - 6), new PointF(cx, cy), new PointF(cx + 4, cy + 3));
            }));
        }

        private Image<Rgba32> GetSiteStatsBar(ISeriesStatus status, int width, int height)
        {
            const int gap = 3;
            const int minSegment = 4;

            var bar = new Image<Rgba32>(width, height, GetOrCreateColor(SiteStatsTrack).ToPixel<Rgba32>());
            ulong[] values = { status?.InProgress ?? 0, status?.Completed ?? 0, status?.Skipped ?? 0,
                status?.OnHold ?? 0, status?.Dropped ?? 0, status?.InPlan ?? 0 };

            var total = values.Aggregate(0UL, (a, b) => a + b);
            var used = values.Count(x => x > 0);
            if (total > 0)
            {
                var available = width - gap * (used - 1);
                var lengths = values.Select(x => x == 0 ? 0 : Math.Max(minSegment, (int)Math.Round(available * ((double)x / total)))).ToArray();

                var longest = Array.IndexOf(lengths, lengths.Max());
                lengths[longest] += available - lengths.Sum();

                bar.Mutate(x => x.Paint(c =>
                {
                    c.Clear(Brushes.Solid(Color.Transparent));
                    var offset = 0;
                    for (int i = 0; i < lengths.Length; i++)
                    {
                        if (lengths[i] <= 0) continue;

                        c.Fill(Brushes.Solid(GetOrCreateColor(SiteStatsStatusColors[i])), new RectanglePolygon(offset, 0, lengths[i], height));
                        offset += lengths[i] + gap;
                    }
                }));
            }

            bar.Mutate(x => x.Round(height / 2f));
            return bar;
        }

        private void DrawSiteStatsHeader(Image image, Image avatar, string name, string rank, string color)
        {
            var background = GetOrCreateColor(SiteStatsBackground);
            if (avatar != null)
            {
                using var art = avatar.Clone(x => x
                    .Resize(new ResizeOptions { Mode = ResizeMode.Crop, Size = new Size(SiteStatsWidth, SiteStatsHeaderHeight) })
                    .GaussianBlur(8));
                image.Mutate(x => x.DrawImage(art, new Point(0, 0), 0.6f));
            }

            var fade = new LinearGradientBrush(new PointF(0, 0), new PointF(SiteStatsWidth, 0), GradientRepetitionMode.None,
                new ColorStop(0.2f, background), new ColorStop(1f, background.WithAlpha(0.3f)));

            image.Mutate(x => x.Paint(c =>
            {
                c.Fill(fade, new RectanglePolygon(0, 0, SiteStatsWidth, SiteStatsHeaderHeight));
                c.Fill(Brushes.Solid(GetOrCreateColor("#2A2733")), new RectanglePolygon(0, SiteStatsHeaderHeight - 2, SiteStatsWidth, 2));
                c.Fill(Brushes.Solid(GetOrCreateColor($"#{color}")), GetRoundedRectPath(19, 5, 82, 82, 13));
            }));

            if (avatar != null)
            {
                image.Mutate(x => x.DrawImage(avatar, new Point(22, 8), 1));
            }
            else
            {
                image.Mutate(x => x.Paint(c => c.Fill(Brushes.Solid(GetOrCreateColor(SiteStatsTrack)), GetRoundedRectPath(22, 8, 76, 76, 10))));
            }

            image.Mutate(x => x.Paint(c => c.Fill(Brushes.Solid(GetOrCreateColor(SiteStatsTextBright)), new RectanglePolygon(118, 16, 3, 60))));

            var nameFont = GetFontSize(_latoBold, 42, name, SiteStatsWidth - 520);
            DrawSiteStatsText(image, name, nameFont, SiteStatsTextBright, 134, 33);
            DrawSiteStatsText(image, "Statystyki anime i mangi", GetOrCreateFont(_latoRegular, 22), "#CFCBD8", 135, 69);

            if (string.IsNullOrWhiteSpace(rank))
                return;

            var rankFont = GetOrCreateFont(_latoBold, 22);
            rank = TrimSiteStatsText(rank.Trim(), rankFont, 300);

            var rankWidth = TextMeasurer.MeasureBounds(rank, new TextOptions(rankFont)).Width;
            var pillWidth = rankWidth + 34;
            var pillX = SiteStatsWidth - 24 - pillWidth;
            var pill = GetRoundedRectPath(pillX, 26, pillWidth, 40, 20);

            image.Mutate(x => x.Paint(c =>
            {
                c.Fill(Brushes.Solid(background.WithAlpha(0.85f)), pill);
                c.Draw(Pens.Solid(GetOrCreateColor($"#{color}"), 2), pill);
            }));
            DrawSiteStatsText(image, rank, rankFont, SiteStatsTextBright, pillX + pillWidth / 2, 45, HorizontalAlignment.Center);
        }

        private void DrawSiteStatsProgress(Image image, double ratio, float x, float y, float width)
        {
            var fill = Math.Max(6, (float)Math.Min(1, ratio) * width);
            var color = GetOrCreateColor(ratio >= 1 ? SiteStatsCompleted : SiteStatsAccent);

            image.Mutate(ctx => ctx.Paint(c =>
            {
                c.Fill(Brushes.Solid(GetOrCreateColor(SiteStatsTrack)), GetRoundedRectPath(x, y, width, 6, 3));
                c.Fill(Brushes.Solid(color), GetRoundedRectPath(x, y, fill, 6, 3));
            }));
        }

        private void DrawSiteStatsFrame(Image image, int px, int w, int height, SiteStatsPanel data)
        {
            const int py = SiteStatsPanelY;

            var accent = GetOrCreateColor(SiteStatsAccent);
            var panel = GetRoundedRectPath(px, py, w, height, 14);

            image.Mutate(x => x.Paint(c =>
            {
                c.Fill(Brushes.Solid(GetOrCreateColor("#1B1A21")), panel);
                c.Fill(Brushes.Solid(GetOrCreateColor("#23212B")), GetRoundedRectPath(px + 1, py + 1, w - 2, 40, 13));
                c.Fill(Brushes.Solid(GetOrCreateColor("#23212B")), new RectanglePolygon(px + 1, py + 21, w - 2, 20));
                c.Draw(Pens.Solid(GetOrCreateColor(SiteStatsLine), 2), panel);
            }));

            if (data.Manga) DrawSiteStatsBookIcon(image, px + 36, py + 21, accent);
            else DrawSiteStatsClapperIcon(image, px + 36, py + 21, accent);

            DrawSiteStatsText(image, data.Title, GetOrCreateFont(_latoBold, 22), SiteStatsTextBright, px + 58, py + 19);
        }

        private void DrawSiteStatsSummary(Image image, int px, SiteStatsPanel data)
        {
            const int py = SiteStatsPanelY;
            const int w = SiteStatsPanelWidth;

            var line = GetOrCreateColor(SiteStatsLine);
            var labelFont = GetOrCreateFont(_latoRegular, 19);
            var columnWidth = (w - 20) / (float)data.Summary.Length;
            for (int i = 0; i < data.Summary.Length; i++)
            {
                var center = px + 10 + columnWidth * (i + 0.5f);
                var valueFont = GetFontSize(_latoBold, 28, data.Summary[i].Value, columnWidth - 22);
                DrawSiteStatsText(image, data.Summary[i].Value, valueFont, SiteStatsTextBright, center, py + 70, HorizontalAlignment.Center);
                DrawSiteStatsText(image, data.Summary[i].Label, labelFont, SiteStatsTextDim, center, py + 99, HorizontalAlignment.Center);

                if (i > 0)
                {
                    var divider = px + 10 + columnWidth * i;
                    image.Mutate(x => x.Paint(c => c.DrawLine(Pens.Solid(line, 2), new PointF(divider, py + 56), new PointF(divider, py + 110))));
                }
            }

            using (var bar = GetSiteStatsBar(data.Status, w - 44, 18))
            {
                image.Mutate(x => x.DrawImage(bar, new Point(px + 22, py + 128), 1));
            }

            ulong[] values = { data.Status?.InProgress ?? 0, data.Status?.Completed ?? 0, data.Status?.Skipped ?? 0,
                data.Status?.OnHold ?? 0, data.Status?.Dropped ?? 0, data.Status?.InPlan ?? 0 };

            var legendFont = GetOrCreateFont(_latoRegular, 20);
            var labelWidths = data.StatusLabels.Select(x => TextMeasurer.MeasureBounds(x, new TextOptions(legendFont)).Width).ToArray();
            for (int i = 0; i < values.Length; i++)
            {
                var colX = px + (i < 3 ? 22 : 256);
                var rowY = py + 168 + 30 * (i % 3);
                var valueX = colX + 38 + labelWidths.Skip(i < 3 ? 0 : 3).Take(3).Max();
                var valueEnd = px + (i < 3 ? 244 : w - 14);
                var value = FormatSiteStatsNumber(values[i]);

                image.Mutate(x => x.Paint(c => c.Fill(Brushes.Solid(GetOrCreateColor(SiteStatsStatusColors[i])), new EllipsePolygon(colX + 8, rowY, 8))));
                DrawSiteStatsText(image, data.StatusLabels[i], legendFont, "#D6D2DE", colX + 26, rowY);
                DrawSiteStatsText(image, value, GetFontSize(_latoBold, 20, value, valueEnd - valueX), SiteStatsTextBright, valueX, rowY);
            }
        }

        private void DrawSiteStatsList(Image image, int px, int w, int headerY, int bottom, SiteStatsPanel data)
        {
            DrawSiteStatsClockIcon(image, px + 35, headerY, GetOrCreateColor(SiteStatsAccent));
            DrawSiteStatsText(image, data.ListTitle, GetOrCreateFont(_latoBold, 19), SiteStatsTextBright, px + 54, headerY - 1);

            var listY = headerY + SiteStatsListOffset;
            if (data.Entries.Count == 0)
            {
                DrawSiteStatsText(image, data.EmptyList, GetOrCreateFont(_latoRegular, 20), SiteStatsTextDim, px + w / 2f, listY + (bottom - listY) / 2f, HorizontalAlignment.Center);
                return;
            }

            const int progressWidth = 90;
            var titleFont = GetOrCreateFont(_latoBold, 21);
            var progressFont = GetOrCreateFont(_latoRegular, 19);
            var rowLine = GetOrCreateColor(SiteStatsTrack);
            for (int i = 0; i < data.Entries.Count; i++)
            {
                var entry = data.Entries[i];
                var top = listY + SiteStatsRowHeight * i;

                if (data.Covers[i] != null)
                {
                    image.Mutate(x => x.DrawImage(data.Covers[i], new Point(px + 22, top + 5), 1));
                }
                else
                {
                    image.Mutate(x => x.Paint(c => c.Fill(Brushes.Solid(rowLine), GetRoundedRectPath(px + 22, top + 5, 50, 50, 6))));
                }

                DrawSiteStatsText(image, TrimSiteStatsText(entry.Title, titleFont, w - 108), titleFont, SiteStatsTextBright, px + 86, top + 19);
                DrawSiteStatsText(image, entry.Progress, progressFont, SiteStatsTextDim, px + 86, top + 43);

                if (entry.Ratio.HasValue)
                    DrawSiteStatsProgress(image, entry.Ratio.Value, px + w - 22 - progressWidth, top + 41, progressWidth);

                if (i < data.Entries.Count - 1)
                {
                    image.Mutate(x => x.Paint(c => c.DrawLine(Pens.Solid(rowLine, 1.5f), new PointF(px + 22, top + SiteStatsRowHeight), new PointF(px + w - 22, top + SiteStatsRowHeight))));
                }
            }
        }

        private static int GetSiteStatsListHeight(SiteStatsPanel data)
            => SiteStatsListOffset + SiteStatsRowHeight * Math.Max(1, data.Entries.Count) + 6;

        private void DrawSiteStatsPanel(Image image, int px, int height, SiteStatsPanel data)
        {
            const int py = SiteStatsPanelY;
            const int w = SiteStatsPanelWidth;

            DrawSiteStatsFrame(image, px, w, height, data);
            DrawSiteStatsSummary(image, px, data);

            image.Mutate(x => x.Paint(c => c.DrawLine(Pens.Solid(GetOrCreateColor(SiteStatsLine), 2), new PointF(px + 22, py + 244), new PointF(px + w - 22, py + 244))));
            DrawSiteStatsList(image, px, w, py + SiteStatsListHeaderY, py + height, data);
        }

        // used when the other list is empty: stats on the left, recent titles on the right
        private void DrawSiteStatsWidePanel(Image image, int height, SiteStatsPanel data)
        {
            const int py = SiteStatsPanelY;
            const int px = 16;
            const int w = SiteStatsWidth - 32;
            const int divider = px + SiteStatsPanelWidth;

            DrawSiteStatsFrame(image, px, w, height, data);
            DrawSiteStatsSummary(image, px, data);

            image.Mutate(x => x.Paint(c => c.DrawLine(Pens.Solid(GetOrCreateColor(SiteStatsLine), 2), new PointF(divider, py + 56), new PointF(divider, py + height - 16))));
            DrawSiteStatsList(image, divider, w - SiteStatsPanelWidth, py + 68, py + height, data);
        }

        private void DrawSiteStatsEmpty(Image image, int height)
        {
            const int py = SiteStatsPanelY;
            const int px = 16;
            const int w = SiteStatsWidth - 32;

            var panel = GetRoundedRectPath(px, py, w, height, 14);
            image.Mutate(x => x.Paint(c =>
            {
                c.Fill(Brushes.Solid(GetOrCreateColor("#1B1A21")), panel);
                c.Draw(Pens.Solid(GetOrCreateColor(SiteStatsLine), 2), panel);
            }));

            var accent = GetOrCreateColor(SiteStatsAccent);
            DrawSiteStatsClapperIcon(image, SiteStatsWidth / 2f - 20, py + 32, accent);
            DrawSiteStatsBookIcon(image, SiteStatsWidth / 2f + 20, py + 32, accent);

            DrawSiteStatsText(image, "Nic tu jeszcze nie ma", GetOrCreateFont(_latoBold, 26), SiteStatsTextBright, SiteStatsWidth / 2f, py + 66, HorizontalAlignment.Center);
            DrawSiteStatsText(image, "Na liście nie ma jeszcze żadnego obejrzanego anime ani przeczytanej mangi.",
                GetOrCreateFont(_latoRegular, 20), SiteStatsTextDim, SiteStatsWidth / 2f, py + 98, HorizontalAlignment.Center);
        }

        public async Task<Image<Rgba32>> GetSiteStatisticAsync(IUserInfo shindenInfo, Discord.Color color, List<ILastReaded> lastRead = null, List<ILastWatched> lastWatch = null)
        {
            if (color == Discord.Color.Default)
                color = Discord.Color.DarkerGrey;

            var watched = GetSiteStatsUniqueEntries((lastWatch ?? new List<ILastWatched>()).Select(x => new SiteStatsEntry(x.TitleId, x.AnimeTitle,
                $"{x.EpisodeNo} / {(x.EpisodesCnt > 0 ? x.EpisodesCnt.ToString() : "?")}",
                x.EpisodesCnt > 0 ? (double)x.EpisodeNo / x.EpisodesCnt : null, x.AnimeCoverUrl)));

            var readed = GetSiteStatsUniqueEntries((lastRead ?? new List<ILastReaded>()).Select(x => new SiteStatsEntry(x.TitleId, x.MangaTitle,
                $"{x.ChapterNo.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)} / {(x.ChaptersCnt > 0 ? x.ChaptersCnt.ToString() : "?")}",
                x.ChaptersCnt > 0 ? (double)x.ChapterNo / x.ChaptersCnt : null, x.MangaCoverUrl)));

            var coverSize = new Size(50, 50);
            var avatarTask = GetSiteStatsImageAsync(shindenInfo?.AvatarUrl, new Size(76, 76), 10, false);
            var covers = await Task.WhenAll(watched.Concat(readed).Select(x => GetSiteStatsImageAsync(x.CoverUrl, coverSize, 6, true)));
            using var avatar = await avatarTask;

            try
            {
                var stats = shindenInfo?.ListStats;
                var anime = new SiteStatsPanel("Anime", false, stats?.AnimeStatus, new[]
                {
                    (FormatSiteStatsShortNumber(stats?.AnimeStatus?.Total ?? 0), "Tytuły"),
                    (FormatSiteStatsShortNumber(stats?.EpisodesCount ?? 0), "Odcinki"),
                    (FormatSiteStatsTime(stats?.AnimeTime), "Czas"),
                    (FormatSiteStatsRating(stats?.AnimeMeanScore?.Rating), "Średnia"),
                }, SiteStatsAnimeLabels, "Ostatnio oglądane", "Brak ostatnio oglądanych", watched, covers.Take(watched.Count).ToArray());

                var manga = new SiteStatsPanel("Manga", true, stats?.MangaStatus, new[]
                {
                    (FormatSiteStatsShortNumber(stats?.MangaStatus?.Total ?? 0), "Tytuły"),
                    (FormatSiteStatsShortNumber(stats?.ChaptersCount ?? 0), "Rozdziały"),
                    (FormatSiteStatsTime(stats?.MangaTime), "Czas"),
                    (FormatSiteStatsRating(stats?.MangaMeanScore?.Rating), "Średnia"),
                }, SiteStatsMangaLabels, "Ostatnio czytane", "Brak ostatnio czytanych", readed, covers.Skip(watched.Count).ToArray());

                var withData = new[] { anime, manga }.Where(x => x.HasData).ToList();
                var panelHeight = withData.Count switch
                {
                    0 => SiteStatsEmptyPanelHeight,
                    1 => Math.Max(SiteStatsWideMinHeight, 68 + GetSiteStatsListHeight(withData[0])),
                    _ => SiteStatsListHeaderY + withData.Max(GetSiteStatsListHeight),
                };

                var image = new Image<Rgba32>(SiteStatsWidth, SiteStatsPanelY + panelHeight + 16, GetOrCreateColor(SiteStatsBackground).ToPixel<Rgba32>());
                DrawSiteStatsHeader(image, avatar, shindenInfo?.Name ?? "", shindenInfo?.Rank, color.RawValue.ToString("X6"));

                switch (withData.Count)
                {
                    case 0:
                        DrawSiteStatsEmpty(image, panelHeight);
                        break;

                    case 1:
                        DrawSiteStatsWidePanel(image, panelHeight, withData[0]);
                        break;

                    default:
                        DrawSiteStatsPanel(image, 16, panelHeight, anime);
                        DrawSiteStatsPanel(image, SiteStatsWidth - 16 - SiteStatsPanelWidth, panelHeight, manga);
                        break;
                }
                return image;
            }
            finally
            {
                foreach (var cover in covers)
                    cover?.Dispose();
            }
        }
    }
}
