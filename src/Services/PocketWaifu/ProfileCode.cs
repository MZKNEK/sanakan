#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Sanakan.Database.Models;

namespace Sanakan.Services.PocketWaifu
{
    public enum ProfileImage
    {
        Background = 0,
        Style = 1,
        Overlay = 2,
        Premium = 3,
        BackgroundAndStyle = 4,
    }

    // Profile layout packed into a base64url string, so it can be saved, shared and applied at once.
    // Version 1 is the layout alone; version 2 adds links to the images, Discord's and the site's in a compact form.
    public class ProfileCode
    {
        private const byte LayoutVersion = 1;
        private const byte ImagesVersion = 2;
        private const int LayoutLength = 6;
        private const int MaxCodeLength = 2000;
        private const int MaxTextBytes = 1024;

        private const byte RawUrl = 0;
        private const byte DiscordUrl = 1;
        private const byte SiteUrl = 2;

        private const string SitePrefix = "https://sanakan.pl/";

        private static readonly Regex DiscordAttachment = new Regex(
            @"^https://cdn\.discordapp\.com/attachments/(\d{1,20})/(\d{1,20})/([^/?#]+)\?ex=([0-9a-f]{8})&is=([0-9a-f]{8})&hm=([0-9a-f]{64})&?$",
            RegexOptions.Compiled);

        private static readonly ProfileSettings KnownSettings = Enum.GetValues(typeof(ProfileSettings))
            .Cast<ProfileSettings>().Aggregate(ProfileSettings.None, (all, x) => all | x);

        public ProfileType Style { get; set; }
        public AvatarBorder Border { get; set; }
        public ProfileSettings Settings { get; set; }
        public int ShadowsPercent { get; set; }
        public Dictionary<ProfileImage, string> Images { get; } = new Dictionary<ProfileImage, string>();

        public bool HasStyleImage => Images.ContainsKey(ProfileImage.Style) || Images.ContainsKey(ProfileImage.BackgroundAndStyle);

        public static ProfileCode FromUser(User user) => new ProfileCode
        {
            Style = user.ProfileType,
            Border = user.AvatarBorder,
            Settings = user.StatsStyleSettings & KnownSettings,
            ShadowsPercent = Math.Clamp((int)Math.Round((1.0f - user.ProfileShadowsOpacity) * 100), 0, 100),
        };

        public string Encode()
        {
            using var data = new MemoryStream();
            var settings = (ushort)Settings;
            data.Write(new byte[] { Images.Count > 0 ? ImagesVersion : LayoutVersion, (byte)Style, (byte)Border,
                (byte)(settings & 0xFF), (byte)(settings >> 8), (byte)ShadowsPercent });

            foreach (var (slot, url) in Images.OrderBy(x => x.Key))
                WriteImage(data, slot, url);

            return Convert.ToBase64String(data.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        public static ProfileCode Decode(string code)
        {
            if (string.IsNullOrWhiteSpace(code) || code.Length > MaxCodeLength)
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

            if (data.Length < LayoutLength || (data[0] == LayoutVersion && data.Length != LayoutLength) || data[0] > ImagesVersion || data[0] < LayoutVersion)
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

            var reader = new Reader(data, LayoutLength);
            while (!reader.End)
            {
                var head = reader.Byte();
                if (head == null)
                    return null;

                var slot = (ProfileImage)(head >> 4);
                var url = ReadUrl(reader, (byte)(head & 0x0F));
                if (url == null || !Enum.IsDefined(typeof(ProfileImage), slot) || result.Images.ContainsKey(slot))
                    return null;

                result.Images[slot] = url;
            }

            if (data[0] == ImagesVersion && result.Images.Count == 0)
                return null;

            if (result.Images.ContainsKey(ProfileImage.BackgroundAndStyle)
                && (result.Images.ContainsKey(ProfileImage.Background) || result.Images.ContainsKey(ProfileImage.Style)))
                return null;

            return result;
        }

        private static void WriteImage(Stream data, ProfileImage slot, string url)
        {
            var discord = DiscordAttachment.Match(url);
            if (discord.Success && ulong.TryParse(discord.Groups[1].Value, out var channel) && ulong.TryParse(discord.Groups[2].Value, out var message))
            {
                data.WriteByte((byte)(((int)slot << 4) | DiscordUrl));
                WriteNumber(data, channel, 8);
                WriteNumber(data, message, 8);
                WriteText(data, discord.Groups[3].Value);
                WriteNumber(data, Convert.ToUInt32(discord.Groups[4].Value, 16), 4);
                WriteNumber(data, Convert.ToUInt32(discord.Groups[5].Value, 16), 4);
                data.Write(Convert.FromHexString(discord.Groups[6].Value));
                return;
            }

            if (url.StartsWith(SitePrefix, StringComparison.Ordinal))
            {
                data.WriteByte((byte)(((int)slot << 4) | SiteUrl));
                WriteText(data, url.Substring(SitePrefix.Length));
                return;
            }

            data.WriteByte((byte)(((int)slot << 4) | RawUrl));
            WriteText(data, url);
        }

        private static string ReadUrl(Reader reader, byte type)
        {
            string url;
            switch (type)
            {
                case DiscordUrl:
                {
                    var channel = reader.Number(8);
                    var message = reader.Number(8);
                    var name = reader.Text();
                    var ex = reader.Number(4);
                    var issued = reader.Number(4);
                    var hash = reader.Bytes(32);
                    if (channel == null || message == null || name == null || ex == null || issued == null || hash == null
                        || name.IndexOfAny(new[] { '/', '?', '#' }) >= 0)
                        return null;

                    url = $"https://cdn.discordapp.com/attachments/{channel}/{message}/{name}?ex={ex:x8}&is={issued:x8}&hm={Convert.ToHexString(hash).ToLowerInvariant()}&";
                }
                break;

                case SiteUrl:
                {
                    var path = reader.Text();
                    url = path == null ? null : SitePrefix + path;
                }
                break;

                case RawUrl:
                    url = reader.Text();
                    break;

                default:
                    return null;
            }

            return url != null && Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
                ? url : null;
        }

        private static void WriteNumber(Stream data, ulong value, int bytes)
        {
            for (int i = bytes - 1; i >= 0; i--)
                data.WriteByte((byte)(value >> (i * 8)));
        }

        // the length as a LEB128 number, then the UTF-8 bytes
        private static void WriteText(Stream data, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            var length = (uint)bytes.Length;
            do
            {
                var part = (byte)(length & 0x7F);
                length >>= 7;
                data.WriteByte(length > 0 ? (byte)(part | 0x80) : part);
            } while (length > 0);
            data.Write(bytes);
        }

        private class Reader
        {
            private readonly byte[] _data;
            private int _at;

            public Reader(byte[] data, int at)
            {
                _data = data;
                _at = at;
            }

            public bool End => _at >= _data.Length;

            public byte? Byte() => _at < _data.Length ? _data[_at++] : null;

            public byte[] Bytes(int count)
            {
                if (count < 0 || _at + count > _data.Length)
                    return null;

                var bytes = _data.AsSpan(_at, count).ToArray();
                _at += count;
                return bytes;
            }

            public ulong? Number(int bytes)
            {
                var raw = Bytes(bytes);
                return raw?.Aggregate(0UL, (value, b) => (value << 8) | b);
            }

            public string Text()
            {
                uint length = 0;
                for (int shift = 0; ; shift += 7)
                {
                    var b = Byte();
                    if (b == null || shift > 14)
                        return null;

                    length |= (uint)(b & 0x7F) << shift;
                    if ((b & 0x80) == 0)
                        break;
                }

                if (length == 0 || length > MaxTextBytes)
                    return null;

                var bytes = Bytes((int)length);
                if (bytes == null)
                    return null;

                try
                {
                    return new UTF8Encoding(false, true).GetString(bytes);
                }
                catch (ArgumentException)
                {
                    return null;
                }
            }
        }

        public bool StyleChanges(User user, bool styleAttachment) => user.ProfileType != Style || styleAttachment || HasStyleImage;

        public bool BorderChanges(User user) => user.AvatarBorder != Border;

        public bool ChangesAnything(User user, bool styleAttachment)
        {
            var current = FromUser(user);
            return StyleChanges(user, styleAttachment) || BorderChanges(user) || Images.Count > 0
                || current.Settings != Settings || current.ShadowsPercent != ShadowsPercent;
        }

        // null when the code can be applied, otherwise the reason
        public string Validate(User user, bool styleAttachment, Func<AvatarBorder, bool> canBuyBorder)
        {
            if (BorderChanges(user) && !canBuyBorder(Border))
                return "tej ramki awatara nie można ustawić kodem.";

            if (styleAttachment && HasStyleImage)
                return "kod ma już obrazek stylu, nie dołączaj innego.";

            var newStyleImage = styleAttachment || HasStyleImage;
            var needImage = ProfileConfig.StyleNeedsImage(Style);
            if (newStyleImage && !needImage)
                return "wybrany styl nie używa obrazka.";

            if (needImage && !newStyleImage && string.IsNullOrEmpty(user.StatsReplacementProfileUri))
                return "wybrany styl wymaga obrazka, dołącz go do polecenia.";

            return null;
        }

        // every image of the code is paid, as when it is set on its own; the background with the style replaces the style's price
        public List<(string What, CurrencyCost Cost)> GetCosts(User user, bool styleAttachment, CurrencyType currency)
        {
            var costs = new List<(string, CurrencyCost)>();
            if (Images.ContainsKey(ProfileImage.BackgroundAndStyle))
                costs.Add(("tło i styl", ProfileConfig.ToPay(ProfileConfigType.BackgroundAndStyle, currency)));
            else if (StyleChanges(user, styleAttachment))
                costs.Add(("styl", ProfileConfig.ToPay(ProfileConfigType.Style, currency)));

            if (Images.ContainsKey(ProfileImage.Background))
                costs.Add(("tło", ProfileConfig.ToPay(ProfileConfigType.Background, currency)));

            if (Images.ContainsKey(ProfileImage.Overlay))
                costs.Add(("nakładka", ProfileConfig.ToPay(ProfileConfigType.Overlay, currency)));

            if (Images.ContainsKey(ProfileImage.Premium))
                costs.Add(("ultra nakładka", ProfileConfig.ToPay(ProfileConfigType.PremiumOverlay, currency)));

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
