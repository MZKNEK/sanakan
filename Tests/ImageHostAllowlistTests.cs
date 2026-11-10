#pragma warning disable 1591

using System.Threading.Tasks;
using Sanakan.Extensions;
using Sanakan.Services;
using Xunit;

namespace Artifacts
{
    public class ImageHostAllowlistTests
    {
        private static ImageProcessing CreateImageProcessing() => new ImageProcessing(null, null);

        [Theory]
        [InlineData("sanakan.pl")]
        [InlineData("waifu.sanakan.pl")]
        [InlineData("ALTER.Sanakan.PL")]
        [InlineData("sanakan.pl.")]
        public void Matches_DomainWithSubdomains(string host)
            => Assert.True(new DomainData("sanakan.pl", includeSubdomains: true).Matches(host));

        [Theory]
        [InlineData("notsanakan.pl")]
        [InlineData("sanakan.pl.evil.com")]
        [InlineData("")]
        [InlineData(null)]
        public void Matches_RejectsLookalikeHosts(string host)
            => Assert.False(new DomainData("sanakan.pl", includeSubdomains: true).Matches(host));

        [Fact]
        public void Matches_ExactOnlyRejectsSubdomain()
        {
            var domain = new DomainData("cdn.discordapp.com");
            Assert.True(domain.Matches("cdn.discordapp.com"));
            Assert.False(domain.Matches("x.cdn.discordapp.com"));
        }

        [Theory]
        [InlineData("https://imgur.com/x.png")]
        [InlineData("https://cdn.imgchest.com/x.png")]
        [InlineData("https://onedrive.live.com/x.png")]
        [InlineData("https://drive.google.com/x.png")]
        [InlineData("https://example.com/x.png")]
        public async Task CheckSiteImageUrl_RejectsOtherHosts(string url)
        {
            var result = await CreateImageProcessing().CheckSiteImageUrlAsync(url);
            Assert.Equal(ImageUrlCheckResult.BlacklistedHost, (ImageUrlCheckResult)result);
        }

        [Theory]
        [InlineData("https://sanakan.pl/x")]
        [InlineData("https://waifu.sanakan.pl/x")]
        [InlineData("https://i.imgur.com/x")]
        public async Task CheckSiteImageUrl_RequiresImageExtension(string url)
        {
            var result = await CreateImageProcessing().CheckSiteImageUrlAsync(url);
            Assert.Equal(ImageUrlCheckResult.WrongExtension, (ImageUrlCheckResult)result);
        }

        [Fact]
        public async Task CheckSiteImageUrl_ErrorListsAllowedHosts()
        {
            var result = await CreateImageProcessing().CheckSiteImageUrlAsync("https://example.com/x.png");
            var message = ExecutionResult.From(result).Message;

            foreach (var host in new[] { "sanakan.pl", "shinden.pl", "shinden.eu", "cdn.discordapp.com",
                "media.discordapp.net", "i.imgur.com", "www.dropbox.com", "dl.dropboxusercontent.com" })
                Assert.Contains(host, message);
        }

        [Theory]
        [InlineData("http://127.0.0.1/x.png")]
        [InlineData("http://10.0.0.1/x.png")]
        public async Task CheckImageUrl_DoesNotRestrictHost(string url)
        {
            // private addresses are refused before any request, so the host check is the only thing left to observe
            var result = await CreateImageProcessing().CheckImageUrlAsync(url);
            Assert.Equal(ImageUrlCheckResult.WrongExtension, (ImageUrlCheckResult)result);
        }

        [Fact]
        public void ExecutionResult_KeepsGenericMessagesForOtherErrors()
        {
            var message = ExecutionResult.From(ImageCheckResult.From(ImageUrlCheckResult.WrongExtension)).Message;
            Assert.Equal("Nie wykryto obrazka! Adres ma niedozwolone rozszerzenie!", message);
        }

        [Theory]
        [InlineData("https://cdn.discordapp.com/attachments/1/2/x.png", true)]
        [InlineData("https://media.discordapp.net/attachments/1/2/x.png", true)]
        [InlineData("https://cdn.shinden.eu/cdn1/x.jpg", true)]
        [InlineData("https://i.imgur.com/x.png", false)]
        [InlineData("https://drive.google.com/x", false)]
        [InlineData("not a url", false)]
        public void IsUrlFromHost_UsesDomainMatching(string url, bool expected)
        {
            var hosts = new[]
            {
                new DomainData("cdn.discordapp.com"),
                new DomainData("media.discordapp.net"),
                new DomainData("shinden.eu", includeSubdomains: true),
            };
            Assert.Equal(expected, CreateImageProcessing().IsUrlFromHost(url, hosts));
        }
    }
}
