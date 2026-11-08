using System;
using System.Net;
using Sanakan.Config.Model;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.ShindenCompat;
using Xunit;

namespace Artifacts
{
    public class ProfileMessagesTests
    {
        private static readonly DateTime Now = new DateTime(2026, 11, 8, 20, 0, 0, DateTimeKind.Local);

        [Fact]
        public void Subscription_ShowsEndTimeOrExpired()
        {
            var active = StatusType.Globals.NewTimeStatus(1);
            active.EndsAt = Now.AddDays(30);
            var ended = StatusType.Color.NewTimeStatus(1);
            ended.EndsAt = Now.AddDays(-1);

            Assert.Equal($"{StatusType.Globals.Name()}: do {active.EndsAt.ToShortDateTime()} ({active.EndsAt.ToRemTime()})", active.ToView(Now));
            Assert.Equal($"{StatusType.Color.Name()}: wygasła", ended.ToView(Now));
        }

        [Theory]
        [InlineData(HttpStatusCode.NotFound, "Brak wyników!")]
        [InlineData(HttpStatusCode.InternalServerError, "Brak połączenia z Shindenem! Spróbuj ponownie za chwilę.")]
        public void SearchError_HidesTheHttpCode(HttpStatusCode code, string expected)
        {
            var client = ShindenClient.Create(new ConfigShinden { Token = "t", ApiToken = "a", UserAgent = "tests", Marmolade = "m" }, null, null, TimeSpan.FromSeconds(1));
            var shinden = new Sanakan.Services.Shinden(client, null, null, new ListLogger(), new FixedTime());

            Assert.Equal(expected, shinden.GetResponseFromSearchCode(code));
        }
    }
}
