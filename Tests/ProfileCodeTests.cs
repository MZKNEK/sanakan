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
