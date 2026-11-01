#pragma warning disable 1591

using System;
using System.IO;
using System.Threading.Tasks;
using Sanakan.Extensions;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using EndCapStyle = SixLabors.ImageSharp.Drawing.EndCapStyle;
using JointStyle = SixLabors.ImageSharp.Drawing.JointStyle;
using PathBuilder = SixLabors.ImageSharp.Drawing.PathBuilder;
using RectangularPolygon = SixLabors.ImageSharp.Drawing.RectangularPolygon;

namespace Sanakan.Services
{
    public partial class ImageProcessing
    {
        private const int LevelUpHeight = 140;
        private const int LevelUpTextX = 158;
        private const int LevelUpIconWidth = 76;

        private record LevelUpPalette(string Accent, string Bright, string Border, string Number);

        private static readonly LevelUpPalette LevelUpRegular = new("#8B63E6", "#9B78F0", "#8F74D4", "#FFFFFF");
        private static readonly LevelUpPalette LevelUpGold = new("#D9A22B", "#F5C445", "#D9B256", "#F8D978");

        private static LevelUpPalette GetLevelUpPalette(Discord.Color color)
        {
            if (color == Discord.Color.Default)
                return LevelUpGold;

            // very dark role colors would vanish on the dark panel, so they are brightened first
            var luminance = 0.299f * color.R + 0.587f * color.G + 0.114f * color.B;
            var lift = luminance < 120 ? (120 - luminance) / (255 - luminance) : 0;

            string Lighten(float t)
            {
                t = lift + (1 - lift) * t;
                return $"#{(byte)(color.R + (255 - color.R) * t):X2}{(byte)(color.G + (255 - color.G) * t):X2}{(byte)(color.B + (255 - color.B) * t):X2}";
            }
            return new LevelUpPalette(Lighten(0), Lighten(0.25f), Lighten(0.15f), Lighten(0.45f));
        }

        private void DrawLevelUpText(Image image, string text, Font font, string color, float x, float y)
        {
            var options = new RichTextOptions(font) { Origin = new PointF(x, y), VerticalAlignment = VerticalAlignment.Center };
            image.Mutate(c => c.DrawText(options, text, GetOrCreateColor(color)));
        }

        private void DrawLevelUpChevron(Image image, float cx, float cy, Color color)
        {
            var pen = new SolidPen(new PenOptions(color, 10) { JointStyle = JointStyle.Round, EndCapStyle = EndCapStyle.Round });
            image.Mutate(x => x.DrawLine(pen, new PointF(cx - 22, cy + 11), new PointF(cx, cy - 11), new PointF(cx + 22, cy + 11)));
        }

        private void DrawLevelUpBackground(Image image, int width, LevelUpPalette palette)
        {
            var accent = GetOrCreateColor(palette.Accent);
            var panel = GetRoundedRectPath(1, 1, width - 2, LevelUpHeight - 2, 16);

            var glow = new RadialGradientBrush(new PointF(width - 60, LevelUpHeight), 170, GradientRepetitionMode.None,
                new ColorStop(0, accent.WithAlpha(0.22f)), new ColorStop(1, accent.WithAlpha(0)));

            var border = new LinearGradientBrush(new PointF(0, 0), new PointF(width, 0), GradientRepetitionMode.None,
                new ColorStop(0, GetOrCreateColor("#3A3646")), new ColorStop(0.6f, GetOrCreateColor("#4A4060")), new ColorStop(1, GetOrCreateColor(palette.Border)));

            var arcStart = new PointF(width * 0.55f, LevelUpHeight - 6);
            var arcEnd = new PointF(width - 8, LevelUpHeight - 52);
            var arc = new PathBuilder().AddCubicBezier(arcStart, new PointF(width * 0.72f, LevelUpHeight - 24),
                new PointF(width * 0.85f, LevelUpHeight - 30), arcEnd).Build();

            var arcBrush = new LinearGradientBrush(arcStart, arcEnd, GradientRepetitionMode.None,
                new ColorStop(0, accent.WithAlpha(0)), new ColorStop(0.5f, accent.WithAlpha(0.9f)), new ColorStop(1, accent.WithAlpha(0.2f)));

            var arcGlowBrush = new LinearGradientBrush(arcStart, arcEnd, GradientRepetitionMode.None,
                new ColorStop(0, accent.WithAlpha(0)), new ColorStop(0.5f, accent.WithAlpha(0.25f)), new ColorStop(1, accent.WithAlpha(0)));

            image.Mutate(x => x
                .Fill(GetOrCreateColor("#19181F"), panel)
                .Fill(glow, panel)
                .Draw(new SolidPen(arcGlowBrush, 8), arc)
                .Draw(new SolidPen(arcBrush, 2), arc)
                .Draw(new SolidPen(border, 2), panel));
        }

        // highlight is the color of the role granted for this level, the badge keeps its default look without it
        public async Task<Image<Rgba32>> GetLevelUpBadgeAsync(string name, long ulvl, string avatarUrl, Discord.Color color, Discord.Color? highlight = null)
        {
            if (color == Discord.Color.Default)
                color = Discord.Color.DarkerGrey;

            const string promoteText = "Awansuje na:";
            const string levelText = "POZIOM";

            var nameFont = GetOrCreateFont(_latoBold, 26);
            var promoteFont = GetOrCreateFont(_latoRegular, 18);
            var lvlFont = GetOrCreateFont(_latoBold, 46);
            var levelTextFont = GetOrCreateFont(_latoRegular, 22);

            var lvl = $"{ulvl}";
            var lvlWidth = TextMeasurer.MeasureSize(lvl, new TextOptions(lvlFont)).Width;
            var textWidth = Math.Max(TextMeasurer.MeasureSize(name, new TextOptions(nameFont)).Width,
                lvlWidth + 10 + TextMeasurer.MeasureSize(levelText, new TextOptions(levelTextFont)).Width);

            var width = LevelUpTextX + (int)textWidth + 36 + LevelUpIconWidth;
            var baseImg = new Image<Rgba32>(width, LevelUpHeight);
            var palette = highlight.HasValue ? GetLevelUpPalette(highlight.Value) : LevelUpRegular;

            DrawLevelUpBackground(baseImg, width, palette);

            var accent = GetOrCreateColor(palette.Accent);
            baseImg.Mutate(x => x
                .Fill(GetOrCreateColor($"#{color.RawValue:X6}"), GetRoundedRectPath(17, 17, 106, 106, 14))
                .Fill(accent, new RectangularPolygon(139, 20, 2, 100)));

            DrawLevelUpChevron(baseImg, width - 54, 58, GetOrCreateColor(palette.Bright));
            DrawLevelUpChevron(baseImg, width - 54, 86, accent.WithAlpha(0.55f));

            DrawLevelUpText(baseImg, name, nameFont, "#F2F0F7", LevelUpTextX, 36);
            DrawLevelUpText(baseImg, promoteText, promoteFont, "#A09AAE", LevelUpTextX, 67);
            DrawLevelUpText(baseImg, lvl, lvlFont, palette.Number, LevelUpTextX - 2, 101);
            DrawLevelUpText(baseImg, levelText, levelTextFont, "#D6D2DE", LevelUpTextX + lvlWidth + 10, 107);

            using (var stream = await GetImageFromUrlAsync(avatarUrl))
            {
                if (stream is null || stream == Stream.Null)
                {
                    baseImg.Mutate(x => x.Fill(GetOrCreateColor("#2A2733"), GetRoundedRectPath(20, 20, 100, 100, 11)));
                    return baseImg;
                }

                using (var avatar = Image.Load(stream))
                {
                    avatar.Mutate(x =>
                    {
                        x.Resize(new ResizeOptions { Mode = ResizeMode.Crop, Size = new Size(100, 100) });
                        x.Round(11);
                    });

                    if (avatar.Frames.Count > 1)
                    {
                        Animate(baseImg, avatar, new Point(20, 20));
                    }
                    else
                    {
                        baseImg.Mutate(x => x.DrawImage(avatar, new Point(20, 20), 1));
                    }
                }
            }

            return baseImg;
        }
    }
}
