#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.Linq;
using Sanakan.Database.Models;

namespace Sanakan.Services.Profiles
{
    public class LegacyProfile
    {
        public ProfileLayout Layout { get; set; }
        public SortedSet<string> Unlocks { get; set; } = new SortedSet<string>(StringComparer.Ordinal);
    }

    // The profile set with the old commands as a layout of layers, looking as it did, with everything it uses unlocked.
    // The pictures go through registerImage (use, path or link -> picture id), which the caller stores.
    public static class ProfileLegacy
    {
        public static bool IsDefaultBackground(string uri)
            => string.IsNullOrEmpty(uri) || uri.EndsWith("np/pbg.png", StringComparison.OrdinalIgnoreCase);

        public static bool HasStyleImage(string uri)
            => !string.IsNullOrEmpty(uri) && !uri.Equals("none", StringComparison.OrdinalIgnoreCase);

        // whether anything differs from a fresh profile; such a profile needs no stored layout
        public static bool IsCustomised(User user)
            => !IsDefaultBackground(user.BackgroundProfileUri)
                || user.ProfileType != ProfileType.Stats
                || !string.IsNullOrEmpty(user.CustomProfileOverlayUrl)
                || !string.IsNullOrEmpty(user.PremiumCustomProfileOverlayUrl)
                || user.AvatarBorder != AvatarBorder.Base
                || user.StatsStyleSettings != ProfileSettings.Default;

        private static bool Has(User user, ProfileSettings flag) => user.StatsStyleSettings.HasFlag(flag);

        public static LegacyProfile FromUser(User user, ulong waifuCardId, Func<ProfileImageUse, string, long> registerImage)
        {
            var layout = new ProfileLayout();
            var result = new LegacyProfile { Layout = layout };
            var unlocks = result.Unlocks;
            int id = 1;
            T Add<T>(T layer) where T : ProfileLayer
            {
                layer.Id = id++;
                layout.Layers.Add(layer);
                return layer;
            }

            var type = user.ProfileType;
            var flip = Has(user, ProfileSettings.Flip);
            var onImage = type is ProfileType.Img or ProfileType.StatsOnImg or ProfileType.CardsOnImg or ProfileType.MiniGalleryOnImg;
            var imageOver = type == ProfileType.StatsWithImg;
            var styleImage = HasStyleImage(user.StatsReplacementProfileUri) && (onImage || imageOver);

            if (!IsDefaultBackground(user.BackgroundProfileUri))
            {
                // the old background was always stretched to exactly 750 x 160
                layout.Header = new SlotImage { Image = registerImage(ProfileImageUse.Header, user.BackgroundProfileUri), X = 0, Y = 0, Scale = 1, Fade = true };
                unlocks.Add(ProfileCatalog.HeaderImage);
            }

            if (styleImage && onImage)
            {
                layout.Style = new SlotImage { Image = registerImage(ProfileImageUse.Style, user.StatsReplacementProfileUri), X = 0, Y = 160, Scale = 1 };
                unlocks.Add(ProfileCatalog.StyleImage);
            }

            int lists = flip ? 403 : 16, cards = flip ? 16 : 368;
            switch (type)
            {
                case ProfileType.Stats:
                case ProfileType.StatsOnImg:
                case ProfileType.StatsWithImg:
                {
                    var y = 182;
                    if (Has(user, ProfileSettings.ShowAnime))
                    {
                        Add(new ListPanelLayer { X = lists, Y = y });
                        y += 146;
                    }
                    if (Has(user, ProfileSettings.ShowManga))
                        Add(new ListPanelLayer { X = lists, Y = y, Manga = true });
                    if (Has(user, ProfileSettings.ShowCards))
                        Add(new CardsPanelLayer { X = cards, Y = 182 });
                    break;
                }

                case ProfileType.MiniGallery:
                case ProfileType.MiniGalleryOnImg:
                    if (Has(user, ProfileSettings.ShowGallery))
                        Add(new GalleryLayer { X = lists, Y = 182, Count = Has(user, ProfileSettings.HalfGallery) ? 2 : 6, Panel = onImage });
                    if (Has(user, ProfileSettings.ShowCards))
                        Add(new CardsPanelLayer { X = cards, Y = 182 });
                    break;

                case ProfileType.Cards:
                case ProfileType.CardsOnImg:
                    Add(new BigGalleryLayer { X = 16, Y = 164, Panel = false });
                    break;
            }

            // only the first animated card of the lower part moved
            var moving = layout.Layers.FirstOrDefault(x => x is GalleryLayer or BigGalleryLayer) ?? layout.Layers.FirstOrDefault(x => x is CardsPanelLayer);
            if (moving != null)
                layout.Animate = ProfileLayout.AnimateLayer(moving.Id);

            if (onImage && type != ProfileType.Img && type != ProfileType.CardsOnImg)
            {
                layout.Panels = PanelLook.Shadow;
                unlocks.Add(ProfileCatalog.PanelLook);
            }
            layout.ShadowOpacity = Math.Clamp((int)Math.Round(user.ProfileShadowsOpacity * 100), 0, 100);

            int overlays = 0;
            if (styleImage && imageOver)
            {
                // "picture on the stats" drew the style picture over the panels, so it becomes an overlay there
                Add(new OverlayLayer { X = 0, Y = 160, Image = registerImage(ProfileImageUse.Overlay, user.StatsReplacementProfileUri) });
                unlocks.Add(ProfileCatalog.Overlay(++overlays));
            }

            if (!string.IsNullOrEmpty(user.CustomProfileOverlayUrl))
            {
                Add(new OverlayLayer { X = 0, Y = 98, Image = registerImage(ProfileImageUse.Overlay, user.CustomProfileOverlayUrl), Hidden = !Has(user, ProfileSettings.ShowOverlay) });
                unlocks.Add(ProfileCatalog.Overlay(++overlays));
            }

            if (Has(user, ProfileSettings.ShowWaifu) && waifuCardId != 0)
            {
                Add(new StickerLayer { X = 622, Y = 30, H = 120, Card = waifuCardId, Background = true });
                unlocks.Add(ProfileCatalog.Sticker(1));
            }

            Add(new RankLayer { X = 163, Y = 135 });

            if (!string.IsNullOrEmpty(user.PremiumCustomProfileOverlayUrl))
            {
                layout.Ultra = new UltraLayer { Id = id++, X = 0, Y = 0, Image = registerImage(ProfileImageUse.Ultra, user.PremiumCustomProfileOverlayUrl), Hidden = !Has(user, ProfileSettings.ShowOverlayPro) };
                unlocks.Add(ProfileCatalog.Ultra);
            }

            if (Has(user, ProfileSettings.BarOnTop))
            {
                layout.Bar.At = BarPlace.Top;
                unlocks.Add(ProfileCatalog.BarPlace);
            }
            if (Has(user, ProfileSettings.BarOpacity))
                layout.Bar.Opacity = Math.Clamp(layout.ShadowOpacity, 20, 100);

            var avatar = layout.Avatar;
            avatar.Frame = user.AvatarBorder.ToString();
            avatar.LevelFrame = Has(user, ProfileSettings.BorderColor);
            if (!ProfileCatalog.IsFreeFrame(user.AvatarBorder))
                unlocks.Add(ProfileCatalog.Frame(user.AvatarBorder));
            if (user.AvatarBorder == AvatarBorder.None && !Has(user, ProfileSettings.RoundAvatar))
            {
                // without a border and the round switch the avatar was square
                avatar.Shape = AvatarShape.Square;
                unlocks.Add(ProfileCatalog.AvatarLook);
            }

            // anything that does not stand where a ready layout puts it gets the own layout unlocked
            foreach (var needed in ProfileRules.RequiredUnlocks(layout))
                unlocks.Add(needed);

            return result;
        }
    }
}
