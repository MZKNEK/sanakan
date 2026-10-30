using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Newtonsoft.Json.Linq;
using Sanakan.Api;
using Sanakan.Config;
using Xunit;

namespace Artifacts
{
    public class BotWebHostTests
    {
        private readonly FakeConfig _config = new FakeConfig();
        private readonly ListLogger _logger = new ListLogger();

        private IServiceCollection BotServices() => new ServiceCollection()
            .AddSingleton<IConfig>(_config)
            .AddSingleton<Shinden.Logger.ILogger>(_logger)
            .AddSingleton(new Sanakan.Services.Helper(_config, _logger))
            .AddSingleton<Sanakan.Services.Fun>();

        [Fact]
        public void AddBotInstances_SharesOnlyReadyInstances()
        {
            var bot = BotServices();
            var api = new ServiceCollection();

            BotWebHost.AddBotInstances(api, bot);

            Assert.Equal(3, api.Count);
            Assert.All(api, x => Assert.NotNull(x.ImplementationInstance));
            Assert.DoesNotContain(api, x => x.ServiceType == typeof(Sanakan.Services.Fun));
        }

        [Fact]
        public async Task Host_ServesApiOnKestrel_WithBotInstances_AndLeavesShutdownToBot()
        {
            using var host = BotWebHost.CreateHost(_config, BotServices(), "http://127.0.0.1:0");
            await host.StartAsync();
            try
            {
                Assert.IsNotType<ConsoleLifetime>(host.Services.GetRequiredService<IHostLifetime>());
                Assert.Same(_logger, host.Services.GetRequiredService<Shinden.Logger.ILogger>());

                var address = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses.Single();
                using var client = new HttpClient { BaseAddress = new Uri(address) };

                var commands = JObject.Parse(await client.GetStringAsync("/api/info/commands"));
                Assert.NotNull(commands["modules"] ?? commands["Modules"]);
            }
            finally
            {
                await host.StopAsync();
            }
        }
    }
}
