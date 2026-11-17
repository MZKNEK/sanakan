using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Moq;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Xunit;
using FunModule = Sanakan.Modules.Fun;

namespace Artifacts
{
    public class ScGiftTests
    {
        private static async Task Give(WaifuCommandHarness harness, ulong from, ulong to, uint value)
        {
            var module = new FunModule(null, null, null, null, harness.Time) { Config = harness.Config, Logger = new ListLogger() };
            var channel = new Mock<ISocketMessageChannel>();
            channel.SetReturnsDefault(Task.FromResult(Mock.Of<IUserMessage>()));
            using var client = new DiscordSocketClient();
            var context = new SocketCommandContext(client, SocketTestObjects.Message(SocketTestObjects.User(from), ".cmd", channel.Object));
            typeof(ModuleBase<SocketCommandContext>).GetProperty("Context").SetValue(module, context);
            await module.GiveUserScAsync(SocketTestObjects.User(to), value);
        }

        private static async Task<WaifuCommandHarness> CreateAsync(long giverSc)
        {
            var harness = new WaifuCommandHarness();
            using var db = await harness.CreateAsync();
            var giver = new User().Default(91, harness.Time.Now());
            giver.ScCnt = giverSc;
            db.Users.AddRange(giver, new User().Default(92, harness.Time.Now()));
            await db.SaveChangesAsync();
            return harness;
        }

        [Fact]
        public async Task Gift_IsRecordedWithTheTax()
        {
            using var harness = await CreateAsync(5000);

            await Give(harness, 91, 92, 2000);

            using var db = harness.Open();
            var gift = Assert.Single(db.ScGifts);
            Assert.Equal((91ul, 92ul, 2000L, 1200L), (gift.FromUserId, gift.ToUserId, gift.Sent, gift.Received));
            Assert.Equal(harness.Time.Now(), gift.Date);
            Assert.Equal(1200, db.Users.Single(x => x.Id == 92).ScCnt - new User().Default(92, harness.Time.Now()).ScCnt);
        }

        [Fact]
        public async Task RefusedGift_IsNotRecorded()
        {
            using var harness = await CreateAsync(1500);

            await Give(harness, 91, 92, 2000);

            using var db = harness.Open();
            Assert.Empty(db.ScGifts);
        }
    }
}
