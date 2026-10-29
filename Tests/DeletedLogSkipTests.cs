using Sanakan.Services;
using Xunit;

namespace Artifacts
{
    public class DeletedLogSkipTests
    {
        [Fact]
        public void SkippedMessageIsConsumedOnce()
        {
            DeletedLog.Skip(424242);

            Assert.True(DeletedLog.ConsumeSkip(424242));
            Assert.False(DeletedLog.ConsumeSkip(424242));
        }

        [Fact]
        public void OtherMessagesAreNotSkipped()
        {
            DeletedLog.Skip(515151);

            Assert.False(DeletedLog.ConsumeSkip(515152));
            Assert.True(DeletedLog.ConsumeSkip(515151));
        }
    }
}
