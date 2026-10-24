using Sanakan.Services.Supervisor;
using Xunit;

namespace Artifacts
{
    public class ImageSpamThresholdTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ReportedSequence_PunishesFirstOversizedMessageAndCountsEveryScam(bool ban)
        {
            var time = new FixedTime();
            var entity = new SupervisorEntity(time);
            var penalty = ban ? "ban" : "mute";

            for (var message = 1; message <= 4; message++)
            {
                var decision = entity.TrackImageSpam(true, 3);

                Assert.True(decision.ShouldDelete);
                Assert.True(decision.ShouldPunish);
                Assert.False(decision.ShouldWarn);
                Assert.Equal(message, decision.OversizedMessages);
                Assert.Equal(message * 3, decision.ScamImages);
                Assert.Equal($"Automatyczny {penalty}: scamowe obrazki - rozpoznano {message * 3} scamowych obrazków w ciągu 5 minut.",
                    decision.GetPenaltyReason(ban));
                time.Value = time.Value.AddSeconds(3);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TwoScamsInOneMessage_ArePunishedRegardlessOfImageLimit(bool oversized)
        {
            var entity = new SupervisorEntity(new FixedTime());
            var decision = entity.TrackImageSpam(oversized, 2);

            Assert.True(decision.ShouldDelete);
            Assert.True(decision.ShouldPunish);
            Assert.False(decision.ShouldWarn);
            Assert.Equal(2, decision.ScamImages);
            Assert.Contains("scamowe obrazki", decision.GetPenaltyReason(false));
        }

        [Fact]
        public void SingleScams_WarnFirstAndPunishSecondMessage()
        {
            var entity = new SupervisorEntity(new FixedTime());
            var first = entity.TrackImageSpam(false, 1);

            Assert.True(first.ShouldDelete);
            Assert.True(first.ShouldWarn);
            Assert.False(first.ShouldPunish);
            Assert.Null(first.GetPenaltyReason(false));

            var second = entity.TrackImageSpam(false, 1);
            Assert.True(second.ShouldPunish);
            Assert.False(second.ShouldWarn);
            Assert.Equal(2, second.ScamImages);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void OversizedMessagesWithoutScams_WarnFirstAndPunishThird(bool ban)
        {
            var entity = new SupervisorEntity(new FixedTime());
            var first = entity.TrackImageSpam(true, 0);

            Assert.True(first.ShouldDelete);
            Assert.True(first.ShouldWarn);
            Assert.False(first.ShouldPunish);
            Assert.Null(first.GetPenaltyReason(ban));

            var second = entity.TrackImageSpam(true, 0);
            Assert.True(second.ShouldDelete);
            Assert.False(second.ShouldWarn);
            Assert.False(second.ShouldPunish);

            var third = entity.TrackImageSpam(true, 0);
            Assert.True(third.ShouldDelete);
            Assert.True(third.ShouldPunish);
            Assert.False(third.ShouldWarn);
            var penalty = ban ? "ban" : "mute";
            Assert.Equal($"Automatyczny {penalty}: spam obrazkami - wysłano 3 wiadomości zawierających więcej niż trzy obrazki w ciągu 2 minut.",
                third.GetPenaltyReason(ban));
        }

        [Fact]
        public void MixedViolations_KeepCountersIndependent()
        {
            var entity = new SupervisorEntity(new FixedTime());
            entity.TrackImageSpam(true, 0);

            var firstScam = entity.TrackImageSpam(false, 1);
            Assert.False(firstScam.ShouldPunish);
            Assert.Equal(1, firstScam.ScamImages);

            var secondOversized = entity.TrackImageSpam(true, 0);
            Assert.False(secondOversized.ShouldPunish);
            Assert.Equal(2, secondOversized.OversizedMessages);

            var secondScam = entity.TrackImageSpam(false, 1);
            Assert.True(secondScam.ShouldPunish);
            Assert.Equal(2, secondScam.ScamImages);
            Assert.Equal("Automatyczny mute: scamowe obrazki - rozpoznano 2 scamowych obrazków w ciągu 5 minut.",
                secondScam.GetPenaltyReason(false));
        }

        [Theory]
        [InlineData(1, "spam obrazkami")]
        [InlineData(2, "scamowe obrazki")]
        public void BothRulesApply_ReasonUsesScamOnlyWhenItsThresholdIsReached(int scams, string reason)
        {
            var entity = new SupervisorEntity(new FixedTime());
            entity.TrackImageSpam(true, 0);
            entity.TrackImageSpam(true, 0);

            var decision = entity.TrackImageSpam(true, scams);

            Assert.True(decision.ShouldPunish);
            Assert.False(decision.ShouldWarn);
            Assert.Equal(3, decision.OversizedMessages);
            Assert.Equal(scams, decision.ScamImages);
            Assert.Contains(reason, decision.GetPenaltyReason(false));
        }

        [Fact]
        public void ScamHistory_SurvivesMoreThanTwoMinutesOfSilence()
        {
            var time = new FixedTime();
            var entity = new SupervisorEntity(time);
            entity.TrackImageSpam(true, 1);

            time.Value = time.Value.AddMinutes(3);
            Assert.True(entity.IsValid());

            var decision = entity.TrackImageSpam(true, 1);
            Assert.True(decision.ShouldPunish);
            Assert.Equal(2, decision.ScamImages);
            Assert.Equal(1, decision.OversizedMessages);
        }

        [Theory]
        [InlineData(300, 2, true)]
        [InlineData(301, 1, false)]
        public void ScamWindow_IncludesFiveMinutesAndExpiresOlderHits(int seconds, int count, bool valid)
        {
            var time = new FixedTime();
            var entity = new SupervisorEntity(time);
            entity.TrackImageSpam(false, 1);

            time.Value = time.Value.AddSeconds(seconds);
            Assert.Equal(valid, entity.IsValid());

            var decision = entity.TrackImageSpam(false, 1);
            Assert.Equal(count, decision.ScamImages);
            Assert.Equal(valid, decision.ShouldPunish);
        }

        [Fact]
        public void ScamWindow_ExpiresOldHitsDespiteRepeatedViolations()
        {
            var time = new FixedTime();
            var start = time.Value;
            var entity = new SupervisorEntity(time);
            entity.TrackImageSpam(false, 3);
            time.Value = start.AddMinutes(3);
            entity.TrackImageSpam(false, 1);

            time.Value = start.AddMinutes(5).AddSeconds(1);
            var decision = entity.TrackImageSpam(false, 1);

            Assert.True(decision.ShouldPunish);
            Assert.Equal(2, decision.ScamImages);
        }

        [Fact]
        public void OversizedWindow_ExpiresOldMessagesDespiteRepeatedViolations()
        {
            var time = new FixedTime();
            var entity = new SupervisorEntity(time);
            entity.TrackImageSpam(true, 0);
            time.Value = time.Value.AddSeconds(90);
            entity.TrackImageSpam(true, 0);

            time.Value = time.Value.AddSeconds(90);
            var decision = entity.TrackImageSpam(true, 0);

            Assert.Equal(2, decision.OversizedMessages);
            Assert.False(decision.ShouldPunish);
        }

        [Theory]
        [InlineData(120, 2, true)]
        [InlineData(121, 1, false)]
        public void OversizedWindow_IncludesTwoMinutesAndExpiresOlderMessages(int seconds, int count, bool valid)
        {
            var time = new FixedTime();
            var entity = new SupervisorEntity(time);
            entity.TrackImageSpam(true, 0);

            time.Value = time.Value.AddSeconds(seconds);
            Assert.Equal(valid, entity.IsValid());
            Assert.Equal(count, entity.TrackImageSpam(true, 0).OversizedMessages);
        }

        [Fact]
        public void CleanMessages_DoNotExtendScamWindow()
        {
            var time = new FixedTime();
            var entity = new SupervisorEntity(time);
            entity.TrackImageSpam(false, 1);
            time.Value = time.Value.AddMinutes(4);
            entity.Inc();
            entity.TrackImageSpam(false, 0);

            time.Value = time.Value.AddMinutes(1).AddSeconds(1);
            Assert.True(entity.IsValid());
            var decision = entity.TrackImageSpam(false, 1);

            Assert.Equal(1, decision.ScamImages);
            Assert.False(decision.ShouldPunish);
            Assert.True(decision.ShouldWarn);
        }

        [Fact]
        public void OversizedWindow_KeepsEntityValidWithoutRegularMessages()
        {
            var time = new FixedTime();
            var start = time.Value;
            var entity = new SupervisorEntity(time);
            entity.TrackImageSpam(true, 0);
            time.Value = start.AddSeconds(90);
            entity.TrackImageSpam(true, 0);

            time.Value = start.AddSeconds(150);
            Assert.True(entity.IsValid());
            Assert.Equal(2, entity.TrackImageSpam(true, 0).OversizedMessages);

            time.Value = start.AddSeconds(271);
            Assert.False(entity.IsValid());
        }

        [Theory]
        [InlineData(true, false, true)]
        [InlineData(false, false, false)]
        [InlineData(true, true, true)]
        [InlineData(false, true, true)]
        public void ImageSpamPenalty_IsMuteOnUnsupervisedChannels(bool hasRole, bool unsupervised, bool mute)
        {
            Assert.Equal(mute, Supervisor.ShouldMuteForImageSpam(hasRole, unsupervised));
        }

        [Fact]
        public void CleanMessage_AfterPenalty_DoesNotTriggerImageModeration()
        {
            var entity = new SupervisorEntity(new FixedTime());
            entity.TrackImageSpam(true, 3);
            var decision = entity.TrackImageSpam(false, 0);

            Assert.False(decision.ShouldDelete);
            Assert.False(decision.ShouldWarn);
            Assert.False(decision.ShouldPunish);
            Assert.Null(decision.GetPenaltyReason(false));
        }
    }
}
