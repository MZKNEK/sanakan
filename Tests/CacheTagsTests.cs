#pragma warning disable 1591

using Sanakan;
using Xunit;

namespace Artifacts
{
    public class CacheTagsTests
    {
        [Fact]
        public void UserTag_Format() => Assert.Equal("user-123", CacheTags.User(123));

        [Fact]
        public void CharacterTag_Format() => Assert.Equal("character-45", CacheTags.Character(45));

        [Fact]
        public void GuildTag_Format() => Assert.Equal("config-67", CacheTags.Guild(67));

        [Fact]
        public void UserProfileTag_Format() => Assert.Equal("user-profile-89", CacheTags.UserProfile(89));

        [Fact]
        public void Constants_HaveExpectedValues()
        {
            Assert.Equal("mute", CacheTags.Mute);
            Assert.Equal("quiz", CacheTags.Quiz);
            Assert.Equal("ultimate-cards", CacheTags.UltimateCards);
            Assert.Equal("unique-cards", CacheTags.UniqueCards);
        }

        [Fact]
        public void TagsForTheSameId_DoNotCollide()
        {
            const ulong id = 999;

            Assert.NotEqual(CacheTags.User(id), CacheTags.Character(id));
            Assert.NotEqual(CacheTags.User(id), CacheTags.Guild(id));
            Assert.NotEqual(CacheTags.User(id), CacheTags.UserProfile(id));
            Assert.NotEqual(CacheTags.Character(id), CacheTags.Guild(id));
            Assert.NotEqual(CacheTags.Character(id), CacheTags.UserProfile(id));
        }

        [Fact]
        public void HandlesMaxUlong()
        {
            Assert.Equal("user-18446744073709551615", CacheTags.User(ulong.MaxValue));
            Assert.Equal("character-18446744073709551615", CacheTags.Character(ulong.MaxValue));
            Assert.Equal("config-18446744073709551615", CacheTags.Guild(ulong.MaxValue));
        }
    }
}
