#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.Linq;
using Sanakan.Database.Models;

namespace Sanakan.Services.PocketWaifu
{
    // Profile layout without images packed into a short base64url string, so it can be saved, shared and applied at once.
    public class ProfileCode
    {
        private const byte Version = 1;
        private const int Length = 6;

        private static readonly ProfileSettings KnownSettings = Enum.GetValues(typeof(ProfileSettings))
            .Cast<ProfileSettings>().Aggregate(ProfileSettings.None, (all, x) => all | x);

        public ProfileType Style { get; set; }
        public AvatarBorder Border { get; set; }
        public ProfileSettings Settings { get; set; }
        public int ShadowsPercent { get; set; }

        public static ProfileCode FromUser(User user) => new ProfileCode
        {
            Style = user.ProfileType,
            Border = user.AvatarBorder,
            Settings = user.StatsStyleSettings & KnownSettings,
            ShadowsPercent = Math.Clamp((int)Math.Round((1.0f - user.ProfileShadowsOpacity) * 100), 0, 100),
        };

        public string Encode()
        {
            var settings = (ushort)Settings;
            var data = new byte[] { Version, (byte)Style, (byte)Border, (byte)(settings & 0xFF), (byte)(settings >> 8), (byte)ShadowsPercent };
            return Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        public static ProfileCode Decode(string code)
        {
            if (string.IsNullOrWhiteSpace(code) || code.Length > 16)
                return null;

            byte[] data;
            try
            {
                var base64 = code.Trim().Replace('-', '+').Replace('_', '/');
                data = Convert.FromBase64String(base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '='));
            }
            catch (FormatException)
            {
                return null;
            }

            if (data.Length != Length || data[0] != Version)
                return null;

            var result = new ProfileCode
            {
                Style = (ProfileType)data[1],
                Border = (AvatarBorder)data[2],
                Settings = (ProfileSettings)(data[3] | (data[4] << 8)),
                ShadowsPercent = data[5],
            };

            if (!Enum.IsDefined(typeof(ProfileType), result.Style)
                || !Enum.IsDefined(typeof(AvatarBorder), result.Border)
                || (result.Settings & ~KnownSettings) != 0
                || result.ShadowsPercent > 100)
                return null;

            return result;
        }

        public bool StyleChanges(User user, bool newStyleImage) => user.ProfileType != Style || newStyleImage;

        public bool BorderChanges(User user) => user.AvatarBorder != Border;

        public bool ChangesAnything(User user, bool newStyleImage)
        {
            var current = FromUser(user);
            return StyleChanges(user, newStyleImage) || BorderChanges(user)
                || current.Settings != Settings || current.ShadowsPercent != ShadowsPercent;
        }

        // null when the code can be applied, otherwise the reason
        public string Validate(User user, bool newStyleImage, Func<AvatarBorder, bool> canBuyBorder)
        {
            if (BorderChanges(user) && !canBuyBorder(Border))
                return "tej ramki awatara nie można ustawić kodem.";

            var needImage = ProfileConfig.StyleNeedsImage(Style);
            if (newStyleImage && !needImage)
                return "wybrany styl nie używa obrazka.";

            if (needImage && !newStyleImage && string.IsNullOrEmpty(user.StatsReplacementProfileUri))
                return "wybrany styl wymaga obrazka, dołącz go do polecenia.";

            return null;
        }

        public List<(string What, CurrencyCost Cost)> GetCosts(User user, bool newStyleImage, CurrencyType currency)
        {
            var costs = new List<(string, CurrencyCost)>();
            if (StyleChanges(user, newStyleImage))
                costs.Add(("styl", ProfileConfig.ToPay(ProfileConfigType.Style, currency)));

            if (BorderChanges(user) && ProfileConfig.IsPaidBorder(Border))
                costs.Add(("ramka awatara", ProfileConfig.ToPay(ProfileConfigType.AvatarBorder, currency)));

            return costs;
        }

        public void ApplyTo(User user)
        {
            user.ProfileType = Style;
            user.AvatarBorder = Border;
            user.StatsStyleSettings = Settings;
            user.ProfileShadowsOpacity = 1.0f - (ShadowsPercent / 100.0f);
        }
    }
}
