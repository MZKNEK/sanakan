#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Sanakan.Database.Models;

namespace Sanakan.Services.Profiles
{
    public class ProfileImageInfo
    {
        public long Id { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool Animated { get; set; }
    }

    // what the rules need to know about the player: their pictures and cards
    public class ProfileRulesContext
    {
        public Func<long, ProfileImageInfo> Image { get; set; } = _ => null;
        public Func<ulong, bool> OwnsCard { get; set; } = _ => true;
        // the rank's pill is as wide as its text; the bounds check takes this width
        public int RankWidth { get; set; } = 120;
    }

    public static class ProfileRules
    {
        public const int MaxLayers = 24;
        public const double MaxSlotZoom = 4;
        public const double MinImageScale = 0.1;
        public const double MaxImageScale = 3;
        public const int MinNickWidth = 200;
        public const int MaxNickWidth = 600;
        public const int MaxCustomFrameShift = 60;
        public const double MinCustomFrameScale = 0.5;
        public const double MaxCustomFrameScale = 2;

        private static readonly Regex HexColor = new Regex("^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);

        private static readonly Type[] Single = { typeof(CardsPanelLayer), typeof(GalleryLayer), typeof(BigGalleryLayer), typeof(RankLayer) };

        public static double CoverScale((int X, int Y, int W, int H) slot, ProfileImageInfo image)
            => Math.Max(slot.W / (double)image.Width, slot.H / (double)image.Height);

        // where a free layer stands; a centred rank stands around its x
        public static (int X, int Y, int W, int H) Box(ProfileLayer layer, ProfileRulesContext context)
        {
            var (w, h) = ProfileCatalog.Size(layer, id => context.Image(id) is ProfileImageInfo i ? (i.Width, i.Height) : null, context.RankWidth);
            var x = layer is RankLayer { Align: NickAlign.Center } ? layer.X - w / 2 : layer.X;
            return (x, layer.Y, w, h);
        }

        private static bool Inside(int x, int y, int w, int h)
            => Inside(x, y, w, h, (0, 0, ProfileLayout.Width, ProfileLayout.Height));

        private static bool Inside(int x, int y, int w, int h, (int X, int Y, int W, int H) field)
        {
            var m = ProfileCatalog.MinInside;
            return x + w >= field.X + m && x <= field.X + field.W - m && y + h >= field.Y + m && y <= field.Y + field.H - m;
        }

        // the reasons the layout cannot be saved, empty when it can
        public static List<string> Validate(ProfileLayout layout, ProfileRulesContext context)
        {
            var errors = new List<string>();
            void Fail(string reason) => errors.Add(reason);

            if (layout == null)
            {
                Fail("brak układu");
                return errors;
            }

            if (layout.Version < 1 || layout.Version > ProfileLayout.CurrentVersion)
                Fail("nieznana wersja układu");

            if (layout.Accent != null && !HexColor.IsMatch(layout.Accent))
                Fail("kolor akcentu musi mieć postać #rrggbb");

            if (layout.ShadowOpacity < 0 || layout.ShadowOpacity > 100)
                Fail("krycie cienia musi być od 0 do 100");

            if (layout.Bar.Opacity < 20 || layout.Bar.Opacity > 100)
                Fail("krycie paska musi być od 20 do 100");

            if ((layout.Bar.Hide ?? new List<string>()).Any(x => !BarLayout.Items.Contains(x)))
                Fail("nieznana pozycja paska");

            CheckSlot(layout.Header, ProfileCatalog.HeaderSlot, "tło nagłówka", context, Fail);
            CheckSlot(layout.Style, ProfileCatalog.StyleSlot, "obrazek stylu", context, Fail);
            CheckAvatar(layout.Avatar, context, Fail);

            var nick = layout.Nick;
            if (nick.W < MinNickWidth || nick.W > MaxNickWidth)
                Fail($"szerokość nicka musi być od {MinNickWidth} do {MaxNickWidth} px");
            if (!Inside(nick.X, nick.Y, nick.W, NickLayer.Height))
                Fail("nick wychodzi poza obrazek");

            if (layout.Layers.Count > MaxLayers)
                Fail("za dużo warstw");

            if (layout.Layers.Any(x => x.Id <= 0) || layout.Layers.Select(x => x.Id).Distinct().Count() != layout.Layers.Count)
                Fail("warstwy muszą mieć różne numery");

            if (layout.LayersOf<StickerLayer>().Count() > ProfileCatalog.MaxStickers)
                Fail($"najwyżej {ProfileCatalog.MaxStickers} karty-naklejki");

            if (layout.LayersOf<OverlayLayer>().Count() > ProfileCatalog.MaxOverlays)
                Fail($"najwyżej {ProfileCatalog.MaxOverlays} nakładki");

            if (layout.LayersOf<ListPanelLayer>().Count(x => !x.Manga) > 1 || layout.LayersOf<ListPanelLayer>().Count(x => x.Manga) > 1
                || Single.Any(t => layout.Layers.Count(x => x.GetType() == t) > 1))
                Fail("każdy panel może być tylko raz");

            foreach (var layer in layout.Layers)
                CheckLayer(layer, context, Fail);

            if (layout.Ultra != null)
                CheckImageLayer(layout.Ultra, "ultra nakładka", context, Fail);

            CheckAnimate(layout, Fail);

            return errors;
        }

        private static void CheckSlot(SlotImage slot, (int X, int Y, int W, int H) field, string name, ProfileRulesContext context, Action<string> fail)
        {
            if (slot == null)
                return;

            var image = context.Image(slot.Image);
            if (image == null || image.Width <= 0 || image.Height <= 0)
            {
                fail($"{name}: nieznany obrazek");
                return;
            }

            var cover = CoverScale(field, image);
            if (slot.Scale < cover - 1e-6 || slot.Scale > cover * MaxSlotZoom + 1e-6)
            {
                fail($"{name}: powiększenie poza zakresem");
                return;
            }

            // the picture always covers its field, a pixel of rounding allowed
            var w = image.Width * slot.Scale;
            var h = image.Height * slot.Scale;
            if (slot.X > field.X + 1 || slot.Y > field.Y + 1 || slot.X + w < field.X + field.W - 1 || slot.Y + h < field.Y + field.H - 1)
                fail($"{name}: obrazek musi wypełniać całe pole");
        }

        private static void CheckAvatar(AvatarLayer avatar, ProfileRulesContext context, Action<string> fail)
        {
            if (!ProfileCatalog.AvatarSizes.Contains(avatar.Size))
                fail("nieznany rozmiar awatara");

            if (!Inside(avatar.X, avatar.Y, avatar.Size, avatar.Size))
                fail("awatar wychodzi poza obrazek");

            if (avatar.HasCustomFrame)
            {
                if (context.Image(avatar.CustomImage) == null)
                    fail("własna ramka: nieznany obrazek");
                if (avatar.CustomScale < MinCustomFrameScale || avatar.CustomScale > MaxCustomFrameScale)
                    fail("własna ramka: skala poza zakresem");
                if (Math.Abs(avatar.CustomX) > MaxCustomFrameShift || Math.Abs(avatar.CustomY) > MaxCustomFrameShift)
                    fail("własna ramka: przesunięcie poza zakresem");
            }
            else if (avatar.HasFrame && !ProfileCatalog.TryParseFrame(avatar.Frame, out _))
            {
                fail("nieznana ramka awatara");
            }
        }

        private static void CheckImageLayer(ImageLayerBase layer, string name, ProfileRulesContext context, Action<string> fail,
            (int X, int Y, int W, int H)? field = null)
        {
            var image = context.Image(layer.Image);
            if (image == null)
            {
                fail($"{name}: nieznany obrazek");
                return;
            }
            if (layer.Scale < MinImageScale || layer.Scale > MaxImageScale)
                fail($"{name}: skala poza zakresem");
            if (layer.Opacity < 0.1 || layer.Opacity > 1)
                fail($"{name}: krycie poza zakresem");
            if (!Inside(layer.X, layer.Y, (int)Math.Round(image.Width * layer.Scale), (int)Math.Round(image.Height * layer.Scale),
                    field ?? (0, 0, ProfileLayout.Width, ProfileLayout.Height)))
                fail($"{name}: wychodzi poza {(field == null ? "obrazek" : "swoje pole")}");
        }

        private static void CheckLayer(ProfileLayer layer, ProfileRulesContext context, Action<string> fail)
        {
            switch (layer)
            {
                case OverlayLayer overlay:
                    CheckImageLayer(overlay, "nakładka", context, fail, ProfileCatalog.OverlayField);
                    return;

                case ListPanelLayer list:
                    if (list.Recent < 0 || list.Recent > ListPanelLayer.MaxRecent)
                        fail($"ostatnie tytuły: od 0 do {ListPanelLayer.MaxRecent}");
                    if ((list.Hide ?? new List<string>()).Any(x => !ListPanelLayer.Parts.Contains(x)))
                        fail("nieznana część panelu anime lub mangi");
                    break;

                case CardsPanelLayer cards:
                    if ((cards.Hide ?? new List<string>()).Any(x => !CardsPanelLayer.Parts.Contains(x)))
                        fail("nieznana część karcianki");
                    break;

                case GalleryLayer gallery:
                    if (gallery.Count != 2 && gallery.Count != 6)
                        fail("galeria ma 2 albo 6 kart");
                    break;

                case StickerLayer sticker:
                    if (sticker.H < StickerLayer.MinHeight || sticker.H > StickerLayer.MaxHeight)
                        fail($"karta-naklejka: rozmiar od {StickerLayer.MinHeight} do {StickerLayer.MaxHeight} px");
                    if (Math.Abs(sticker.Tilt) > StickerLayer.MaxTilt)
                        fail($"karta-naklejka: pochylenie najwyżej {StickerLayer.MaxTilt}°");
                    if (!context.OwnsCard(sticker.Card))
                        fail("karta-naklejka: to nie twoja karta");
                    break;
            }

            var box = Box(layer, context);
            if (!Inside(box.X, box.Y, box.W, box.H))
                fail("element wychodzi poza obrazek");
        }

        private static void CheckAnimate(ProfileLayout layout, Action<string> fail)
        {
            var target = layout.Animate;
            if (target == null)
                return;

            var ok = target switch
            {
                ProfileLayout.AnimateAvatar => true,
                ProfileLayout.AnimateHeader => layout.Header != null,
                ProfileLayout.AnimateStyle => layout.Style != null,
                ProfileLayout.AnimateUltra => layout.Ultra != null,
                ProfileLayout.AnimateFrame => layout.Avatar.HasCustomFrame,
                _ => target.StartsWith(ProfileLayout.AnimateLayerPrefix) && int.TryParse(target.Substring(ProfileLayout.AnimateLayerPrefix.Length), out var id)
                    && layout.FindLayer(id) is StickerLayer or OverlayLayer or CardsPanelLayer or GalleryLayer or BigGalleryLayer,
            };
            if (!ok)
                fail("animowany może być awatar, obrazek, karta-naklejka albo panel z kartami");
        }

        // ---- unlocks ----

        // whether the panels, nick, rank and avatar stand where one ready layout puts them; then the layout is free
        public static bool IsReadyLayout(ProfileLayout layout)
            => ProfileCatalog.Presets.Any(p => SameStructure(layout, p.Make()));

        private static string KindOf(ProfileLayer layer) => layer switch
        {
            ListPanelLayer l => l.Manga ? "manga" : "anime",
            CardsPanelLayer or GalleryLayer or BigGalleryLayer or RankLayer => layer.Type,
            _ => null,
        };

        private static bool SameStructure(ProfileLayout layout, ProfileLayout preset)
        {
            if (layout.Avatar.X != preset.Avatar.X || layout.Avatar.Y != preset.Avatar.Y)
                return false;

            var n = layout.Nick;
            var pn = preset.Nick;
            if (n.X != pn.X || n.Y != pn.Y || n.W != pn.W || n.Align != pn.Align)
                return false;

            foreach (var layer in layout.Layers)
            {
                var kind = KindOf(layer);
                if (kind == null)
                    continue;

                var match = preset.Layers.FirstOrDefault(x => KindOf(x) == kind);
                if (match == null || match.X != layer.X || match.Y != layer.Y)
                    return false;
                if (layer is RankLayer rank && rank.Align != ((RankLayer)match).Align)
                    return false;
            }
            return true;
        }

        // every unlock the layout needs, whether the player has it or not
        public static SortedSet<string> RequiredUnlocks(ProfileLayout layout)
        {
            var needs = new SortedSet<string>(StringComparer.Ordinal);

            if (!IsReadyLayout(layout)) needs.Add(ProfileCatalog.Layout);
            if (layout.Accent != null) needs.Add(ProfileCatalog.Accent);
            if (layout.Panels != PanelLook.Solid) needs.Add(ProfileCatalog.PanelLook);
            if (layout.Bar.Hide?.Count > 0) needs.Add(ProfileCatalog.BarHide);
            if (layout.Bar.At != BarPlace.Bottom) needs.Add(ProfileCatalog.BarPlace);
            if (layout.Nick.Hidden) needs.Add(ProfileCatalog.NickHide);
            if (layout.Header != null) needs.Add(ProfileCatalog.HeaderImage);
            if (layout.Style != null) needs.Add(ProfileCatalog.StyleImage);
            if (layout.Ultra != null) needs.Add(ProfileCatalog.Ultra);

            var avatar = layout.Avatar;
            if (avatar.Size != ProfileCatalog.DefaultAvatarSize || (!avatar.HasFrame && avatar.Shape != AvatarShape.Circle))
                needs.Add(ProfileCatalog.AvatarLook);
            if (avatar.HasCustomFrame)
                needs.Add(ProfileCatalog.CustomFrame);
            else if (ProfileCatalog.TryParseFrame(avatar.Frame, out var border) && !ProfileCatalog.IsFreeFrame(border))
                needs.Add(ProfileCatalog.Frame(border));

            var lists = layout.LayersOf<ListPanelLayer>().ToList();
            if (lists.Any(x => x.Size == ListPanelSize.Large)) needs.Add(ProfileCatalog.LargeLists);
            if (lists.Any(x => x.Recent > 0)) needs.Add(ProfileCatalog.Recent);
            if (lists.Any(x => x.Hide?.Count > 0) || layout.LayersOf<CardsPanelLayer>().Any(x => x.Hide?.Count > 0))
                needs.Add(ProfileCatalog.PanelHide);

            var stickers = layout.LayersOf<StickerLayer>().Count();
            for (int i = 1; i <= Math.Min(stickers, ProfileCatalog.MaxStickers); i++) needs.Add(ProfileCatalog.Sticker(i));
            var overlays = layout.LayersOf<OverlayLayer>().Count();
            for (int i = 1; i <= Math.Min(overlays, ProfileCatalog.MaxOverlays); i++) needs.Add(ProfileCatalog.Overlay(i));

            return needs;
        }

        public static List<ProfileFeature> MissingUnlocks(ProfileLayout layout, IEnumerable<string> owned)
        {
            var have = new HashSet<string>(owned ?? Enumerable.Empty<string>());
            return RequiredUnlocks(layout).Where(x => !have.Contains(x)).Select(ProfileCatalog.Feature).Where(x => x != null).ToList();
        }

        public static long Cost(IEnumerable<ProfileFeature> features, Services.PocketWaifu.CurrencyType currency)
            => features.Sum(x => currency == Services.PocketWaifu.CurrencyType.TC ? x.Tc : x.Sc);
    }
}
