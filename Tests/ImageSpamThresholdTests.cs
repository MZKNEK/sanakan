using System;
using Sanakan.Services.Supervisor;
using Xunit;

namespace Artifacts
{
    public class ImageSpamThresholdTests
    {
        [Theory]
        [InlineData(1, true, false)]
        [InlineData(2, true, true)]
        [InlineData(3, true, true)]
        [InlineData(1, false, false)]
        [InlineData(2, false, false)]
        [InlineData(3, false, true)]
        public void ShouldPunishImageSpam(int count, bool scamImage, bool expected)
            => Assert.Equal(expected, Supervisor.ShouldPunishImageSpam(count, scamImage));

        [Fact]
        public void IncImageSpam_AddsEveryHit()
        {
            var entity = new SupervisorEntity(new FixedTime());

            Assert.Equal(2, entity.IncImageSpam(2));
            Assert.Equal(3, entity.IncImageSpam());
        }

        [Fact]
        public void IncImageSpam_ResetsAfterTwoMinutes()
        {
            var time = new FixedTime();
            var entity = new SupervisorEntity(time);

            entity.IncImageSpam(3);
            time.Value = time.Value.AddMinutes(2).AddSeconds(1);

            Assert.Equal(1, entity.IncImageSpam());
        }
    }
}
