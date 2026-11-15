using System;
using System.Collections.Generic;
using System.Linq;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services.PocketWaifu;
using Sanakan.Services.Profiles;
using Xunit;

namespace Artifacts
{
    public class ProfileLayoutTests
    {
        private static readonly Dictionary<long, ProfileImageInfo> Images = new Dictionary<long, ProfileImageInfo>
        {
            [1] = new ProfileImageInfo { Id = 1, Width = 750, Height = 160 },
            [2] = new ProfileImageInfo { Id = 2, Width = 750, Height = 340 },
            [3] = new ProfileImageInfo { Id = 3, Width = 750, Height = 402 },
            [4] = new ProfileImageInfo { Id = 4, Width = 750, Height = 500 },
            [5] = new ProfileImageInfo { Id = 5, Width = 300, Height = 300 },
        };

        private static ProfileRulesContext Context(params ulong[] cards) => new ProfileRulesContext
        {
            Image = id => Images.TryGetValue(id, out var i) ? i : null,
            OwnsCard = id => cards.Contains(id),
        };

        private static ProfileLayout Stats() => ProfileCatalog.Default();

        // ---- JSON ----

        [Fact]
        public void LayoutSurvivesJsonRoundTrip()
        {
            var layout = Stats();
            layout.Accent = "#4fc3e8";
            layout.Panels = PanelLook.Glass;
            layout.Header = new SlotImage { Image = 1, Fade = true };
            layout.Layers.Add(new StickerLayer { Id = 9, X = 600, Y = 20, Card = 77, Tilt = -5 });
            layout.Layers.Add(new OverlayLayer { Id = 10, Image = 3, Y = 98, Opacity = 0.5 });
            layout.Ultra = new UltraLayer { Image = 4 };
            layout.Animate = ProfileLayout.AnimateLayer(9);

            var json = layout.ToJson();
            var again = ProfileLayout.FromJson(json);

            Assert.Equal(json, again.ToJson());
            Assert.Contains("\"t\":\"manga\"", json);
            Assert.Contains("\"panels\":\"glass\"", json);
            Assert.True(again.LayersOf<ListPanelLayer>().Single(x => x.Y == 328).Manga);
            Assert.Equal(77UL, again.LayersOf<StickerLayer>().Single().Card);
        }

        [Fact]
        public void UnknownFieldsAndLayerTypesAreSkipped()
        {
            var json = "{\"v\":1,\"future\":{\"a\":1},\"layers\":[{\"t\":\"hologram\",\"id\":1},{\"t\":\"rank\",\"id\":2,\"x\":5,\"y\":6,\"glow\":true},null]}";
            var layout = ProfileLayout.FromJson(json);

            var rank = Assert.IsType<RankLayer>(Assert.Single(layout.Layers));
            Assert.Equal(5, rank.X);
            Assert.NotNull(layout.Bar);
            Assert.NotNull(layout.Avatar);
            Assert.NotNull(layout.Nick);
        }

        // ---- presets and unlocks ----

        [Fact]
        public void EveryPresetIsValidAndFree()
        {
            foreach (var preset in ProfileCatalog.Presets)
            {
                var layout = preset.Make();
                Assert.Empty(ProfileRules.Validate(layout, Context()));
                Assert.True(ProfileRules.IsReadyLayout(layout), preset.Key);
                // a ready layout is free, the large panels some of them use are an option of their own
                var needs = ProfileRules.RequiredUnlocks(layout).Where(x => x != ProfileCatalog.LargeLists);
                Assert.True(!needs.Any(), preset.Key + ": " + string.Join(", ", needs));
            }
        }

        [Fact]
        public void MovingAPanelNeedsTheOwnLayout()
        {
            var layout = Stats();
            layout.LayersOf<CardsPanelLayer>().Single().X += 4;

            Assert.Contains(ProfileCatalog.Layout, ProfileRules.RequiredUnlocks(layout));
        }

        [Fact]
        public void RemovingAPanelKeepsTheLayoutReady()
        {
            var layout = Stats();
            layout.Layers.RemoveAll(x => x is CardsPanelLayer);

            Assert.True(ProfileRules.IsReadyLayout(layout));
        }

        [Fact]
        public void StickersAndOverlaysAreNotPartOfTheLayoutButEachSlotIsAnUnlock()
        {
            var layout = Stats();
            layout.Layers.Add(new StickerLayer { Id = 10, X = 10, Y = 10, Card = 1 });
            layout.Layers.Add(new StickerLayer { Id = 11, X = 300, Y = 40, Card = 2 });
            layout.Layers.Add(new OverlayLayer { Id = 12, Image = 5, X = 100, Y = 100 });

            var needs = ProfileRules.RequiredUnlocks(layout);

            Assert.Equal(new[] { "overlay:1", "sticker:1", "sticker:2" }, needs);
        }

        [Theory]
        [InlineData("None", null)]
        [InlineData("Base", null)]
        [InlineData("Water", "frame:Water")]
        [InlineData("custom", "customFrame")]
        public void FramesNeedTheirOwnUnlock(string frame, string expected)
        {
            var layout = Stats();
            layout.Avatar.Frame = frame;

            var needs = ProfileRules.RequiredUnlocks(layout);

            if (expected == null) Assert.Empty(needs);
            else Assert.Equal(new[] { expected }, needs);
        }

        [Fact]
        public void EachOptionNeedsItsUnlock()
        {
            var layout = Stats();
            layout.Accent = "#ff0000";
            layout.Panels = PanelLook.Shadow;
            layout.Bar.At = BarPlace.Top;
            layout.Bar.Hide.Add(BarLayout.Ac);
            layout.Nick.Hidden = true;
            layout.Avatar.Size = 112;
            layout.Header = new SlotImage { Image = 1 };
            layout.Style = new SlotImage { Image = 2, Y = 160 };
            layout.Ultra = new UltraLayer { Image = 4 };
            var anime = layout.LayersOf<ListPanelLayer>().First();
            anime.Recent = 1;
            anime.Hide.Add(ListPanelLayer.Mean);

            var needs = ProfileRules.RequiredUnlocks(layout);

            Assert.Equal(new[] { "accent", "avatar", "barHide", "barPos", "headerImg", "nickHide", "panelHide", "panels", "recent", "styleImg", "ultra" }, needs);
        }

        [Fact]
        public void ASquareAvatarIsPaidOnlyWithoutAFrame()
        {
            var layout = Stats();
            layout.Avatar.Shape = AvatarShape.Square;
            Assert.Empty(ProfileRules.RequiredUnlocks(layout));

            layout.Avatar.Frame = AvatarLayer.NoFrame;
            Assert.Contains(ProfileCatalog.AvatarLook, ProfileRules.RequiredUnlocks(layout));
        }

        [Fact]
        public void OnlyMissingUnlocksAreCharged()
        {
            var layout = Stats();
            layout.Accent = "#ff0000";
            layout.Ultra = new UltraLayer { Image = 4 };

            var missing = ProfileRules.MissingUnlocks(layout, new[] { ProfileCatalog.Ultra });

            var feature = Assert.Single(missing);
            Assert.Equal(ProfileCatalog.Accent, feature.Key);
            Assert.Equal(18000, ProfileRules.Cost(missing, CurrencyType.SC));
            Assert.Equal(299, ProfileRules.Cost(missing, CurrencyType.TC));
        }

        [Fact]
        public void EveryPaidFrameAndSlotHasAPrice()
        {
            foreach (var border in ProfileCatalog.PaidFrames)
                Assert.NotNull(ProfileCatalog.Feature(ProfileCatalog.Frame(border)));
            for (int i = 1; i <= ProfileCatalog.MaxStickers; i++)
                Assert.NotNull(ProfileCatalog.Feature(ProfileCatalog.Sticker(i)));
            for (int i = 1; i <= ProfileCatalog.MaxOverlays; i++)
                Assert.NotNull(ProfileCatalog.Feature(ProfileCatalog.Overlay(i)));
            Assert.All(ProfileCatalog.Features, f => Assert.True(f.Sc > 0 && f.Tc > 0, f.Key));
            Assert.Equal(ProfileCatalog.Features.Count, ProfileCatalog.Features.Select(x => x.Key).Distinct().Count());
        }

        // ---- validation ----

        private static List<string> Errors(Action<ProfileLayout> change, params ulong[] cards)
        {
            var layout = Stats();
            change(layout);
            return ProfileRules.Validate(layout, Context(cards));
        }

        [Fact]
        public void AnOverlayMustReachIntoItsFieldAndOnlyTheUltraOneCoversTheTop()
        {
            Assert.Empty(Errors(l => l.Layers.Add(new OverlayLayer { Id = 20, Image = 5, X = 0, Y = -100 })));
            Assert.NotEmpty(Errors(l => l.Layers.Add(new OverlayLayer { Id = 20, Image = 5, X = 0, Y = -220 })));
            Assert.Empty(Errors(l => l.Ultra = new UltraLayer { Id = 20, Image = 5, X = 0, Y = -220 }));
        }

        [Fact]
        public void TheHeaderMustCoverItsField()
        {
            Assert.Empty(Errors(l => l.Header = new SlotImage { Image = 1 }));
            Assert.NotEmpty(Errors(l => l.Header = new SlotImage { Image = 1, X = 10 }));
            Assert.NotEmpty(Errors(l => l.Header = new SlotImage { Image = 1, Scale = 0.9 }));
            Assert.Empty(Errors(l => l.Header = new SlotImage { Image = 5, X = 0, Y = -100, Scale = 2.5 }));
            Assert.NotEmpty(Errors(l => l.Header = new SlotImage { Image = 5, X = 0, Y = -650, Scale = 2.5 }));
            Assert.NotEmpty(Errors(l => l.Header = new SlotImage { Image = 99 }));
        }

        [Fact]
        public void StickersMustBeTheirOwnCardsAndWithinLimits()
        {
            Assert.Empty(Errors(l => l.Layers.Add(new StickerLayer { Id = 9, X = 10, Y = 10, Card = 5 }), 5));
            Assert.NotEmpty(Errors(l => l.Layers.Add(new StickerLayer { Id = 9, X = 10, Y = 10, Card = 6 }), 5));
            Assert.NotEmpty(Errors(l => l.Layers.Add(new StickerLayer { Id = 9, X = 10, Y = 10, Card = 5, Tilt = 40 }), 5));
            Assert.NotEmpty(Errors(l =>
            {
                for (int i = 0; i < 5; i++)
                    l.Layers.Add(new StickerLayer { Id = 20 + i, X = 10 + i, Y = 10, Card = 5 });
            }, 5));
        }

        [Fact]
        public void PanelsAreSingleAndStayInside()
        {
            Assert.NotEmpty(Errors(l => l.Layers.Add(new CardsPanelLayer { Id = 9, X = 0, Y = 0 })));
            Assert.NotEmpty(Errors(l => l.Layers.Add(new ListPanelLayer { Id = 9, X = 0, Y = 0 })));
            Assert.NotEmpty(Errors(l => l.LayersOf<CardsPanelLayer>().Single().X = 740));
            Assert.Empty(Errors(l => l.LayersOf<CardsPanelLayer>().Single().X = 734));
            Assert.NotEmpty(Errors(l => l.Layers[0].Id = l.Layers[1].Id));
        }

        [Fact]
        public void OptionsAreChecked()
        {
            Assert.NotEmpty(Errors(l => l.Accent = "red"));
            Assert.NotEmpty(Errors(l => l.Avatar.Size = 100));
            Assert.NotEmpty(Errors(l => l.Avatar.Frame = "Hologram"));
            Assert.NotEmpty(Errors(l => l.Avatar.Frame = AvatarLayer.CustomFrame));
            Assert.Empty(Errors(l => { l.Avatar.Frame = AvatarLayer.CustomFrame; l.Avatar.CustomImage = 5; }));
            Assert.NotEmpty(Errors(l => l.Bar.Hide.Add("gold")));
            Assert.NotEmpty(Errors(l => l.Nick.W = 900));
            Assert.NotEmpty(Errors(l => l.LayersOf<ListPanelLayer>().First().Recent = 4));
        }

        [Fact]
        public void OnlyExistingPartsCanMove()
        {
            Assert.Empty(Errors(l => l.Animate = ProfileLayout.AnimateAvatar));
            Assert.NotEmpty(Errors(l => l.Animate = ProfileLayout.AnimateUltra));
            Assert.NotEmpty(Errors(l => l.Animate = ProfileLayout.AnimateLayer(99)));
            Assert.NotEmpty(Errors(l => l.Animate = ProfileLayout.AnimateLayer(l.LayersOf<RankLayer>().Single().Id)));
            Assert.Empty(Errors(l => l.Animate = ProfileLayout.AnimateLayer(l.LayersOf<CardsPanelLayer>().Single().Id)));
        }

        // ---- the old profiles ----

        private static readonly DateTime Now = new DateTime(2026, 11, 15);

        private static User OldUser(ProfileType type, ProfileSettings settings = ProfileSettings.Default, AvatarBorder border = AvatarBorder.Base)
        {
            var user = new User().Default(42, Now);
            user.ProfileType = type;
            user.StatsStyleSettings = settings;
            user.AvatarBorder = border;
            return user;
        }

        private static readonly Dictionary<ProfileImageUse, long> UseImage = new Dictionary<ProfileImageUse, long>
        {
            [ProfileImageUse.Header] = 1, [ProfileImageUse.Style] = 2, [ProfileImageUse.Overlay] = 3, [ProfileImageUse.Ultra] = 4,
        };

        private static LegacyProfile Convert(User user, ulong waifu = 0)
        {
            var registered = new List<(ProfileImageUse, string)>();
            var result = ProfileLegacy.FromUser(user, waifu, (use, path) =>
            {
                registered.Add((use, path));
                // the picture on the stats becomes an overlay of 750 x 340
                return use == ProfileImageUse.Overlay && path == user.StatsReplacementProfileUri ? 2 : UseImage[use];
            });
            Assert.Empty(ProfileRules.Validate(result.Layout, Context(waifu)));
            Assert.Subset(result.Unlocks, ProfileRules.RequiredUnlocks(result.Layout));
            return result;
        }

        [Fact]
        public void AFreshProfileIsNotCustomisedAndConvertsToTheDefault()
        {
            var user = OldUser(ProfileType.Stats);
            Assert.False(ProfileLegacy.IsCustomised(user));

            var result = Convert(user);

            Assert.Empty(result.Unlocks);
            Assert.Null(result.Layout.Header);
            Assert.Equal(new[] { "anime", "manga", "cards", "rank" }, result.Layout.Layers.Select(x => x.Type));
            Assert.Equal(ProfileLayout.AnimateLayer(result.Layout.LayersOf<CardsPanelLayer>().Single().Id), result.Layout.Animate);
        }

        [Fact]
        public void AFullyDressedProfileKeepsEverythingUnlocked()
        {
            var user = OldUser(ProfileType.StatsOnImg, ProfileSettings.Default | ProfileSettings.Flip | ProfileSettings.BarOnTop | ProfileSettings.ShowWaifu, AvatarBorder.Water);
            user.BackgroundProfileUri = "../GOut/Saved/BG42.webp";
            user.StatsReplacementProfileUri = "../GOut/Saved/SR42.webp";
            user.CustomProfileOverlayUrl = "../GOut/Saved/OV42.webp";
            user.PremiumCustomProfileOverlayUrl = "../GOut/Saved/UOV42.webp";
            Assert.True(ProfileLegacy.IsCustomised(user));

            var result = Convert(user, 777);
            var layout = result.Layout;

            Assert.Equal(new[] { "barPos", "frame:Water", "headerImg", "overlay:1", "panels", "sticker:1", "styleImg", "ultra" }, result.Unlocks);
            Assert.Equal(PanelLook.Shadow, layout.Panels);
            Assert.Equal(BarPlace.Top, layout.Bar.At);
            Assert.Equal(403, layout.LayersOf<ListPanelLayer>().First().X);
            Assert.Equal(16, layout.LayersOf<CardsPanelLayer>().Single().X);
            Assert.Equal(777UL, layout.LayersOf<StickerLayer>().Single().Card);
            Assert.Equal(98, layout.LayersOf<OverlayLayer>().Single().Y);
            // drawn as before: the rank under the overlay, the waifu over it
            Assert.Equal(new[] { "rank", "overlay", "sticker" }, layout.Layers.Where(x => x is RankLayer or OverlayLayer or StickerLayer).Select(x => x.Type));
            Assert.NotNull(layout.Ultra);
            Assert.Equal("Water", layout.Avatar.Frame);
            Assert.True(ProfileRules.IsReadyLayout(layout));
        }

        [Fact]
        public void HiddenOverlaysStayUnlockedButHidden()
        {
            var user = OldUser(ProfileType.Stats, ProfileSettings.Default & ~ProfileSettings.ShowOverlay & ~ProfileSettings.ShowOverlayPro);
            user.CustomProfileOverlayUrl = "../GOut/Saved/OV42.webp";
            user.PremiumCustomProfileOverlayUrl = "../GOut/Saved/UOV42.webp";

            var result = Convert(user);

            Assert.True(result.Layout.LayersOf<OverlayLayer>().Single().Hidden);
            Assert.True(result.Layout.Ultra.Hidden);
            Assert.Contains(ProfileCatalog.Overlay(1), result.Unlocks);
            Assert.Contains(ProfileCatalog.Ultra, result.Unlocks);
        }

        [Fact]
        public void ThePictureOnTheStatsBecomesAnOverlayOverThePanels()
        {
            var user = OldUser(ProfileType.StatsWithImg);
            user.StatsReplacementProfileUri = "../GOut/Saved/SR42.webp";

            var layout = Convert(user).Layout;

            Assert.Null(layout.Style);
            var overlay = layout.LayersOf<OverlayLayer>().Single();
            Assert.Equal((0, 160), (overlay.X, overlay.Y));
            Assert.True(layout.Layers.IndexOf(overlay) > layout.Layers.FindIndex(x => x is CardsPanelLayer));
        }

        [Theory]
        [InlineData(ProfileType.Img, new string[0])]
        [InlineData(ProfileType.Cards, new[] { "bigGallery" })]
        [InlineData(ProfileType.CardsOnImg, new[] { "bigGallery" })]
        [InlineData(ProfileType.MiniGallery, new[] { "gallery", "cards" })]
        [InlineData(ProfileType.MiniGalleryOnImg, new[] { "gallery", "cards" })]
        public void EveryOldStyleHasItsPanels(ProfileType type, string[] panels)
        {
            var user = OldUser(type);
            user.StatsReplacementProfileUri = "../GOut/Saved/SR42.webp";

            var layout = Convert(user).Layout;

            Assert.Equal(panels.Append("rank"), layout.Layers.Select(x => x.Type));
            var onImage = type is ProfileType.Img or ProfileType.CardsOnImg or ProfileType.MiniGalleryOnImg;
            Assert.Equal(onImage, layout.Style != null);
            Assert.True(ProfileRules.IsReadyLayout(layout));
        }

        [Fact]
        public void TheHalfGalleryShowsTwoCards()
        {
            var layout = Convert(OldUser(ProfileType.MiniGallery, ProfileSettings.Default | ProfileSettings.HalfGallery)).Layout;
            Assert.Equal(2, layout.LayersOf<GalleryLayer>().Single().Count);
        }

        [Fact]
        public void HiddenAnimeMovesTheMangaUpUnderAnOwnLayout()
        {
            var result = Convert(OldUser(ProfileType.Stats, ProfileSettings.Default & ~ProfileSettings.ShowAnime));

            Assert.Equal(182, result.Layout.LayersOf<ListPanelLayer>().Single().Y);
            Assert.Contains(ProfileCatalog.Layout, result.Unlocks);
        }

        [Fact]
        public void TheOldSquareAvatarStaysSquare()
        {
            var result = Convert(OldUser(ProfileType.Stats, ProfileSettings.Default, AvatarBorder.None));

            Assert.Equal(AvatarShape.Square, result.Layout.Avatar.Shape);
            Assert.Contains(ProfileCatalog.AvatarLook, result.Unlocks);

            var round = Convert(OldUser(ProfileType.Stats, ProfileSettings.Default | ProfileSettings.RoundAvatar, AvatarBorder.None));
            Assert.Equal(AvatarShape.Circle, round.Layout.Avatar.Shape);
            Assert.Empty(round.Unlocks);
        }
    }
}
