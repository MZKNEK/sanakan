using System.Linq;
using System.Threading.Tasks;
using Discord.Commands;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services.Commands;
using Xunit;

namespace Artifacts
{
    public class CommandNamesTests
    {
        // Discord.NET builds each module once while registering it, only the names are tested here
        private sealed class StubServices : System.IServiceProvider
        {
            public object GetService(System.Type type) => type.IsInterface
                ? ((Moq.Mock)System.Activator.CreateInstance(typeof(Moq.Mock<>).MakeGenericType(type))).Object
                : System.Runtime.Serialization.FormatterServices.GetUninitializedObject(type);
        }

        private static async Task<CommandService> AllCommandsAsync()
        {
            var commands = new CommandService();
            var services = new StubServices();
            CommandHandler.AddTypeReaders(commands);
            await commands.AddModulesAsync(typeof(Sanakan.Modules.PocketWaifu).Assembly, services);
            await commands.AddModuleAsync<Sanakan.Modules.Moderation>(services);
            await commands.AddModuleAsync<Sanakan.Modules.Debug>(services);
            return commands;
        }

        [Theory]
        [InlineData("kartas 685", "kartas")]
        [InlineData("karta 685", "karta")]
        [InlineData("karta- 685", "karta-")]
        [InlineData("karcianka", "karcianka")]
        [InlineData("karciankas", "karcianka")]
        [InlineData("karcianka- Karna", "karcianka-")]
        public async Task LegacyAndNewCardCommands_ResolveOnlyToThemselves(string input, string expected)
        {
            var result = (await AllCommandsAsync()).Search(input);

            Assert.True(result.IsSuccess);
            Assert.All(result.Commands, x => Assert.Equal(expected, x.Command.Name));
        }

        [Theory]
        [InlineData("dev nieobecni", "nieobecni")]
        [InlineData("dev nieobecni usuń", "nieobecni usuń")]
        [InlineData("dev recpower", "recpower")]
        public async Task DevCommands_AreFound(string input, string expected)
        {
            var result = (await AllCommandsAsync()).Search(input);

            Assert.True(result.IsSuccess);
            Assert.Contains(result.Commands, x => x.Command.Name == expected);
        }

        [Fact]
        public async Task NoTwoCommandsShareANameOrAliasWithDifferentMeaning()
        {
            var all = (await AllCommandsAsync()).Commands
                .SelectMany(c => c.Aliases.Select(a => (Alias: a.ToLower(), Method: $"{c.Module.Name}.{c.Name}")))
                .GroupBy(x => x.Alias)
                .Where(g => g.Select(x => x.Method).Distinct().Count() > 1)
                .Select(g => g.Key)
                .ToList();

            Assert.DoesNotContain(all, x => x.StartsWith("kartas") || x.StartsWith("karciankas"));
        }

        private static Card Ultimate(int attackBonus, int defenceBonus, int healthBonus) => new Card
        {
            FromFigure = true,
            Rarity = Rarity.SSS,
            Quality = Quality.Omega,
            Attack = 100,
            Defence = 90,
            Health = 100,
            AttackBonus = attackBonus,
            DefenceBonus = defenceBonus,
            HealthBonus = healthBonus,
        };

        [Fact]
        public void Pills_StopAtTheStatLimit()
        {
            var maxed = Ultimate(9899, 9909, 99899);

            Assert.Equal(0, maxed.UsefulUltimatePills(ItemType.IncreaseUltimateAttack));
            Assert.Equal(0, maxed.UsefulUltimatePills(ItemType.IncreaseUltimateDefence));
            Assert.Equal(0, maxed.UsefulUltimatePills(ItemType.IncreaseUltimateHealth));
            Assert.Equal(0, maxed.UsefulUltimatePills(ItemType.IncreaseUltimateAll));
        }

        [Fact]
        public void Pills_CountWhatIsStillUseful()
        {
            var card = Ultimate(9889, 9909, 0);

            Assert.Equal(2, card.UsefulUltimatePills(ItemType.IncreaseUltimateAttack));
            Assert.Equal(0, card.UsefulUltimatePills(ItemType.IncreaseUltimateDefence));
            Assert.True(card.UsefulUltimatePills(ItemType.IncreaseUltimateAll) > 1000);
        }
    }
}
