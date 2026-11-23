using Microsoft.AspNetCore.Mvc;
using Moq;
using Sanakan.Api.Controllers;
using Sanakan.Extensions;
using Xunit;

namespace Artifacts
{
    public class EmbedAvatarTests
    {
        private const ulong UserId = 123456789012345678;
        private const ulong GuildId = 245931283031523330;

        private sealed class TestController : UserController
        {
            private readonly Discord.IUser _user;
            public ulong? AskedGuild { get; private set; }

            public TestController(Discord.IUser user)
                : base(null, null, new ListLogger(), null, new FakeConfig(), new FixedTime(), null) => _user = user;

            protected override Discord.IUser FindCachedDiscordUser(ulong id, ulong? guild)
            {
                AskedGuild = guild;
                return _user?.Id == id ? _user : null;
            }
        }

        private static Mock<Discord.IGuildUser> Member(string avatarUrl)
        {
            var member = new Mock<Discord.IGuildUser>();
            member.SetupGet(x => x.Id).Returns(UserId);
            member.SetupGet(x => x.GuildId).Returns(GuildId);
            member.Setup(x => x.GetAvatarUrl(It.IsAny<Discord.ImageFormat>(), It.IsAny<ushort>())).Returns(avatarUrl);
            member.Setup(x => x.GetDefaultAvatarUrl()).Returns("https://cdn.discordapp.com/embed/avatars/1.png");
            return member;
        }

        [Fact]
        public void WithoutPublicApiUrl_LinksDiscordCdnAsBefore()
        {
            var member = Member("https://cdn.discordapp.com/avatars/1/abc.png?size=128");

            Assert.Equal("https://cdn.discordapp.com/avatars/1/abc.png",
                EmbedAuthorBuilderExtension.GetEmbedAvatarUrl(member.Object, true, null));
            Assert.Equal("https://cdn.discordapp.com/avatars/1/abc.png",
                EmbedAuthorBuilderExtension.GetEmbedAvatarUrl(member.Object, false, " "));
        }

        [Theory]
        [InlineData("https://api.example/", true, "https://api.example/api/user/discord/123456789012345678/avatar?guild=245931283031523330")]
        [InlineData("https://api.example", false, "https://api.example/api/user/discord/123456789012345678/avatar")]
        public void WithPublicApiUrl_LinksStableApiAddress(string baseUrl, bool fromGuild, string expected)
        {
            var member = Member("https://cdn.discordapp.com/avatars/1/abc.png?size=128");

            Assert.Equal(expected, EmbedAuthorBuilderExtension.GetEmbedAvatarUrl(member.Object, fromGuild, baseUrl));
        }

        [Fact]
        public void WithPublicApiUrl_UserOutsideGuild_HasNoGuildParameter()
        {
            var user = new Mock<Discord.IUser>();
            user.SetupGet(x => x.Id).Returns(UserId);

            Assert.Equal("https://api.example/api/user/discord/123456789012345678/avatar",
                EmbedAuthorBuilderExtension.GetEmbedAvatarUrl(user.Object, true, "https://api.example/"));
        }

        [Fact]
        public void Endpoint_RedirectsToCurrentAvatarWithoutSizeQuery()
        {
            var controller = ControllerHarness.Attach(new TestController(Member("https://cdn.discordapp.com/avatars/1/new.gif?size=128").Object));

            var result = Assert.IsType<RedirectResult>(controller.GetDiscordAvatar(UserId, GuildId));

            Assert.Equal("https://cdn.discordapp.com/avatars/1/new.gif", result.Url);
            Assert.False(result.Permanent);
            Assert.Equal(GuildId, controller.AskedGuild);
            Assert.Equal("public, max-age=3600", controller.Response.Headers.CacheControl.ToString());
        }

        [Fact]
        public void Endpoint_UserWithoutAvatar_RedirectsToTheirDefaultAvatar()
        {
            var controller = ControllerHarness.Attach(new TestController(Member(null).Object));

            var result = Assert.IsType<RedirectResult>(controller.GetDiscordAvatar(UserId, null));

            Assert.Equal("https://cdn.discordapp.com/embed/avatars/1.png", result.Url);
        }

        [Fact]
        public void Endpoint_UnknownUser_RedirectsToDefaultAvatarFromId()
        {
            var controller = ControllerHarness.Attach(new TestController(null));

            var result = Assert.IsType<RedirectResult>(controller.GetDiscordAvatar(UserId, null));

            Assert.Equal(Discord.CDN.GetDefaultUserAvatarUrl(UserId), result.Url);
            Assert.StartsWith("https://cdn.discordapp.com/embed/avatars/", result.Url);
        }

        [Fact]
        public void Endpoint_IsAnonymousGet()
        {
            var method = typeof(UserController).GetMethod(nameof(UserController.GetDiscordAvatar));

            Assert.NotEmpty(method.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute), true));
            var get = (HttpGetAttribute)Assert.Single(method.GetCustomAttributes(typeof(HttpGetAttribute), true));
            Assert.Equal("discord/{id}/avatar", get.Template);
        }
    }
}
