#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.Linq;
using Sanakan.Database.Models;

namespace Sanakan.Services.Profiles
{
    public class ProfileFeature
    {
        public ProfileFeature(string key, string name, string description, long sc, long tc, bool tcOnly = false)
        {
            Key = key;
            Name = name;
            Description = description;
            Sc = sc;
            Tc = tc;
            TcOnly = tcOnly;
        }

        public string Key { get; }
        public string Name { get; }
        public string Description { get; }
        public long Sc { get; }
        public long Tc { get; }
        // bought only with TC; a save that buys it is paid in TC whole
        public bool TcOnly { get; }
    }

    // The one-time unlocks with their prices, the limits, the sizes of the parts and the ready layouts.
    // The API hands all of it to Profilator, so the site keeps no prices or sizes of its own.
    public static class ProfileCatalog
    {
        public const int MaxStickers = 4;
        public const int MaxOverlays = 3;
        public const int MinInside = 16;
        public const int MaxLayoutJson = 16 * 1024;

        public const string BarHide = "barHide";
        public const string BarPlace = "barPos";
        public const string PanelHide = "panelHide";
        public const string NickHide = "nickHide";
        public const string PanelLook = "panels";
        public const string AvatarLook = "avatar";
        public const string Recent = "recent";
        public const string Accent = "accent";
        public const string LargeLists = "listLarge";
        public const string StyleImage = "styleImg";
        public const string HeaderImage = "headerImg";
        public const string Layout = "layout";
        public const string CustomFrame = "customFrame";
        public const string Ultra = "ultra";
        public const string StickerPrefix = "sticker:";
        public const string OverlayPrefix = "overlay:";
        public const string FramePrefix = "frame:";

        public static string Sticker(int slot) => StickerPrefix + slot;
        public static string Overlay(int slot) => OverlayPrefix + slot;
        public static string Frame(AvatarBorder border) => FramePrefix + border;

        private static readonly long[][] StickerPrices = { new long[] { 9000, 149 }, new long[] { 15000, 249 }, new long[] { 24000, 399 }, new long[] { 36000, 599 } };
        private static readonly long[][] OverlayPrices = { new long[] { 30000, 499 }, new long[] { 45000, 749 }, new long[] { 60000, 999 } };

        public static readonly IReadOnlyList<ProfileFeature> Features = BuildFeatures();

        private static readonly Dictionary<string, ProfileFeature> ByKey = Features.ToDictionary(x => x.Key);

        private static List<ProfileFeature> BuildFeatures()
        {
            var list = new List<ProfileFeature>
            {
                new ProfileFeature(BarHide, "Ukrywanie pozycji paska", "dowolne pozycje paska", 6000, 99),
                new ProfileFeature(BarPlace, "Położenie paska", "pasek na górze albo ukryty", 6000, 99),
                new ProfileFeature(PanelHide, "Ukrywanie pozycji paneli", "karcianka, anime i manga", 6000, 99),
                new ProfileFeature(NickHide, "Ukrycie nicka", "np. dla własnego nicka na nakładce", 6000, 99),
                new ProfileFeature(PanelLook, "Wygląd paneli", "szkło i cień paneli, szkło pod paskiem i kartami", 12000, 199),
                new ProfileFeature(AvatarLook, "Awatar: rozmiar i kształt", "większy awatar, zaokrąglony i kwadrat", 12000, 199),
                new ProfileFeature(Recent, "Ostatnio oglądane i czytane", "ostatnie tytuły w panelach anime i mangi", 12000, 199),
                new ProfileFeature(Accent, "Kolor akcentu", "dowolny kolor zamiast koloru roli", 18000, 299),
                new ProfileFeature(LargeLists, "Duże panele anime i mangi", "cztery liczby, wszystkie statusy, więcej tytułów", 18000, 299),
                new ProfileFeature(StyleImage, "Własny obrazek stylu", "obrazek w polu 750 × 340, zmiany za darmo", 24000, 399),
                new ProfileFeature(HeaderImage, "Własne tło nagłówka", "obrazek w polu 750 × 160, zmiany za darmo", 30000, 499),
            };
            for (int i = 0; i < MaxStickers; i++)
                list.Add(new ProfileFeature(Sticker(i + 1), $"Miejsce na kartę-naklejkę {i + 1}", "karta postawiona w dowolnym miejscu", StickerPrices[i][0], StickerPrices[i][1]));
            for (int i = 0; i < MaxOverlays; i++)
                list.Add(new ProfileFeature(Overlay(i + 1), $"Miejsce na nakładkę {i + 1}", "obrazek od wysokości nicka w dół, zmiany za darmo", OverlayPrices[i][0], OverlayPrices[i][1]));
            list.Add(new ProfileFeature(Layout, "Własny układ", "przesuwanie paneli, nicka, rangi i awatara", 48000, 799));
            foreach (var border in PaidFrames)
                list.Add(new ProfileFeature(Frame(border), $"Ramka awatara: {FrameName(border)}", "ramka z bota, na zawsze", 36000, 599));
            list.Add(new ProfileFeature(CustomFrame, "Własna ramka awatara", "własny obrazek wokół awatara, zmiany za darmo", 72000, 1199));
            list.Add(new ProfileFeature(Ultra, "Ultra nakładka", "obrazek na całym profilu, zmiany za darmo", 240000, 3999));
            return list;
        }

        public static ProfileFeature Feature(string key) => key != null && ByKey.TryGetValue(key, out var f) ? f : null;

        public static bool IsFreeFrame(AvatarBorder border) => border == AvatarBorder.None || border == AvatarBorder.Base;

        // TurqLeaves was given out, never sold; it stays with whoever has it
        public static bool IsSelectableFrame(AvatarBorder border) => border != AvatarBorder.TurqLeaves;

        public static IEnumerable<AvatarBorder> PaidFrames => Enum.GetValues<AvatarBorder>().Where(x => !IsFreeFrame(x));

        public static string FrameName(AvatarBorder border) => border switch
        {
            AvatarBorder.None => "brak",
            AvatarBorder.Base => "domyślna",
            AvatarBorder.PurpleLeaves => "liście",
            AvatarBorder.Dzedai => "dzidowa",
            AvatarBorder.Water => "woda",
            AvatarBorder.Crows => "kruki",
            AvatarBorder.Bow => "wstążka",
            AvatarBorder.Metal => "metalowa",
            AvatarBorder.RedThinLeaves => "kwiatki",
            AvatarBorder.Skull => "czaszka",
            AvatarBorder.Fire => "ogień",
            AvatarBorder.Promium => "promium",
            AvatarBorder.Ice => "lód",
            AvatarBorder.Gold => "złota",
            AvatarBorder.Red => "czerwona",
            AvatarBorder.Rainbow => "tęcza",
            AvatarBorder.Pink => "różowa",
            AvatarBorder.Simple => "prosta",
            AvatarBorder.TurqLeaves => "turkusowe liście",
            _ => border.ToString(),
        };

        public static bool TryParseFrame(string frame, out AvatarBorder border)
        {
            border = AvatarBorder.None;
            return !string.IsNullOrEmpty(frame) && Enum.TryParse(frame, false, out border) && Enum.IsDefined(border);
        }

        // ---- sizes ----

        public static readonly (int X, int Y, int W, int H) HeaderSlot = (0, 0, 750, 160);
        public static readonly (int X, int Y, int W, int H) StyleSlot = (0, 160, 750, 340);
        // an overlay is cut to it, the old overlay's place; only the ultra overlay covers the whole profile
        public static readonly (int X, int Y, int W, int H) OverlayField = (0, 98, 750, 402);

        public const int SmallListHeight = 130;
        public const int LargeListHeight = 276;
        public const int ListWidth = 331;
        public const int CardsPanelWidth = 370;
        public const int PanelHeight = 276;
        public const int BigGalleryWidth = 718;
        public const int BigGalleryHeight = 304;
        public const int NarrowCardsPanelWidth = 186;
        public const int LowCardsPanelHeight = 130;
        public const int BigGalleryRowHeight = 164;
        public const int BigGalleryHalfWidth = 360;
        public const int RankHeight = 22;

        // the rank's font size, height and padding at each size
        public static (int Font, int Height, int Pad) RankMetrics(RankSize size) => size switch
        {
            RankSize.Small => (11, 18, 9),
            RankSize.Large => (16, 27, 14),
            _ => (13, RankHeight, 11),
        };
        public const double CardRatio = 475.0 / 667.0;

        public static readonly int[] AvatarSizes = { 84, 112, 140 };
        public const int DefaultAvatarSize = 84;

        public static int StickerWidth(int height) => (int)Math.Round(CardRatio * height);

        // the box of a free layer; the rank's width depends on its text, so it is given by the caller
        public static (int W, int H) Size(ProfileLayer layer, Func<long, (int W, int H)?> imageSize = null, int rankWidth = 0) => layer switch
        {
            ListPanelLayer l => (ListWidth, l.Size == ListPanelSize.Large ? LargeListHeight : SmallListHeight),
            CardsPanelLayer c => c.Size switch
            {
                CardsPanelSize.Narrow => (NarrowCardsPanelWidth, PanelHeight),
                CardsPanelSize.Low => (CardsPanelWidth, LowCardsPanelHeight),
                _ => (CardsPanelWidth, PanelHeight),
            },
            GalleryLayer => (ListWidth, PanelHeight),
            BigGalleryLayer g => g.Count switch
            {
                3 => (BigGalleryHalfWidth, BigGalleryRowHeight),
                6 => (BigGalleryWidth, BigGalleryRowHeight),
                _ => (BigGalleryWidth, BigGalleryHeight),
            },
            StickerLayer s => (StickerWidth(s.H), s.H),
            RankLayer r => (rankWidth, RankMetrics(r.Size).Height),
            ImageLayerBase i => imageSize?.Invoke(i.Image) is (int w, int h) ? ((int)Math.Round(w * i.Scale), (int)Math.Round(h * i.Scale)) : (0, 0),
            _ => (0, 0),
        };

        // ---- ready layouts, free to use ----

        public class Preset
        {
            public Preset(string key, string name, Func<ProfileLayout> make)
            {
                Key = key;
                Name = name;
                Make = make;
            }

            public string Key { get; }
            public string Name { get; }
            public Func<ProfileLayout> Make { get; }
        }

        private static ProfileLayout Base(params ProfileLayer[] layers)
        {
            var layout = new ProfileLayout();
            int id = 1;
            foreach (var layer in layers)
            {
                layer.Id = id++;
                layout.Layers.Add(layer);
            }
            return layout;
        }

        private static ListPanelLayer Anime(int x, int y, ListPanelSize size = ListPanelSize.Small) => new ListPanelLayer { X = x, Y = y, Size = size };
        private static ListPanelLayer Manga(int x, int y, ListPanelSize size = ListPanelSize.Small) => new ListPanelLayer { X = x, Y = y, Size = size, Manga = true };
        private static RankLayer Rank(int x = 163, int y = 135, NickAlign align = NickAlign.Left) => new RankLayer { X = x, Y = y, Align = align };

        public static readonly IReadOnlyList<Preset> Presets = new List<Preset>
        {
            new Preset("stats", "Statystyki", () => Base(Anime(16, 182), Manga(16, 328), new CardsPanelLayer { X = 368, Y = 182 }, Rank())),
            new Preset("statsFlip", "Statystyki, panele zamienione", () => Base(Anime(403, 182), Manga(403, 328), new CardsPanelLayer { X = 16, Y = 182 }, Rank())),
            new Preset("gallery", "Galeria z karcianką", () => Base(new GalleryLayer { X = 16, Y = 182 }, new CardsPanelLayer { X = 368, Y = 182 }, Rank())),
            new Preset("galleryFlip", "Galeria z karcianką, panele zamienione", () => Base(new GalleryLayer { X = 403, Y = 182 }, new CardsPanelLayer { X = 16, Y = 182 }, Rank())),
            new Preset("bigGallery", "Duża galeria", () => Base(new BigGalleryLayer { X = 16, Y = 164 }, Rank())),
            new Preset("lists", "Duże listy", () => Base(Anime(16, 182, ListPanelSize.Large), Manga(403, 182, ListPanelSize.Large), Rank())),
            new Preset("animeCards", "Anime i karcianka", () => Base(Anime(16, 182, ListPanelSize.Large), new CardsPanelLayer { X = 368, Y = 182 }, Rank())),
            new Preset("duo", "Anime i manga", () => Base(Anime(16, 190), Manga(403, 190), Rank())),
            new Preset("cardsCenter", "Karcianka w centrum", () => Base(new CardsPanelLayer { X = 190, Y = 182 }, Rank())),
            new Preset("card", "Wizytówka", () =>
            {
                var layout = Base(Rank(375, 286, NickAlign.Center));
                layout.Avatar.X = 305;
                layout.Avatar.Y = 84;
                layout.Nick = new NickLayer { X = 125, Y = 236, W = 500, Align = NickAlign.Center };
                return layout;
            }),
            new Preset("minimal", "Minimalny", () =>
            {
                var layout = Base(Rank(170, 130));
                layout.Avatar.X = 40;
                layout.Avatar.Y = 50;
                layout.Nick = new NickLayer { X = 170, Y = 92, W = 540 };
                return layout;
            }),
            new Preset("image", "Sam obrazek", () => Base(Rank())),
        };

        public static ProfileLayout Default() => Presets[0].Make();

        public static Preset FindPreset(string key) => Presets.FirstOrDefault(x => x.Key == key);
    }
}
