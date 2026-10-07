#pragma warning disable 1591

using Sanakan.Api.Models;
using Sanakan.Config.Model;
using Sanakan.Services;
using Xunit;

namespace Artifacts
{
    public class ImageUrlSafetyTests
    {
        [Theory]
        [InlineData("http://127.0.0.1/x.png")]
        [InlineData("http://10.0.0.1/x.png")]
        [InlineData("http://192.168.1.1/x.png")]
        [InlineData("http://172.16.0.1/x.png")]
        [InlineData("http://169.254.169.254/latest/meta-data")]
        [InlineData("http://0.0.0.0/x.png")]
        [InlineData("http://localhost/x.png")]
        [InlineData("http://foo.local/x.png")]
        [InlineData("http://[::1]/x.png")]
        [InlineData("http://[fd00::1]/x.png")]
        [InlineData("file:///etc/passwd")]
        [InlineData("ftp://host/x.png")]
        public void IsPublicHttpUrl_BlocksInternalAndNonHttp(string url)
            => Assert.False(ImageProcessing.IsPublicHttpUrl(url));

        [Theory]
        [InlineData("https://sanakan.pl/i/x.png")]
        [InlineData("https://cdn.discordapp.com/x.png")]
        [InlineData("http://cdn.shinden.eu/cdn1/x.jpg")]
        [InlineData("https://i.imgur.com/x.png")]
        [InlineData("https://example.com/x")]
        public void IsPublicHttpUrl_AllowsPublicHosts(string url)
            => Assert.True(ImageProcessing.IsPublicHttpUrl(url));

        [Fact]
        public void RichMessageConfig_ToString_MasksWebhookSecret()
        {
            var config = new RichMessageConfig
            {
                WebHookUrl = "https://discord.com/api/webhooks/123456/VERYSECRETTOKEN",
                Type = RichMessageType.None,
            };

            var text = config.ToString();

            Assert.DoesNotContain("VERYSECRETTOKEN", text);
            Assert.Contains("discord.com", text);
        }
    }
}
