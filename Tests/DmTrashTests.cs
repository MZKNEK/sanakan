using Discord;
using Moq;
using Sanakan.Services;
using Xunit;

namespace Artifacts
{
    public class DmTrashTests
    {
        private const ulong Bot = 1;
        private const ulong User = 2;

        private static IMessage Msg(ulong authorId, IMessageChannel channel)
        {
            var author = new Mock<IUser>();
            author.Setup(x => x.Id).Returns(authorId);
            var msg = new Mock<IMessage>();
            msg.Setup(x => x.Author).Returns(author.Object);
            msg.Setup(x => x.Channel).Returns(channel);
            return msg.Object;
        }

        [Theory]
        [InlineData("\U0001F5D1")]
        [InlineData("\U0001F5D1️")]
        public void RecognizesWastebasket(string name)
        {
            Assert.True(DmTrash.IsTrash(new Emoji(name)));
        }

        [Fact]
        public void IgnoresOtherEmotes()
        {
            Assert.False(DmTrash.IsTrash(new Emoji("❌")));
            Assert.False(DmTrash.IsTrash(Emote.Parse("<:kosz:123456789012345678>")));
            Assert.False(DmTrash.IsTrash(null));
        }

        [Fact]
        public void DeletesOnlyBotMessagesInDm()
        {
            var dm = new Mock<IDMChannel>().Object;
            var guild = new Mock<ITextChannel>().Object;

            Assert.True(DmTrash.ShouldDelete(Msg(Bot, dm), Bot, User));
            Assert.False(DmTrash.ShouldDelete(Msg(User, dm), Bot, User));
            Assert.False(DmTrash.ShouldDelete(Msg(Bot, guild), Bot, User));
            Assert.False(DmTrash.ShouldDelete(Msg(Bot, dm), Bot, Bot));
            Assert.False(DmTrash.ShouldDelete(null, Bot, User));
        }
    }
}
