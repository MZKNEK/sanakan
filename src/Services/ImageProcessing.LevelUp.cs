#pragma warning disable 1591

using System;
using System.IO;
using System.Threading.Tasks;
using Sanakan.Extensions;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using EllipsePolygon = SixLabors.ImageSharp.Drawing.EllipsePolygon;
using EndCapStyle = SixLabors.ImageSharp.Drawing.EndCapStyle;
using JointStyle = SixLabors.ImageSharp.Drawing.JointStyle;

namespace Sanakan.Services
{
    public partial class ImageProcessing
    {
        // Discord shows attachments close to their pixel size, so the badge is drawn only slightly above 1:1 -
        // sharp enough on phones without looking oversized on desktop
        private const float LevelUpScale = 1.6f;
        private const float LevelUpHeight = 72;
        private const float LevelUpTextX = 80;
        private const float LevelUpAvatarX = 14;
        private const float LevelUpAvatarY = 10;
        private const float LevelUpAvatarSize = 52;
        private const float LevelUpMarkRadius = 9;
        private const int LevelUpBounceFrames = 14;
        private const uint LevelUpBounceFrameMs = 50;
        private const uint LevelUpBouncePauseMs = 1800;

        private static float LS(float value) => value * LevelUpScale;

        private record LevelUpPalette(string Accent, string Number);

        private static readonly LevelUpPalette LevelUpRegular = new("#8B63E6", "#FFFFFF");
        private static readonly LevelUpPalette LevelUpGold = new("#D9A22B", "#F8D978");

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
            return new LevelUpPalette(Lighten(0), Lighten(0.45f));
        }

        private void DrawLevelUpText(Image image, string text, Font font, string color, float x, float y)
        {
            var options = new RichTextOptions(font) { Origin = new PointF(x, y), VerticalAlignment = VerticalAlignment.Center };
            image.Mutate(c => c.DrawText(options, text, GetOrCreateColor(color)));
        }

        private void DrawLevelUpMark(Image image, LevelUpPalette palette, float lift = 0)
        {
            var cx = LS(LevelUpAvatarX + LevelUpAvatarSize - 8);
            var cy = LS(LevelUpAvatarY + LevelUpAvatarSize - 8) - lift;
            var arm = LS(LevelUpMarkRadius * 0.45f);
            var pen = new SolidPen(new PenOptions(Color.White, LS(LevelUpMarkRadius * 0.2f)) { JointStyle = JointStyle.Round, EndCapStyle = EndCapStyle.Round });

            image.Mutate(x => x
                .Fill(GetOrCreateColor(palette.Accent), new EllipsePolygon(cx, cy, LS(LevelUpMarkRadius)))
                .DrawLine(pen, new PointF(cx - arm, cy + arm / 2), new PointF(cx, cy - arm / 2), new PointF(cx + arm, cy + arm / 2)));
        }

        // a still avatar gets a short loop: the mark hops twice and rests; the first frame is the resting badge
        private Image<Rgba32> AnimateLevelUpMark(Image<Rgba32> card, LevelUpPalette palette)
        {
            var result = card.Clone();
            DrawLevelUpMark(result, palette);
            SetLevelUpFrameDelay(result.Frames.RootFrame, LevelUpBounceFrameMs);

            for (int i = 1; i <= LevelUpBounceFrames; i++)
            {
                var t = i / (float)LevelUpBounceFrames;
                var lift = i == LevelUpBounceFrames ? 0 : LS(4) * MathF.Abs(MathF.Sin(MathF.PI * 2 * t)) * (1 - t);

                using var frame = card.Clone();
                DrawLevelUpMark(frame, palette, lift);
                var added = result.Frames.AddFrame(frame.Frames.RootFrame);
                SetLevelUpFrameDelay(added, i == LevelUpBounceFrames ? LevelUpBouncePauseMs : LevelUpBounceFrameMs);
            }

            result.Metadata.GetWebpMetadata().RepeatCount = 0;
            return result;
        }

        private static void SetLevelUpFrameDelay(ImageFrame frame, uint delayMs)
        {
            var meta = frame.Metadata.GetWebpMetadata();
            meta.DisposalMethod = WebpDisposalMethod.DoNotDispose;
            meta.BlendMethod = WebpBlendMethod.Source;
            meta.FrameDelay = delayMs;
        }

        // highlight is the color of the role granted for this level, the badge keeps its default look without it
        public async Task<Image<Rgba32>> GetLevelUpBadgeAsync(string name, long ulvl, string avatarUrl, Discord.Color color, Discord.Color? highlight = null)
        {
            if (color == Discord.Color.Default)
                color = Discord.Color.DarkerGrey;

            const string promoteText = "Awansuje na:";
            const string levelText = "POZIOM";

            var nameFont = GetOrCreateFont(_latoBold, LS(17));
            var promoteFont = GetOrCreateFont(_latoRegular, LS(12));
            var lvlFont = GetOrCreateFont(_latoBold, LS(26));
            var levelTextFont = GetOrCreateFont(_latoRegular, LS(12));

            var lvl = $"{ulvl}";
            var lvlWidth = TextMeasurer.MeasureSize(lvl, new TextOptions(lvlFont)).Width;
            var textWidth = Math.Max(TextMeasurer.MeasureSize(name, new TextOptions(nameFont)).Width,
                lvlWidth + LS(6) + TextMeasurer.MeasureSize(levelText, new TextOptions(levelTextFont)).Width);
            textWidth = Math.Max(textWidth, TextMeasurer.MeasureSize(promoteText, new TextOptions(promoteFont)).Width);

            var width = (int)(LS(LevelUpTextX + 20) + textWidth);
            var height = (int)LS(LevelUpHeight);
            var baseImg = new Image<Rgba32>(width, height);
            var palette = highlight.HasValue ? GetLevelUpPalette(highlight.Value) : LevelUpRegular;

            var avatarSize = (int)LS(LevelUpAvatarSize);
            var avatarPos = new Point((int)LS(LevelUpAvatarX), (int)LS(LevelUpAvatarY));
            var avatarCenter = new PointF(avatarPos.X + avatarSize / 2f, avatarPos.Y + avatarSize / 2f);
            var ringWidth = LS(2.5f);

            // Discord's embed surface with a side bar, like a native embed
            baseImg.Mutate(x => x
                .Fill(GetOrCreateColor("#2B2D31"), GetRoundedRectPath(0, 0, width, height, LS(6)))
                .Fill(GetOrCreateColor(palette.Accent), GetRoundedRectPath(0, 0, LS(4), height, LS(2)))
                .Draw(new SolidPen(GetOrCreateColor($"#{color.RawValue:X6}"), ringWidth),
                    new EllipsePolygon(avatarCenter, avatarSize / 2f + ringWidth / 2f)));

            DrawLevelUpText(baseImg, name, nameFont, "#F2F0F7", LS(LevelUpTextX), LS(17));
            DrawLevelUpText(baseImg, promoteText, promoteFont, "#A09AAE", LS(LevelUpTextX), LS(34));
            DrawLevelUpText(baseImg, lvl, lvlFont, palette.Number, LS(LevelUpTextX - 1), LS(54));
            DrawLevelUpText(baseImg, levelText, levelTextFont, "#D6D2DE", LS(LevelUpTextX + 6) + lvlWidth, LS(58));

            var animatedAvatar = false;
            using (var stream = await GetImageFromUrlAsync(avatarUrl))
            {
                if (stream is null || stream == Stream.Null)
                {
                    baseImg.Mutate(x => x.Fill(GetOrCreateColor("#3F4147"), new EllipsePolygon(avatarCenter, avatarSize / 2f)));
                }
                else
                {
                    // Rgba32: without an alpha channel the area outside the circle would turn black
                    using (var avatar = Image.Load<Rgba32>(stream))
                    {
                        avatar.Mutate(x =>
                        {
                            x.Resize(new ResizeOptions { Mode = ResizeMode.Crop, Size = new Size(avatarSize, avatarSize) });
                            x.Round(avatarSize / 2f);
                        });

                        if (avatar.Frames.Count > 1)
                        {
                            animatedAvatar = true;
                            Animate(baseImg, avatar, avatarPos);
                        }
                        else
                        {
                            baseImg.Mutate(x => x.DrawImage(avatar, avatarPos, 1));
                        }
                    }
                }
            }

            if (!animatedAvatar)
            {
                using (baseImg)
                {
                    return AnimateLevelUpMark(baseImg, palette);
                }
            }

            // the mark overlaps the avatar, so it is drawn last to stay on top of every frame of an animated one
            DrawLevelUpMark(baseImg, palette);
            return baseImg;
        }
    }
}
