using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Sanakan.Database.Models;
using Sanakan.Services;
using Sanakan.Services.PocketWaifu;
using Sanakan.Services.Profiles;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Xunit;

namespace Artifacts
{
    public class LayeredProfileRenderTests : IDisposable
    {
        // the bot reads as local only the files under its own folders
        private readonly string _dir = $"{Dir.SavedData}/test-layered-{Guid.NewGuid():N}";
        private readonly ImageProcessing _img = new ImageProcessing(null, new TagIcon(7, "galeria", "x"), true);

        public LayeredProfileRenderTests() => Directory.CreateDirectory(_dir);

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        private string Picture(string name, int w, int h, int frames = 1)
        {
            var path = $"{_dir}/{name}";
            using var image = new Image<Rgba32>(w, h, new Rgba32(200, 80, 120, 180));
            for (int i = 1; i < frames; i++)
            {
                using var frame = new Image<Rgba32>(w, h, new Rgba32((byte)(40 * i), 160, 90, 200));
                image.Frames.AddFrame(frame.Frames.RootFrame);
            }
            if (frames > 1) image.SaveAsGif(path);
            else image.SaveAsPng(path);
            return path;
        }

        private static Card NewCard(ulong id, Rarity rarity) => new Card
        {
            Id = id, Name = "Karta", Title = "Tytuł", Character = 1000 + id, Rarity = rarity, RarityOnStart = rarity,
            Dere = Dere.Tsundere, Attack = 50, Defence = 20, Health = 300, Image = null, CreationDate = new DateTime(2026, 1, 1),
            Tags = new List<Tag>(), Relation = new List<TagCardRelation>(),
        };

        private ProfileRenderData Data(Dictionary<long, string> images)
        {
            var cards = Enumerable.Range(1, 12).Select(i => NewCard((ulong)i, (Rarity)(i % 8))).ToList();
            var list = new ProfileListStats
            {
                Statuses = new ulong[] { 12, 340, 3, 9, 7, 120 }, Total = "491", Units = "9 876", Time = "105 dni", Mean = "7,42",
                Recent = new List<ProfileRecentTitle> { new ProfileRecentTitle { Title = "Kroniki Pustego Nieba", Progress = "5 / 12", Ratio = 5 / 12d } },
            };
            return new ProfileRenderData
            {
                Nick = "Bardzo długi nick gracza Żółć", Rank = "Ranga testowa", RoleColor = "#9B59B6",
                Top = 17, Level = 37, LevelProgress = 0.62, Sc = 98765, Tc = 4321,
                Cards = 14, Limit = 1000, Karma = KarmaLook.Neutral, Rarities = new Dictionary<Rarity, int> { [Rarity.A] = 7, [Rarity.SSS] = 1 },
                Waifu = cards[0], Gallery = cards, Card = id => cards.FirstOrDefault(x => x.Id == id),
                Anime = list, Manga = list,
                ImagePath = id => images.TryGetValue(id, out var p) ? p : null,
            };
        }

        [Fact]
        public async Task AnOverlayCoversTheNickButStaysInItsField()
        {
            var path = $"{_dir}/solid.png";
            var green = new Rgba32(10, 200, 30, 255);
            using (var solid = new Image<Rgba32>(750, 500, green))
                solid.SaveAsPng(path);
            var data = Data(new Dictionary<long, string> { [1] = path });

            var layout = ProfileCatalog.FindPreset("minimal").Make();
            layout.Layers.Add(new OverlayLayer { Id = 50, Image = 1, X = 0, Y = 0 });
            using (var image = (await _img.GetLayeredProfileAsync(layout, data)).CloneAs<Rgba32>())
            {
                Assert.NotEqual(green, image[400, 60]);
                Assert.Equal(green, image[300, 116]);
                Assert.Equal(green, image[400, 300]);
            }

            layout.Layers.Clear();
            layout.Ultra = new UltraLayer { Id = 50, Image = 1, X = 0, Y = 0 };
            using (var image = (await _img.GetLayeredProfileAsync(layout, data)).CloneAs<Rgba32>())
                Assert.Equal(green, image[400, 60]);
        }

        // the brightest pixel of the minimal layout's nick box
        private static int NickBrightness(Image<Rgba32> image, ProfileLayout layout)
        {
            var n = layout.Nick;
            var max = 0;
            for (int y = n.Y; y < n.Y + NickLayer.Height; y++)
                for (int x = n.X; x < n.X + n.W; x++)
                    max = Math.Max(max, Math.Min(image[x, y].R, Math.Min(image[x, y].G, image[x, y].B)));
            return max;
        }

        private static bool NickHas(Image<Rgba32> image, ProfileLayout layout, Func<Rgba32, bool> pixel, int fromX = 0, int toX = 750)
        {
            var n = layout.Nick;
            for (int y = n.Y; y < n.Y + NickLayer.Height; y++)
                for (int x = Math.Max(n.X, fromX); x < Math.Min(n.X + n.W, toX); x++)
                    if (pixel(image[x, y]))
                        return true;
            return false;
        }

        [Fact]
        public async Task TheNickLiesUnderOrOverTheLayersAsItsPlaceSays()
        {
            var layout = ProfileCatalog.FindPreset("minimal").Make();
            var n = layout.Nick;
            layout.Layers.Insert(0, new TileLayer { Id = 40, X = n.X - 10, Y = n.Y - 6, W = n.W + 20, H = NickLayer.Height + 12 });
            var data = Data(new Dictionary<long, string>());

            layout.Nick.At = 0;
            using (var under = (await _img.GetLayeredProfileAsync(layout, data)).CloneAs<Rgba32>())
                Assert.True(NickBrightness(under, layout) < 60);

            layout.Nick.At = 1;
            using (var over = (await _img.GetLayeredProfileAsync(layout, data)).CloneAs<Rgba32>())
                Assert.True(NickBrightness(over, layout) > 200);
        }

        [Fact]
        public async Task EveryNickFontDrawsItsOwnLetters()
        {
            var layout = ProfileCatalog.FindPreset("minimal").Make();
            var data = Data(new Dictionary<long, string>());
            data.Nick = "Żółć Gęślą";
            byte[] Pixels(Image<Rgba32> image)
            {
                var bytes = new byte[layout.Nick.W * NickLayer.Height * 4];
                image.Clone(c => c.Crop(new Rectangle(layout.Nick.X, layout.Nick.Y, layout.Nick.W, NickLayer.Height))).CopyPixelDataTo(bytes);
                return bytes;
            }

            byte[] lato;
            using (var image = (await _img.GetLayeredProfileAsync(layout, data)).CloneAs<Rgba32>())
                lato = Pixels(image);

            foreach (var font in ProfileCatalog.NickFonts.Where(x => x.Key != ProfileCatalog.DefaultNickFont))
            {
                layout.Nick.Font = font.Key;
                using var image = (await _img.GetLayeredProfileAsync(layout, data)).CloneAs<Rgba32>();
                Assert.True(NickBrightness(image, layout) > 200, font.Key);
                Assert.False(lato.SequenceEqual(Pixels(image)), font.Key + " is drawn in Lato");
            }
        }

        [Fact]
        public async Task EveryNickColourDraws()
        {
            var layout = ProfileCatalog.FindPreset("minimal").Make();
            layout.Accent = "#00c853";
            var data = Data(new Dictionary<long, string>());
            data.Nick = "MMMMMMMMMMMMMMMM";
            bool Red(Rgba32 p) => p.R > 200 && p.G < 60 && p.B < 60;
            bool Blue(Rgba32 p) => p.B > 200 && p.R < 60 && p.G < 60;

            layout.Nick.Paint = NickPaint.Solid;
            layout.Nick.Color1 = "#ff0000";
            using (var image = (await _img.GetLayeredProfileAsync(layout, data)).CloneAs<Rgba32>())
                Assert.True(NickHas(image, layout, Red));

            layout.Nick.Paint = NickPaint.Accent;
            using (var image = (await _img.GetLayeredProfileAsync(layout, data)).CloneAs<Rgba32>())
                Assert.True(NickHas(image, layout, p => p.G > 150 && p.R < 40 && p.B < 120));

            layout.Nick.Paint = NickPaint.Gradient;
            layout.Nick.Color2 = "#0000ff";
            using (var image = (await _img.GetLayeredProfileAsync(layout, data)).CloneAs<Rgba32>())
            {
                Assert.True(NickHas(image, layout, Red, toX: layout.Nick.X + 40));
                Assert.False(NickHas(image, layout, Blue, toX: layout.Nick.X + 40));
                Assert.True(NickHas(image, layout, Blue, fromX: layout.Nick.X + 200));
            }

            layout.Nick.Paint = NickPaint.Outline;
            using (var image = (await _img.GetLayeredProfileAsync(layout, data)).CloneAs<Rgba32>())
                Assert.True(NickHas(image, layout, Red));
        }

        [Theory]
        [InlineData(TileLook.Panel, null)]
        [InlineData(TileLook.Glass, 80)]
        [InlineData(TileLook.Shadow, 100)]
        public async Task EveryTileDrawsWhereItIsPlaced(TileLook look, int? opacity)
        {
            var path = $"{_dir}/white.png";
            using (var white = new Image<Rgba32>(750, 340, new Rgba32(255, 255, 255, 255)))
                white.SaveAsPng(path);
            var layout = ProfileCatalog.FindPreset("image").Make();
            layout.Style = new SlotImage { Image = 1, X = 0, Y = 160 };
            layout.Layers.Add(new TileLayer { Id = 40, X = 100, Y = 200, W = 300, H = 150, Look = look, Opacity = opacity });

            using var image = (await _img.GetLayeredProfileAsync(layout, Data(new Dictionary<long, string> { [1] = path }))).CloneAs<Rgba32>();

            Assert.True(image[250, 275].R < 200, look.ToString());
            Assert.Equal(255, image[600, 400].R);
        }

        [Fact]
        public async Task TheSecondUltraOverlayLiesOverTheFirst()
        {
            string Solid(string name, Rgba32 color)
            {
                var path = $"{_dir}/{name}.png";
                using var image = new Image<Rgba32>(750, 500, color);
                image.SaveAsPng(path);
                return path;
            }
            var green = new Rgba32(10, 200, 30, 255);
            var red = new Rgba32(220, 20, 20, 255);
            var data = Data(new Dictionary<long, string> { [1] = Solid("green", green), [2] = Solid("red", red) });
            var layout = ProfileCatalog.FindPreset("minimal").Make();
            layout.Ultra = new UltraLayer { Image = 1 };
            layout.Ultra2 = new UltraLayer { Image = 2, X = 375 };

            using var image = (await _img.GetLayeredProfileAsync(layout, data)).CloneAs<Rgba32>();

            Assert.Equal(green, image[200, 300]);
            Assert.Equal(red, image[500, 300]);
        }

        [Fact]
        public void LegendItemsKeepTheirColumnsUntilTheyDoNotFit()
        {
            Assert.Equal(new[] { 30f, 131f, 232f }, ImageProcessing.LegendSpread(new[] { 80f, 60f, 70f }, 16, 331));

            var widths = new[] { 120f, 75f, 90f };
            var at = ImageProcessing.LegendSpread(widths, 16, 331);
            Assert.Equal(30f, at[0]);
            Assert.True(at[1] >= at[0] + widths[0] && at[2] >= at[1] + widths[1]);
            Assert.Equal(16 + 331 - 14, at[2] + widths[2], 3);
        }

        [Theory]
        [InlineData(9999, "9 999")]
        [InlineData(237960, "238K")]
        [InlineData(237940, "237,9K")]
        [InlineData(999960, "1M")]
        [InlineData(3650000, "3,7M")]
        [InlineData(100000000, "100M")]
        public void BarNumbersAreRoundedToATenth(long value, string expected)
        {
            Assert.Equal(expected, ImageProcessing.LpShort(value));
        }

        [Theory]
        [InlineData(200f, 300f, 14f, 90f)]
        [InlineData(250f, 330f, 11.2f, 90f)]
        [InlineData(250f, 370f, 8f, 66f)]
        [InlineData(300f, 500f, 8f, 36f)]
        public void LargeNumbersCloseUpTheBar(float left, float currencies, float gap, float track)
        {
            Assert.Equal((gap, track), ImageProcessing.BarSpacing(left, currencies, 5, true));
        }

        [Theory]
        [InlineData(StickerGround.Panel, 0)]
        [InlineData(StickerGround.Glass, 0)]
        [InlineData(StickerGround.Glass, 9)]
        [InlineData(StickerGround.Shadow, -7)]
        [InlineData(StickerGround.None, 5)]
        [InlineData(StickerGround.Glass, 90)]
        [InlineData(StickerGround.Shadow, -90)]
        [InlineData(StickerGround.Panel, 45)]
        public async Task EveryGroundUnderACardDraws(StickerGround ground, int tilt)
        {
            var layout = ProfileCatalog.FindPreset("minimal").Make();
            layout.Bar.Look = BarLook.Glass;
            layout.Layers.Add(new StickerLayer { Id = 30, Card = 2, X = 300, Y = 200, H = 200, Tilt = tilt, Ground = ground, Pad = 24, GroundOpacity = 70 });

            using var image = await _img.GetLayeredProfileAsync(layout, Data(new Dictionary<long, string>()));

            Assert.Equal((750, 500), (image.Width, image.Height));
        }

        [Theory]
        [InlineData(HeaderShade.Band, 120, true)]
        [InlineData(HeaderShade.Fade, 140, true)]
        [InlineData(HeaderShade.None, 120, false)]
        public async Task TheHeaderShadeIsChosenAndFree(HeaderShade shade, int y, bool darker)
        {
            var path = $"{_dir}/white.png";
            using (var white = new Image<Rgba32>(750, 160, new Rgba32(255, 255, 255, 255)))
                white.SaveAsPng(path);
            var layout = ProfileCatalog.FindPreset("minimal").Make();
            layout.Header = new SlotImage { Image = 1 };
            layout.Nick.Hidden = true;
            layout.Shade = shade;

            using var image = (await _img.GetLayeredProfileAsync(layout, Data(new Dictionary<long, string> { [1] = path }))).CloneAs<Rgba32>();

            Assert.Equal(darker, image[400, y].R < 250);
            Assert.Equal(255, image[400, 20].R);
            Assert.DoesNotContain(ProfileRules.RequiredUnlocks(layout), x => x.Contains("shade"));
        }

        [Fact]
        public async Task ALayoutOfTwiceTheSizeDrawsAt750By500()
        {
            var layout = ProfileLayout.FromJson(ProfileCatalog.FindPreset("stats").Make().ToJson().Insert(1, "\"hd\":true,"));

            using var image = await _img.GetLayeredProfileAsync(layout, Data(new Dictionary<long, string>()));

            Assert.Equal((750, 500), (image.Width, image.Height));
        }

        private string Webp(string name, int w, int h, int frames)
        {
            var path = $"{_dir}/{name}";
            using var image = new Image<Rgba32>(w, h, new Rgba32(200, 80, 120, 180));
            for (int i = 1; i < frames; i++)
                image.Frames.CreateFrame(new Rgba32((byte)(i * 7), 160, 90, 200));
            image.SaveAsWebp(path);
            return path;
        }

        [Fact]
        public async Task AMovingPictureKeepsOnlyTheFramesOfThePixelBudget()
        {
            // 2000 x 1000 lets 32 frames into the budget of 64 million pixels
            var images = new Dictionary<long, string> { [1] = Webp("big.webp", 2000, 1000, 40), [2] = Webp("small.webp", 100, 100, 200) };
            var layout = ProfileCatalog.FindPreset("minimal").Make();
            layout.Ultra = new UltraLayer { Image = 1 };
            layout.Animate = ProfileLayout.AnimateUltra;

            using (var big = await _img.GetLayeredProfileAsync(layout, Data(images)))
                Assert.Equal(32, big.Frames.Count);

            layout.Ultra.Image = 2;
            using (var small = await _img.GetLayeredProfileAsync(layout, Data(images)))
                Assert.Equal(ImageProcessing.MaxProfileFrames, small.Frames.Count);

            layout.Animate = null;
            layout.Ultra.Image = 1;
            using var still = await _img.GetLayeredProfileAsync(layout, Data(images));
            Assert.Equal(1, still.Frames.Count);
        }

        [Fact]
        public async Task EveryCardGameSizeAndBigGalleryDraws()
        {
            var layout = new ProfileLayout();
            layout.Layers.Add(new CardsPanelLayer { Id = 1, X = 16, Y = 175, Size = CardsPanelSize.Narrow });
            layout.Layers.Add(new CardsPanelLayer { Id = 2, X = 210, Y = 175, Size = CardsPanelSize.Narrow, Hide = new List<string> { CardsPanelLayer.Waifu } });
            layout.Layers.Add(new CardsPanelLayer { Id = 3, X = 380, Y = 175, Size = CardsPanelSize.Low, Panel = false });
            layout.Layers.Add(new BigGalleryLayer { Id = 4, X = 16, Y = 320, Count = 3 });

            using var image = await _img.GetLayeredProfileAsync(layout, Data(new Dictionary<long, string>()));

            Assert.Equal((750, 500), (image.Width, image.Height));
        }

        [Theory]
        [InlineData("#F1C40F", true)]
        [InlineData("#FFFFFF", true)]
        [InlineData("#546E7A", false)]
        [InlineData("#9B59B6", false)]
        public void LightAccentsTakeDarkLetters(string color, bool light)
        {
            Assert.Equal(light, ImageProcessing.IsLightColor(color));
        }

        [Fact]
        public async Task EveryRankStyleDrawsAtEverySize()
        {
            var layout = ProfileCatalog.FindPreset("minimal").Make();
            layout.Layers.Clear();
            var id = 1;
            foreach (var style in Enum.GetValues<RankStyle>())
                foreach (var size in Enum.GetValues<RankSize>())
                    layout.Layers.Add(new RankLayer { Id = id, X = 20 + (id % 3) * 240, Y = 170 + (id++ / 3) * 40, Style = style, Size = size });

            using var image = await _img.GetLayeredProfileAsync(layout, Data(new Dictionary<long, string>()));

            Assert.Equal((750, 500), (image.Width, image.Height));
        }

        [Fact]
        public async Task EveryPresetDraws()
        {
            var data = Data(new Dictionary<long, string>());
            foreach (var preset in ProfileCatalog.Presets)
            {
                using var image = await _img.GetLayeredProfileAsync(preset.Make(), data);
                Assert.Equal(750, image.Width);
                Assert.Equal(500, image.Height);
                Assert.Equal(1, image.Frames.Count);
            }
        }

        [Fact]
        public async Task EverythingAtOnceDraws()
        {
            var images = new Dictionary<long, string>
            {
                [1] = Picture("header.png", 300, 100), [2] = Picture("style.png", 750, 340),
                [3] = Picture("overlay.png", 200, 120), [4] = Picture("ultra.png", 750, 500), [5] = Picture("frame.png", 150, 150),
            };
            var layout = ProfileCatalog.FindPreset("lists").Make();
            layout.Accent = "#4FC3E8";
            layout.Panels = PanelLook.Glass;
            layout.Bar.At = BarPlace.Top;
            layout.Bar.Hide.Add(BarLayout.Messages);
            layout.Header = new SlotImage { Image = 1, X = 0, Y = -40, Scale = 2.5 };
            layout.Style = new SlotImage { Image = 2, X = 0, Y = 160 };
            layout.Ultra = new UltraLayer { Image = 4 };
            layout.Avatar.Frame = AvatarLayer.CustomFrame;
            layout.Avatar.CustomImage = 5;
            layout.Avatar.Size = 112;
            foreach (var list in layout.LayersOf<ListPanelLayer>())
            {
                list.Recent = 3;
                list.Legend = LegendMode.All;
            }
            layout.Layers.Add(new OverlayLayer { Id = 20, Image = 3, X = 500, Y = 20, Opacity = 0.8 });
            layout.Layers.Add(new StickerLayer { Id = 21, Card = 3, X = 620, Y = 180, H = 150, Tilt = 6, Ground = StickerGround.Shadow });
            layout.Layers.Add(new StickerLayer { Id = 22, Card = 4, X = 300, Y = 330, H = 120, Tilt = -4 });
            layout.Layers.Add(new CardsPanelLayer { Id = 23, X = 190, Y = 182, Hide = new List<string> { CardsPanelLayer.Waifu } });
            layout.Layers.RemoveAll(x => x is ListPanelLayer { Manga: true });

            Assert.Empty(ProfileRules.Validate(layout, new ProfileRulesContext
            {
                Image = id => images.ContainsKey(id) ? new ProfileImageInfo { Id = id, Width = id == 1 ? 300 : 750, Height = id == 1 ? 100 : 500 } : null,
            }));

            using var image = await _img.GetLayeredProfileAsync(layout, Data(images));
            Assert.Equal(1, image.Frames.Count);
        }

        [Fact]
        public async Task OnlyTheChosenPartMoves()
        {
            var images = new Dictionary<long, string> { [3] = Picture("moving.gif", 120, 80, frames: 4), [4] = Picture("moving2.gif", 120, 80, frames: 3) };
            var layout = ProfileCatalog.Default();
            layout.Layers.Add(new OverlayLayer { Id = 20, Image = 3, X = 400, Y = 30 });
            layout.Layers.Add(new OverlayLayer { Id = 21, Image = 4, X = 100, Y = 300 });

            using (var still = await _img.GetLayeredProfileAsync(layout, Data(images)))
                Assert.Equal(1, still.Frames.Count);

            layout.Animate = ProfileLayout.AnimateLayer(21);
            using var moving = await _img.GetLayeredProfileAsync(layout, Data(images));
            Assert.Equal(3, moving.Frames.Count);
        }

        [Fact]
        public async Task ABrokenPictureIsLeftOutOfTheProfile()
        {
            var good = Webp("good.webp", 120, 80, 3);
            var cut = File.ReadAllBytes(good);
            var broken = $"{_dir}/broken.webp";
            File.WriteAllBytes(broken, cut.Take(cut.Length / 2).ToArray());
            var garbage = $"{_dir}/garbage.png";
            File.WriteAllBytes(garbage, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4, 5, 6, 7, 8 });
            var layout = ProfileCatalog.Default();
            layout.Header = new SlotImage { Image = 2 };
            layout.Ultra = new UltraLayer { Image = 1 };
            layout.Layers.Add(new OverlayLayer { Id = 21, Image = 3, X = 10, Y = 10 });
            layout.Animate = ProfileLayout.AnimateUltra;

            using var image = await _img.GetLayeredProfileAsync(layout, Data(new Dictionary<long, string> { [1] = broken, [2] = garbage, [3] = good }));

            Assert.Equal((750, 500), (image.Width, image.Height));
            Assert.Equal(1, image.Frames.Count);
        }

        [Fact]
        public async Task MissingPicturesAndCardsLeaveTheirPlaceEmpty()
        {
            var layout = ProfileCatalog.Default();
            layout.Header = new SlotImage { Image = 9 };
            layout.Layers.Add(new StickerLayer { Id = 20, Card = 999, X = 10, Y = 10 });
            layout.Layers.Add(new OverlayLayer { Id = 21, Image = 8, X = 10, Y = 10 });

            using var image = await _img.GetLayeredProfileAsync(layout, Data(new Dictionary<long, string> { [8] = $"{_dir}/gone.png" }));
            Assert.Equal(750, image.Width);
        }
    }
}
