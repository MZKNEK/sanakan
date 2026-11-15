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
        public async Task TwiceTheSizeDrawsTheSameLayoutAt1500By1000()
        {
            var layout = ProfileCatalog.FindPreset("stats").Make();
            layout.Layers.Add(new StickerLayer { Id = 30, Card = 2, X = 600, Y = 20, H = 120, Tilt = 5, Ground = StickerGround.Glass });
            layout.LayersOf<CardsPanelLayer>().Single().Panel = false;
            layout.Hd = true;

            using var image = await _img.GetLayeredProfileAsync(layout, Data(new Dictionary<long, string>()));

            Assert.Equal((1500, 1000), (image.Width, image.Height));
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
