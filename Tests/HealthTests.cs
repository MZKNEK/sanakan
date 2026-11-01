using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Sanakan.Api;
using Sanakan.Services;
using Sanakan.ShindenCompat;
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
        private long _minute = 1000;
        private ShindenActivity Create() => new ShindenActivity(() => _minute);

        [Fact]
        public void MeasuresRequest()
        {
            var activity = Create();
            activity.RequestCompleted(HttpStatusCode.OK, 120);

            Assert.Equal(120, activity.Recent.Get());
            Assert.Equal(1, activity.Requests.Get(5).Total);
            Assert.Equal(0, activity.Requests.Get(5).Errors);
        }

        [Theory]
        [InlineData(HttpStatusCode.InternalServerError)]
        [InlineData(HttpStatusCode.ServiceUnavailable)]
        [InlineData(HttpStatusCode.Unauthorized)]
        [InlineData(HttpStatusCode.TooManyRequests)]
        public void FailedStatusCodeIsError(HttpStatusCode code)
        {
            var activity = Create();
            activity.RequestCompleted(code, 50);

            Assert.Equal(1, activity.Requests.Get(5).Errors);
            Assert.Null(activity.Recent.Get());
        }

        [Theory]
        [InlineData(HttpStatusCode.NotFound)]
        [InlineData(HttpStatusCode.BadRequest)]
        public void ClientStatusCodeIsNotError(HttpStatusCode code)
        {
            var activity = Create();
            activity.RequestCompleted(code, 50);

            Assert.Equal(0, activity.Requests.Get(5).Errors);
            Assert.Equal(50, activity.Recent.Get());
        }

        [Fact]
        public void TimeoutIsCountedAsErrorAndTimeout()
        {
            var activity = Create();
            activity.RequestCompleted(HttpStatusCode.OK, 40);
            activity.RequestFailed(true);

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
            activity.RequestFailed(false);

            Assert.Equal(1, activity.Requests.Get(5).Errors);
            Assert.Equal(0, activity.Timeouts.Get(5).Total);
            Assert.Null(activity.Recent.Get());
        }

        [Fact]
        public void ParsingErrorAfterSuccessfulResponseIsError()
        {
            var activity = Create();
            activity.RequestCompleted(HttpStatusCode.OK, 40);
            activity.ParsingFailed();

            Assert.Equal(1, activity.Requests.Get(5).Total);
            Assert.Equal(1, activity.Requests.Get(5).Errors);
            Assert.Null(activity.Recent.Get());
        }
    }

    public class ShindenActivityHandlerTests
    {
        private class StubHandler : HttpMessageHandler
        {
            public Func<HttpResponseMessage> Respond { get; set; } = () => new HttpResponseMessage(HttpStatusCode.OK);

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => Task.FromResult(Respond());
        }

        private readonly ShindenActivity _activity = new ShindenActivity(() => 1000);
        private readonly StubHandler _stub = new StubHandler();

        private Task<HttpResponseMessage> SendAsync()
        {
            using var client = new HttpClient(new ShindenActivityHandler(_activity, _stub), false);
            return client.GetAsync("http://shinden.test/api");
        }

        [Fact]
        public async Task TracksResponseCode()
        {
            await SendAsync();
            _stub.Respond = () => new HttpResponseMessage(HttpStatusCode.BadGateway);
            await SendAsync();

            var snap = _activity.Requests.Get(5);
            Assert.Equal(2, snap.Total);
            Assert.Equal(1, snap.Errors);
        }

        [Fact]
        public async Task CancellationIsTimeout()
        {
            _stub.Respond = () => throw new TaskCanceledException();

            await Assert.ThrowsAsync<TaskCanceledException>(SendAsync);
            Assert.Equal(1, _activity.Timeouts.Get(5).Total);
            Assert.Equal(1, _activity.Requests.Get(5).Errors);
        }

        [Fact]
        public async Task TransportErrorIsNotTimeout()
        {
            _stub.Respond = () => throw new HttpRequestException("connection refused");

            await Assert.ThrowsAsync<HttpRequestException>(SendAsync);
            Assert.Equal(0, _activity.Timeouts.Get(5).Total);
            Assert.Equal(1, _activity.Requests.Get(5).Errors);
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
