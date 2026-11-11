using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Moq;
using Sanakan.Services;
using Xunit;

namespace Artifacts
{
    public class DmPagesTests
    {
        private readonly List<TimeSpan> _waits = new List<TimeSpan>();

        private Helper NewHelper() => new Helper(new FakeConfig(), null)
        {
            Wait = delay =>
            {
                _waits.Add(delay);
                return Task.CompletedTask;
            }
        };

        private static (Mock<IUser> User, Mock<IDMChannel> Channel) Recipient()
        {
            var channel = new Mock<IDMChannel> { DefaultValue = DefaultValue.Mock };
            var user = new Mock<IUser>();
            user.Setup(x => x.CreateDMChannelAsync(It.IsAny<RequestOptions>())).ReturnsAsync(channel.Object);
            return (user, channel);
        }

        private static Embed[] Pages(int count)
            => Enumerable.Range(1, count).Select(x => new EmbedBuilder { Description = $"page {x}" }.Build()).ToArray();

        private static int Sent(Mock<IDMChannel> channel) => channel.Invocations.Count(x => x.Method.Name == "SendMessageAsync");

        [Fact]
        public async Task SinglePage_IsSentWithoutWaiting()
        {
            var (user, channel) = Recipient();

            var res = await NewHelper().SendEmbedsOnDMAsync(user.Object, Pages(1));

            Assert.True(res.IsOk());
            Assert.Equal(1, Sent(channel));
            Assert.Empty(_waits);
        }

        [Fact]
        public async Task SeveralPages_WaitOnlyBetweenThem()
        {
            var (user, channel) = Recipient();
            var helper = NewHelper();

            var res = await helper.SendEmbedsOnDMAsync(user.Object, Pages(3));
            await helper.LastPagesSending;

            Assert.True(res.IsOk());
            Assert.Equal(3, Sent(channel));
            Assert.Equal(new[] { Helper.DmPageDelay, Helper.DmPageDelay }, _waits);
        }

        [Fact]
        public void DefaultDelay_StaysUnderFiveMessagesPerFiveSeconds()
        {
            Assert.True(Helper.DmPageDelay >= TimeSpan.FromSeconds(1));
        }

        [Fact]
        public async Task Answer_ComesAfterTheFirstPage_TheRestFollows()
        {
            var (user, channel) = Recipient();
            // continuations run inline on SetResult, so the remaining pages do not wait for a thread pool thread on a busy CI runner
            var release = new TaskCompletionSource();
            var helper = new Helper(new FakeConfig(), null) { Wait = _ => release.Task };

            var res = await helper.SendEmbedsOnDMAsync(user.Object, Pages(3));

            Assert.True(res.IsOk());
            Assert.Equal(1, Sent(channel));
            release.SetResult();
            await helper.LastPagesSending.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(3, Sent(channel));
        }

        [Fact]
        public async Task FailureAfterTheFirstPage_IsOnlyLogged()
        {
            var (user, channel) = Recipient();
            var sent = 0;
            channel.Setup(x => x.SendMessageAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<Embed>(), It.IsAny<RequestOptions>(), It.IsAny<AllowedMentions>(),
                    It.IsAny<MessageReference>(), It.IsAny<MessageComponent>(), It.IsAny<ISticker[]>(), It.IsAny<Embed[]>(), It.IsAny<MessageFlags>(), It.IsAny<PollProperties>()))
                .Returns(() => ++sent == 1 ? Task.FromResult(Mock.Of<IUserMessage>()) : Task.FromException<IUserMessage>(new InvalidOperationException("gone")));
            var logger = new ListLogger();
            var helper = new Helper(new FakeConfig(), logger) { Wait = _ => Task.CompletedTask };

            var res = await helper.SendEmbedsOnDMAsync(user.Object, Pages(3));
            await helper.LastPagesSending;

            Assert.True(res.IsOk());
            Assert.Contains(logger.Messages, x => x.Contains("stopped after 1/3"));
        }

        [Fact]
        public async Task ManyPages_GoAsOneFileWithoutWaiting()
        {
            var (user, channel) = Recipient();

            var res = await NewHelper().SendEmbedsOnDMAsync(user.Object, Pages(30));

            Assert.True(res.IsOk());
            Assert.Equal(0, Sent(channel));
            Assert.Single(channel.Invocations, x => x.Method.Name == "SendFileAsync");
            Assert.Empty(_waits);
        }
    }
}
