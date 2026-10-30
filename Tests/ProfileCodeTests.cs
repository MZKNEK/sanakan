using System.Linq;
using Sanakan.Database.Models;
using Sanakan.Services.PocketWaifu;
using Sanakan.TypeReaders;
using Xunit;

namespace Artifacts
{
    public class ProfileCodeTests
    {
        private static User NewUser() => new User
        {
            ProfileType = ProfileType.Stats,
            AvatarBorder = AvatarBorder.Base,
            StatsStyleSettings = ProfileSettings.Default,
            ProfileShadowsOpacity = 0.7f,
        };

        private static string Validate(ProfileCode code, User user, bool newImage = false)
            => code.Validate(user, newImage, ProfileConfigTypeReader.IsSelectableBorder);

        private const string DiscordLink = "https://cdn.discordapp.com/attachments/1234567890123456789/1300000000000000001/BG2.webp?ex=6724a1b2&is=67235032&hm=0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef&";
        private const string SiteLink = "https://sanakan.pl/i/u/abc123/Profilator/0f1e2d3c4b5a6978.webp";
        private const string OtherLink = "https://i.imgur.com/AbCdEf1.png";

        // made by Profilator's profile.js (sansite) from the same profile, so both read and write the same codes
        private const string SiteCode = "AgUOgAhGAREiEPR96YEVEgqHHMACAAEIQkcyLndlYnBnJKGyZyNQMgEjRWeJq83vASNFZ4mrze8BI0VniavN7wEjRWeJq83vEitpL3UvYWJjMTIzL1Byb2ZpbGF0b3IvMGYxZTJkM2M0YjVhNjk3OC53ZWJwIB9odHRwczovL2kuaW1ndXIuY29tL0FiQ2RFZjEucG5n";

        private static ProfileCode WithImages()
        {
            var code = ProfileCode.Decode("AQUOgAhG");
            code.Images[ProfileImage.Background] = DiscordLink;
            code.Images[ProfileImage.Style] = SiteLink;
            code.Images[ProfileImage.Overlay] = OtherLink;
            return code;
        }

        private static string Base64Url(params byte[] bytes)
            => System.Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        [Fact]
        public void Images_SameCodeAsProfilator()
        {
            Assert.Equal(SiteCode, WithImages().Encode());

            var decoded = ProfileCode.Decode(SiteCode);
            Assert.Equal(ProfileType.StatsOnImg, decoded.Style);
            Assert.Equal(DiscordLink, decoded.Images[ProfileImage.Background]);
            Assert.Equal(SiteLink, decoded.Images[ProfileImage.Style]);
            Assert.Equal(OtherLink, decoded.Images[ProfileImage.Overlay]);
            Assert.Equal(3, decoded.Images.Count);
        }

        [Fact]
        public void Images_DiscordLinkTakesHalfTheRoom()
        {
            var code = ProfileCode.Decode("AQUOgAhG");
            code.Images[ProfileImage.Background] = DiscordLink;

            Assert.True(code.Encode().Length < DiscordLink.Length * 0.6);
        }

        [Fact]
        public void Images_DiscordLinkWithoutTrailingAmpersand_ComesBackWithIt()
        {
            var code = ProfileCode.Decode("AQUOgAhG");
            code.Images[ProfileImage.Premium] = DiscordLink.TrimEnd('&');

            Assert.Equal(DiscordLink, ProfileCode.Decode(code.Encode()).Images[ProfileImage.Premium]);
        }

        [Fact]
        public void Images_WithoutThem_StaysVersionOne()
        {
            Assert.Equal("AQUOgAhG", ProfileCode.Decode("AQUOgAhG").Encode());
        }

        [Fact]
        public void Images_InvalidCodes_AreRefused()
        {
            var both = ProfileCode.Decode("AQUOgAhG");
            both.Images[ProfileImage.BackgroundAndStyle] = SiteLink;
            both.Images[ProfileImage.Background] = SiteLink;
            Assert.Null(ProfileCode.Decode(both.Encode()));

            var ftp = ProfileCode.Decode("AQUOgAhG");
            ftp.Images[ProfileImage.Overlay] = "ftp://example.com/a.png";
            Assert.Null(ProfileCode.Decode(ftp.Encode()));

            var layout = new byte[] { 2, 5, 14, 128, 8, 70 };
            Assert.Null(ProfileCode.Decode(Base64Url(layout)));
            Assert.Null(ProfileCode.Decode(Base64Url(layout.Concat(new byte[] { 0x03, 1, (byte)'a' }).ToArray())));
            Assert.Null(ProfileCode.Decode(Base64Url(layout.Concat(new byte[] { 0x50, 1, (byte)'a' }).ToArray())));
            Assert.Null(ProfileCode.Decode(Base64Url(new byte[] { 1, 5, 14, 128, 8, 70, 0x00, 1, (byte)'a' })));
            Assert.Null(ProfileCode.Decode(SiteCode.Substring(0, SiteCode.Length - 8)));

            var link = System.Text.Encoding.UTF8.GetBytes(SiteLink.Substring("https://sanakan.pl/".Length));
            var twice = layout.Concat(new byte[] { 0x22, (byte)link.Length }).Concat(link).Concat(new byte[] { 0x22, (byte)link.Length }).Concat(link).ToArray();
            Assert.Null(ProfileCode.Decode(Base64Url(twice)));
        }

        [Fact]
        public void Images_EveryImageIsPaid()
        {
            var user = NewUser();
            user.ProfileType = ProfileType.StatsOnImg;
            user.AvatarBorder = AvatarBorder.Red;
            user.StatsStyleSettings = ProfileSettings.ShowOverlay | ProfileSettings.BarOnTop;
            user.StatsReplacementProfileUri = "./SR1.webp";
            var code = ProfileCode.Decode("AQUOgAhG");
            code.Images[ProfileImage.Background] = SiteLink;
            code.Images[ProfileImage.Overlay] = SiteLink;
            code.Images[ProfileImage.Premium] = SiteLink;

            var costs = code.GetCosts(user, false, CurrencyType.SC);

            Assert.Equal(new[] { "tło", "nakładka", "ultra nakładka" }, costs.Select(x => x.What));
            Assert.Equal(5000 + 4000 + 120000, costs.Sum(x => x.Cost.Cost));
            Assert.True(code.ChangesAnything(user, false));
            Assert.Null(Validate(code, user));
        }

        [Fact]
        public void Images_StyleImageIsPaidAsStyle_TogetherAsBackgroundAndStyle()
        {
            var user = NewUser();
            user.ProfileType = ProfileType.Img;
            var code = ProfileCode.FromUser(user);
            code.Images[ProfileImage.Style] = SiteLink;
            Assert.Equal(new[] { "styl" }, code.GetCosts(user, false, CurrencyType.TC).Select(x => x.What));
            Assert.Equal(1000, code.GetCosts(user, false, CurrencyType.TC).Sum(x => x.Cost.Cost));

            var together = ProfileCode.FromUser(user);
            together.Images[ProfileImage.BackgroundAndStyle] = SiteLink;
            Assert.Equal(new[] { "tło i styl" }, together.GetCosts(user, false, CurrencyType.SC).Select(x => x.What));
            Assert.Equal(10000, together.GetCosts(user, false, CurrencyType.SC).Sum(x => x.Cost.Cost));
        }

        [Fact]
        public void Images_StyleImageRules()
        {
            var user = NewUser();
            var code = ProfileCode.FromUser(user);
            code.Style = ProfileType.StatsWithImg;
            code.Images[ProfileImage.BackgroundAndStyle] = SiteLink;

            Assert.Null(Validate(code, user));
            Assert.NotNull(Validate(code, user, true));

            code.Style = ProfileType.Stats;
            Assert.NotNull(Validate(code, user));
        }
        [Fact]
        public void Code_RoundTripsUserSettings()
        {
            var user = NewUser();
            user.ProfileType = ProfileType.MiniGalleryOnImg;
            user.AvatarBorder = AvatarBorder.Rainbow;
            user.StatsStyleSettings = ProfileSettings.Flip | ProfileSettings.ShowOverlayPro | ProfileSettings.BarOnTop;
            user.ProfileShadowsOpacity = 0.25f;

            var code = ProfileCode.Decode(ProfileCode.FromUser(user).Encode());

            Assert.Equal(ProfileType.MiniGalleryOnImg, code.Style);
            Assert.Equal(AvatarBorder.Rainbow, code.Border);
            Assert.Equal(user.StatsStyleSettings, code.Settings);
            Assert.Equal(75, code.ShadowsPercent);
            Assert.False(code.ChangesAnything(user, false));
        }

        [Fact]
        public void Code_IsShortAndUrlSafe()
        {
            var user = NewUser();
            user.StatsStyleSettings = (ProfileSettings)0x1FFF;
            user.AvatarBorder = AvatarBorder.TurqLeaves;

            var encoded = ProfileCode.FromUser(user).Encode();

            Assert.Equal(8, encoded.Length);
            Assert.DoesNotContain('+', encoded);
            Assert.DoesNotContain('/', encoded);
            Assert.DoesNotContain('=', encoded);
        }

        [Fact]
        public void HelpExample_IsValidCode()
        {
            var code = ProfileCode.Decode("AQUOgAhG");

            Assert.NotNull(code);
            Assert.Equal(ProfileType.StatsOnImg, code.Style);
            Assert.Equal(AvatarBorder.Red, code.Border);
            Assert.Equal(ProfileSettings.ShowOverlay | ProfileSettings.BarOnTop, code.Settings);
            Assert.Equal(70, code.ShadowsPercent);
        }

        [Theory]
        [InlineData("")]
        [InlineData("nie kod!")]
        [InlineData("AQUOgAhGAQUOgAhGAQUO")]
        [InlineData("AgUOgAhG")] // wrong version
        [InlineData("AQgOgAhG")] // unknown style
        [InlineData("AQVkgAhG")] // unknown border
        [InlineData("AQUOgCBG")] // unknown settings bit
        [InlineData("AQUOgAhl")] // shadows over 100
        [InlineData("AQUOgA")]   // too short
        public void Decode_InvalidCode_ReturnsNull(string code)
        {
            Assert.Null(ProfileCode.Decode(code));
        }

        [Fact]
        public void ApplyTo_SetsAllValues()
        {
            var user = NewUser();
            var code = new ProfileCode { Style = ProfileType.Cards, Border = AvatarBorder.Gold, Settings = ProfileSettings.RoundAvatar, ShadowsPercent = 40 };

            code.ApplyTo(user);

            Assert.Equal(ProfileType.Cards, user.ProfileType);
            Assert.Equal(AvatarBorder.Gold, user.AvatarBorder);
            Assert.Equal(ProfileSettings.RoundAvatar, user.StatsStyleSettings);
            Assert.Equal(0.6f, user.ProfileShadowsOpacity, 3);
            Assert.False(code.ChangesAnything(user, false));
        }

        [Fact]
        public void Costs_OnlyChangedStyleAndBorderArePaid()
        {
            var user = NewUser();

            var settingsOnly = new ProfileCode { Style = user.ProfileType, Border = user.AvatarBorder, Settings = ProfileSettings.None, ShadowsPercent = 0 };
            Assert.Empty(settingsOnly.GetCosts(user, false, CurrencyType.SC));
            Assert.True(settingsOnly.ChangesAnything(user, false));

            var both = new ProfileCode { Style = ProfileType.Cards, Border = AvatarBorder.Gold, Settings = user.StatsStyleSettings, ShadowsPercent = 30 };
            var costs = both.GetCosts(user, false, CurrencyType.SC);
            Assert.Equal(28000, costs.Sum(x => x.Cost.Cost));

            costs = both.GetCosts(user, false, CurrencyType.TC);
            Assert.Equal(9000, costs.Sum(x => x.Cost.Cost));
            Assert.All(costs, x => Assert.Equal(CurrencyType.TC, x.Cost.Type));
        }

        [Fact]
        public void Costs_FreeBorderChangeIsNotPaid()
        {
            var user = NewUser();
            user.AvatarBorder = AvatarBorder.Gold;
            var code = ProfileCode.FromUser(user);
            code.Border = AvatarBorder.None;

            Assert.Empty(code.GetCosts(user, false, CurrencyType.SC));
        }

        [Fact]
        public void Costs_SameStyleWithNewImageIsPaid()
        {
            var user = NewUser();
            user.ProfileType = ProfileType.Img;
            user.StatsReplacementProfileUri = "./SR1.webp";
            var code = ProfileCode.FromUser(user);

            Assert.Empty(code.GetCosts(user, false, CurrencyType.SC));
            Assert.Equal(3000, code.GetCosts(user, true, CurrencyType.SC).Sum(x => x.Cost.Cost));
            Assert.True(code.ChangesAnything(user, true));
        }

        [Fact]
        public void Validate_SpecialBorder_OnlyWhenAlreadyOwned()
        {
            var user = NewUser();
            var code = ProfileCode.FromUser(user);
            code.Border = AvatarBorder.TurqLeaves;

            Assert.NotNull(Validate(code, user));

            user.AvatarBorder = AvatarBorder.TurqLeaves;
            Assert.Null(Validate(code, user));
        }

        [Fact]
        public void Validate_StyleWithImage_RequiresExistingOrNewImage()
        {
            var user = NewUser();
            var code = ProfileCode.FromUser(user);
            code.Style = ProfileType.StatsWithImg;

            Assert.NotNull(Validate(code, user));
            Assert.Null(Validate(code, user, true));

            user.StatsReplacementProfileUri = "./SR1.webp";
            Assert.Null(Validate(code, user));
        }

        [Fact]
        public void Validate_ImageForStyleWithoutImage_IsRejected()
        {
            var user = NewUser();
            var code = ProfileCode.FromUser(user);

            Assert.NotNull(Validate(code, user, true));
        }

        [Theory]
        [InlineData(ProfileType.Stats, ProfileType.Stats, null, false, true)]
        [InlineData(ProfileType.Stats, ProfileType.Cards, null, true, false)]
        [InlineData(ProfileType.Img, ProfileType.Img, "https://example.com/a.png", true, false)]
        public void ProfileConfig_Style_PaysOnlyForChangeOrNewImage(ProfileType current, ProfileType wanted, string url, bool pay, bool nothing)
        {
            var user = NewUser();
            user.ProfileType = current;
            var config = new ProfileConfig { Type = ProfileConfigType.Style, Style = wanted, Url = url };

            Assert.Equal(pay, config.NeedPay(user));
            Assert.Equal(nothing, config.ChangesNothing(user));
        }

        [Theory]
        [InlineData(AvatarBorder.Gold, AvatarBorder.Gold, false, true)]
        [InlineData(AvatarBorder.Base, AvatarBorder.Gold, true, false)]
        [InlineData(AvatarBorder.Gold, AvatarBorder.None, false, false)]
        public void ProfileConfig_Border_PaysOnlyForNewPaidBorder(AvatarBorder current, AvatarBorder wanted, bool pay, bool nothing)
        {
            var user = NewUser();
            user.AvatarBorder = current;
            var config = new ProfileConfig { Type = ProfileConfigType.AvatarBorder, Border = wanted };

            Assert.Equal(pay, config.NeedPay(user));
            Assert.Equal(nothing, config.ChangesNothing(user));
        }

        [Theory]
        [InlineData(ProfileConfigType.Background)]
        [InlineData(ProfileConfigType.Overlay)]
        [InlineData(ProfileConfigType.PremiumOverlay)]
        [InlineData(ProfileConfigType.BackgroundAndStyle)]
        public void ProfileConfig_NewImage_IsAlwaysPaid(ProfileConfigType type)
        {
            var config = new ProfileConfig { Type = type, Style = ProfileType.Img, Url = "https://example.com/a.png" };

            Assert.True(config.NeedPay(NewUser()));
            Assert.False(config.ChangesNothing(NewUser()));
        }
    }
}
