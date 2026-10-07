using Discord;
using Sanakan.Api;
using Sanakan.Services;
using Xunit;

namespace Artifacts
{
    public class MinuteStatsTests
    {
        private long _minute = 1000;
        private MinuteStats Create() => new MinuteStats(() => _minute);

        [Fact]
        public void CountsTotalAndErrorsInsideWindow()
        {
            var stats = Create();
            stats.Add(false);
            stats.Add(true);
            stats.AddError();

            var snap = stats.Get(5);
            Assert.Equal(2, snap.Total);
            Assert.Equal(2, snap.Errors);
            Assert.Equal(100, snap.ErrorRate);
        }

        [Fact]
        public void EmptyStatsAreZero()
        {
            var snap = Create().Get(60);
            Assert.Equal(0, snap.Total);
            Assert.Equal(0, snap.ErrorRate);
            Assert.Equal(0, snap.AvgMs);
        }

        [Fact]
        public void OldMinutesFallOutOfWindow()
        {
            var stats = Create();
            stats.Add(false);
            _minute += 4;
            stats.Add(false);

            Assert.Equal(2, stats.Get(5).Total);

            _minute += 1;
            Assert.Equal(1, stats.Get(5).Total);
            Assert.Equal(2, stats.Get(60).Total);
        }

        [Fact]
        public void BucketIsResetWhenReusedAfterAnHour()
        {
            var stats = Create();
            stats.Add(true);
            _minute += 60;
            stats.Add(false);

            var snap = stats.Get(60);
            Assert.Equal(1, snap.Total);
            Assert.Equal(0, snap.Errors);
        }

        [Fact]
        public void TracksAverageAndMaxOfTimedEntries()
        {
            var stats = Create();
            stats.AddTimed(10);
            stats.AddTimed(30);
            stats.Add(true);
            _minute += 1;
            stats.AddTimed(80);

            var snap = stats.Get(5);
            Assert.Equal(4, snap.Total);
            Assert.Equal(3, snap.Timed);
            Assert.Equal(40, snap.AvgMs);
            Assert.Equal(80, snap.MaxMs);
            Assert.Equal(25, snap.ErrorRate);
        }
    }

    public class RecentLatencyTests
    {
        private long _minute = 1000;
        private RecentLatency Create() => new RecentLatency(() => _minute);

        [Fact]
        public void NullWithoutAnySuccess() => Assert.Null(Create().Get());

        [Fact]
        public void ReturnsShortestInCurrentMinute()
        {
            var recent = Create();
            recent.Success(50);
            recent.Success(5);
            recent.Success(20);

            Assert.Equal(5, recent.Get());
        }

        [Fact]
        public void NullInNextMinuteUntilNewSuccess()
        {
            var recent = Create();
            recent.Success(5);
            _minute += 1;

            Assert.Null(recent.Get());

            recent.Success(40);
            Assert.Equal(40, recent.Get());
        }

        [Fact]
        public void FailureHidesResultUntilNextSuccess()
        {
            var recent = Create();
            recent.Success(5);
            recent.Failure();

            Assert.Null(recent.Get());

            recent.Success(30);
            Assert.Equal(5, recent.Get());
        }

        [Fact]
        public void FailedWithinHoldsForGivenMinutes()
        {
            var recent = Create();
            Assert.False(recent.FailedWithin(2));

            recent.Failure();
            Assert.True(recent.FailedWithin(2));
            _minute += 1;
            Assert.True(recent.FailedWithin(2));
            Assert.False(recent.FailedWithin(1));
            _minute += 1;
            Assert.False(recent.FailedWithin(2));
        }

        [Fact]
        public void SuccessClearsFailedWithin()
        {
            var recent = Create();
            recent.Failure();
            recent.Success(10);

            Assert.False(recent.FailedWithin(2));
        }

        [Fact]
        public void SuccessWithoutMeasurementUsesLastKnownLatency()
        {
            var recent = Create();
            recent.Success(70);
            _minute += 1;
            recent.Success(null);

            Assert.Equal(70, recent.Get());
        }

        [Fact]
        public void SuccessWithoutAnyMeasurementIsNull()
        {
            var recent = Create();
            recent.Success(null);

            Assert.Null(recent.Get());
        }
    }

    public class ShindenActivityTests
    {
        private long _now = 1_000_000;
        private long _minute = 1000;
        private ShindenActivity Create() => new ShindenActivity(() => _now, () => _minute);

        private void Request(ShindenActivity activity, long durationMs, string response = "Response code: OK")
        {
            activity.Track("Processing request: [GET] https://shinden/api");
            _now += durationMs;
            activity.Track(response);
            activity.Track("Request successful.");
        }

        [Fact]
        public void MeasuresSoloRequest()
        {
            var activity = Create();
            Request(activity, 120);

            Assert.Equal(120, activity.Recent.Get());
            Assert.Equal(1, activity.Requests.Get(5).Total);
            Assert.Equal(0, activity.Requests.Get(5).Errors);
        }

        [Fact]
        public void OverlappingRequestsAreNotMeasured()
        {
            var activity = Create();
            activity.Track("Processing request: [GET] a");
            _now += 10;
            activity.Track("Processing request: [GET] b");
            _now += 300;
            activity.Track("Response code: OK");
            activity.Track("Response code: OK");

            Assert.Null(activity.Recent.Get());
            Assert.Equal(2, activity.Requests.Get(5).Total);

            Request(activity, 90);
            Assert.Equal(90, activity.Recent.Get());
        }

        [Theory]
        [InlineData("Response code: InternalServerError")]
        [InlineData("Response code: 503")]
        [InlineData("Response code: Unauthorized")]
        [InlineData("Response code: TooManyRequests")]
        public void FailedStatusCodeIsError(string response)
        {
            var activity = Create();
            Request(activity, 50, response);

            Assert.Equal(1, activity.Requests.Get(5).Errors);
            Assert.Null(activity.Recent.Get());
        }

        [Theory]
        [InlineData("Response code: NotFound")]
        [InlineData("Response code: 404")]
        [InlineData("Response code: BadRequest")]
        public void ClientStatusCodeIsNotError(string response)
        {
            var activity = Create();
            Request(activity, 50, response);

            Assert.Equal(0, activity.Requests.Get(5).Errors);
            Assert.Equal(50, activity.Recent.Get());
        }

        [Fact]
        public void TimeoutIsCountedAsErrorAndTimeout()
        {
            var activity = Create();
            Request(activity, 40);
            activity.Track("Processing request: [GET] https://shinden/api");
            _now += 10_000;
            activity.Track("Timeout while sending request: [GET] https://shinden/api");

            var snap = activity.Requests.Get(5);
            Assert.Equal(2, snap.Total);
            Assert.Equal(1, snap.Errors);
            Assert.Equal(50, snap.ErrorRate);
            Assert.Equal(1, activity.Timeouts.Get(5).Total);
            Assert.Null(activity.Recent.Get());
        }

        [Fact]
        public void ConnectionErrorIsError()
        {
            var activity = Create();
            activity.Track("Processing request: [GET] https://shinden/api");
            activity.Track("Error while sending request: connection refused");

            Assert.Equal(1, activity.Requests.Get(5).Errors);
            Assert.Equal(0, activity.Timeouts.Get(5).Total);
            Assert.Null(activity.Recent.Get());
        }

        [Fact]
        public void ParsingErrorAfterSuccessfulResponseIsError()
        {
            var activity = Create();
            Request(activity, 40);
            activity.Track("In parsing: Unexpected character");

            Assert.Equal(1, activity.Requests.Get(5).Errors);
            Assert.Null(activity.Recent.Get());
        }

        [Fact]
        public void LostCompletionIsForgottenAfterStaleTimeout()
        {
            var activity = Create();
            activity.Track("Processing request: [GET] lost");
            _now += 31_000;

            Request(activity, 60);
            Assert.Equal(60, activity.Recent.Get());
        }

        [Fact]
        public void IgnoresUnrelatedMessages()
        {
            var activity = Create();
            activity.Track(null);
            activity.Track("Parsing response.");
            activity.Track("Response body: {}");

            Assert.Equal(0, activity.Requests.Get(5).Total);
            Assert.Null(activity.Recent.Get());
        }
    }

    public class HealthStatusTests
    {
        [Fact]
        public void OkWhenEverythingWorks()
            => Assert.Equal(HealthMonitor.Ok, HealthMonitor.GetStatus(ConnectionState.Connected, 50, true, true, 0));

        [Theory]
        [InlineData(ConnectionState.Disconnected)]
        [InlineData(ConnectionState.Connecting)]
        [InlineData(ConnectionState.Disconnecting)]
        public void DownWhenNotConnectedEvenIfDependenciesFail(ConnectionState state)
            => Assert.Equal(HealthMonitor.Down, HealthMonitor.GetStatus(state, 5000, false, false, 10));

        [Theory]
        [InlineData(50, false, true, 0)]
        [InlineData(50, true, false, 0)]
        [InlineData(1001, true, true, 0)]
        [InlineData(50, true, true, 1)]
        public void DegradedWhenSomethingIsWrong(int latency, bool db, bool shinden, int rejected)
            => Assert.Equal(HealthMonitor.Degraded, HealthMonitor.GetStatus(ConnectionState.Connected, latency, db, shinden, rejected));

        [Fact]
        public void LatencyAtThresholdIsStillOk()
            => Assert.Equal(HealthMonitor.Ok, HealthMonitor.GetStatus(ConnectionState.Connected, 1000, true, true, 0));
    }
}
