using System.Collections.Generic;
using System.Linq;
using Sanakan;
using Sanakan.Services;
using Xunit;

namespace Artifacts
{
    public class DiscordChannelLoggerTests
    {
        [Fact]
        public void Pack_JoinsLinesIntoSingleCodeBlock()
        {
            var pending = new Queue<string>(new[] { "a", "b", "c" });

            var messages = DiscordChannelLogger.Pack(pending, null, 5);

            Assert.Single(messages);
            Assert.Equal("```ansi\na\nb\nc\n```", messages[0]);
            Assert.Empty(pending);
        }

        [Fact]
        public void Pack_SplitsMessagesAtLengthLimit()
        {
            var pending = new Queue<string>(Enumerable.Range(0, 50).Select(x => new string('x', 100)));

            var messages = DiscordChannelLogger.Pack(pending, null, 10);

            Assert.True(messages.Count > 1);
            Assert.All(messages, x => Assert.True(x.Length <= DiscordChannelLogger.MaxMessageLength));
            Assert.Empty(pending);
        }

        [Fact]
        public void Pack_LeavesRestInQueueWhenMessageLimitReached()
        {
            var pending = new Queue<string>(Enumerable.Range(0, 100).Select(x => new string('x', 500)));

            var messages = DiscordChannelLogger.Pack(pending, null, 2);

            Assert.Equal(2, messages.Count);
            Assert.NotEmpty(pending);
        }

        [Fact]
        public void Pack_HeaderGoesFirst()
        {
            var pending = new Queue<string>(new[] { "a" });

            var messages = DiscordChannelLogger.Pack(pending, "[skipped 3 entries]", 5);

            Assert.Equal("```ansi\n[skipped 3 entries]\na\n```", messages[0]);
        }

        [Fact]
        public void Pack_TooLongLineIsTruncated()
        {
            var pending = new Queue<string>(new[] { new string('x', 5000) });

            var messages = DiscordChannelLogger.Pack(pending, null, 5);

            Assert.Single(messages);
            Assert.True(messages[0].Length <= DiscordChannelLogger.MaxMessageLength);
            Assert.EndsWith("…\n```", messages[0]);
        }

        [Theory]
        [InlineData(LoggerExtensions.RoutinePrefix + "Exec: api-tc u1 (5) (12ms)")]
        [InlineData(LoggerExtensions.RoutinePrefix + "Cmd: u1 profil (40ms)")]
        [InlineData(LoggerExtensions.RoutinePrefix + "Discord: reconnected")]
        [InlineData("")]
        [InlineData(LoggerExtensions.RoutinePrefix)]
        public void ShouldSend_RoutineEntriesAreFiltered(string message)
        {
            Assert.False(DiscordChannelLogger.ShouldSend(message));
        }

        [Theory]
        [InlineData("API: site:shinden PUT /api/user/discord/1/tc -> 200 (15ms)")]
        [InlineData("API: u1 app:token POST /api/waifu/boosterpack/open/1 -> 503 (3ms)")]
        [InlineData(LoggerExtensions.ErrorPrefix + "Exec: api-tc u1 (5) (3ms) - System.NullReferenceException")]
        [InlineData(LoggerExtensions.ErrorPrefix + "Cmd: u1 harem (3ms) - System.NullReferenceException")]
        [InlineData("Error while sending request: timeout")]
        [InlineData("Request unsuccessful.")]
        [InlineData("Discord: disconnected, waiting for reconnect")]
        [InlineData("Discord: reconnect timeout, shutting down")]
        [InlineData("API: kill requested from web")]
        [InlineData("Bot: SIGTERM received")]
        public void ShouldSend_ErrorsAndApiEntriesPass(string message)
        {
            Assert.True(DiscordChannelLogger.ShouldSend(message));
        }

        [Fact]
        public void Sanitize_BreaksCodeBlockFence()
        {
            // DoesNotContain porównuje kulturowo i ignoruje zero-width space, discord nie
            Assert.False(DiscordChannelLogger.Sanitize("x ``` y", 100).Contains("```", System.StringComparison.Ordinal));
        }
    }
}
