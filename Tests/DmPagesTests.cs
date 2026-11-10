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

            var res = await NewHelper().SendEmbedsOnDMAsync(user.Object, Pages(3));

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
        public async Task ManyPages_GoAsOneFileWithoutWaiting()
        {
            var (user, channel) = Recipient();

            var res = await NewHelper().SendEmbedsOnDMAsync(user.Object, Pages(30));

            Assert.True(res.IsOk());
            Assert.Equal(0, Sent(channel));
            Assert.Single(channel.Invocations.Where(x => x.Method.Name == "SendFileAsync"));
            Assert.Empty(_waits);
        }
    }
}
