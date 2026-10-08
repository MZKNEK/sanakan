using System.Collections.Generic;
using System.Linq;
using Sanakan.Api.Models;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Xunit;

namespace Artifacts
{
    public class RichMessageFieldLimitTests
    {
        private static RichMessage WithFields(int count) => new RichMessage
        {
            Fields = Enumerable.Range(0, count).Select(i => new RichMessageField { Name = $"n{i}", Value = "v" }).ToList(),
        };

        [Fact]
        public void TwentyFiveFields_AreAllKept()
        {
            var embed = WithFields(25).ToEmbed();

            Assert.Equal(25, embed.Fields.Count());
        }

        [Fact]
        public void TooManyFields_AreTrimmedToLimit()
        {
            var embed = WithFields(40).ToEmbed();

            Assert.Equal(25, embed.Fields.Count());
        }
    }

    public class MonthlyMessageTests
    {
        [Fact]
        public void SendAnyMsgInMonth_HandlesUnderflow()
        {
            Assert.False(new User { MessagesCnt = 5, MessagesCntAtDate = 10 }.SendAnyMsgInMonth());
            Assert.True(new User { MessagesCnt = 10, MessagesCntAtDate = 5 }.SendAnyMsgInMonth());
            Assert.False(new User { MessagesCnt = 7, MessagesCntAtDate = 7 }.SendAnyMsgInMonth());
        }
    }
}
