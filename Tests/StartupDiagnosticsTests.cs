#pragma warning disable 1591

using System;
using Discord.Net;
using Sanakan.Services;
using Xunit;

namespace Artifacts
{
    public class StartupDiagnosticsTests
    {
        [Fact]
        public void DisallowedIntents_PointToTheDeveloperPortal()
        {
            var text = StartupDiagnostics.DescribeDisconnect(new Exception("WebSocket connection was closed", new WebSocketClosedException(4014, "Disallowed intent(s).")));

            Assert.Contains("close code 4014", text);
            Assert.Contains("Message Content Intent", text);
        }

        [Fact]
        public void SlowStep_NamesTheStepAndTime()
            => Assert.Equal("Start: database still running after 15 s", StartupDiagnostics.SlowStep("database", TimeSpan.FromSeconds(15.2)));

        [Fact]
        public void WrongToken_IsNamed()
            => Assert.Contains("BotToken", StartupDiagnostics.DescribeDisconnect(new WebSocketClosedException(4004, "Authentication failed.")));

        [Fact]
        public void OtherDisconnect_HasNoHint()
        {
            var text = StartupDiagnostics.DescribeDisconnect(new TimeoutException("no heartbeat"));

            Assert.Equal("Discord: disconnected before the bot was ready: no heartbeat", text);
            Assert.Null(StartupDiagnostics.GatewayCloseCode(new TimeoutException()));
        }
    }
}
