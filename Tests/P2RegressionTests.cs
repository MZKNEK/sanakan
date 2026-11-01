using System.Collections.Generic;
using System.Linq;
using Sanakan.Api.Models;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Xunit;

namespace Artifacts
{
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
