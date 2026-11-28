using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Sanakan.Api.Controllers;
using Sanakan.Api.Models;
using Sanakan.Config;
using Sanakan.Database.Models;
using Sanakan.Database.Models.Analytics;
using Sanakan.Database.Models.Configuration;
using Sanakan.Extensions;
using Sanakan.Preconditions;
using Xunit;

namespace Artifacts
{
    public class RevengeListTests : IDisposable
    {
        private const ulong Player = 10;
        private const ulong Guild = 98001;
        private const ulong DuelChannel = 501;

        private readonly WaifuCommandHarness _h = new WaifuCommandHarness();

        public void Dispose() => _h.Dispose();

        private sealed class Provider : IServiceProvider
        {
            private readonly IConfig _config;
            public Provider(IConfig config) => _config = config;
            public object GetService(Type serviceType) => serviceType == typeof(IConfig) ? _config : null;
        }

        private static ICommandContext Context(ulong channel)
        {
            var guild = new Mock<IGuild>();
            guild.SetupGet(x => x.Id).Returns(Guild);
            var messageChannel = new Mock<IMessageChannel>();
            messageChannel.SetupGet(x => x.Id).Returns(channel);
            var ctx = new Mock<ICommandContext>();
            ctx.SetupGet(x => x.Guild).Returns(guild.Object);
            ctx.SetupGet(x => x.Channel).Returns(messageChannel.Object);
            ctx.SetupGet(x => x.User).Returns(SocketTestObjects.User(Player));
            return ctx.Object;
        }

        [Theory]
        [InlineData(DuelChannel)]
        [InlineData(502UL)]
        public async Task List_WorksOnTheDuelChannelAndOnTheCommandChannel(ulong channel)
        {
            using (var db = await _h.CreateAsync())
            {
                var options = new GuildOptions { Id = Guild, WaifuConfig = new Sanakan.Database.Models.Configuration.Waifu { DuelChannel = DuelChannel } };
                options.CommandChannels.Add(new CommandChannel { Channel = 502 });
                options.WaifuConfig.CommandChannels.Add(new WaifuCommandChannel { Channel = 503 });
                db.Guilds.Add(options);
                await db.SaveChangesAsync();
            }

            var result = await new RequireAnyCommandOrDuelChannel().CheckPermissionsAsync(Context(channel), null, new Provider(_h.Config));

            Assert.True(result.IsSuccess, result.ErrorReason);
        }

        private PlayerController Controller(ulong caller)
        {
            var controller = ControllerHarness.Attach(new PlayerController(_h.Config, _h.Time, new FakeExecutor(), _h.Tags, null, null, _h.Shinden));
            controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("DiscordId", caller.ToString()) }, "test"));
            return controller;
        }

        [Fact]
        public async Task Api_ListsTodaysWinnersWithTheCommandNumbering()
        {
            var today = _h.Time.Now().Date;
            using (var db = await _h.CreateAsync())
            {
                db.Users.Add(new User().Default(Player, _h.Time.Now()));
                var withShinden = new User().Default(20, _h.Time.Now());
                withShinden.Shinden = 555;
                db.Users.Add(withShinden);
                db.PlayerNames.Add(new PlayerName { UserId = 30, Name = "stary nick", SeenAt = today });
                db.PvpDefeats.Add(new PvpDefeat { UserId = Player, AttackerId = 40, Date = today.AddHours(-2) });
                db.PvpDefeats.Add(new PvpDefeat { UserId = Player, AttackerId = 30, Date = today.AddHours(1) });
                db.PvpDefeats.Add(new PvpDefeat { UserId = Player, AttackerId = 20, Date = today.AddHours(2) });
                db.PvpDefeats.Add(new PvpDefeat { UserId = Player, AttackerId = 30, Date = today.AddHours(3), Avenged = true });
                db.PvpDefeats.Add(new PvpDefeat { UserId = 20, AttackerId = 50, Date = today.AddHours(1) });
                await db.SaveChangesAsync();
            }

            var list = (UserRevenges)Assert.IsType<OkObjectResult>(await Controller(Player).GetRevengeListAsync()).Value;

            Assert.True(list.Used);
            Assert.Equal(new[] { (1, "30", "stary nick", (string)null, 2, true), (2, "20", null, "555", 1, false) },
                list.Players.Select(x => (x.Number, x.DiscordId, x.Nick, x.ShindenId, x.Wins, x.Avenged)).ToArray());

            var other = (UserRevenges)Assert.IsType<OkObjectResult>(await Controller(20).GetRevengeListAsync()).Value;
            Assert.False(other.Used);
            Assert.Equal("50", Assert.Single(other.Players).DiscordId);
        }

        [Fact]
        public async Task Api_WithoutAProfile_IsNotFound()
        {
            using (await _h.CreateAsync()) { }

            Assert.Equal(404, (await Controller(Player).GetRevengeListAsync()).StatusOf());
        }
    }
}
