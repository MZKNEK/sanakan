using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Moq;
using Sanakan.Services.Commands;
using Sanakan.Services.PocketWaifu;
using Xunit;

namespace Artifacts
{
    public class CommandErrorsProbeModule : ModuleBase<ICommandContext>
    {
        [Command("liczba")]
        public Task RunAsync([Summary("nr pakietu kart")] int number) => Task.CompletedTask;

        [Command("gracz")]
        public Task RunAsync([Summary("nazwa użytkownika")] IUser user) => Task.CompletedTask;
    }

    public class CommandErrorsTests
    {
        private static async Task<ParseResult> ParseAsync(string input)
        {
            var service = new CommandService();
            await service.AddModuleAsync<CommandErrorsProbeModule>(null);

            var channel = new Mock<IMessageChannel>();
            channel.Setup(x => x.GetUsersAsync(It.IsAny<CacheMode>(), It.IsAny<RequestOptions>()))
                .Returns(AsyncEnumerable.Empty<IReadOnlyCollection<IUser>>());
            var context = new Mock<ICommandContext>();
            context.SetupGet(x => x.Channel).Returns(channel.Object);

            var search = service.Search(input);
            return await search.Commands[0].ParseAsync(context.Object, search, PreconditionResult.FromSuccess(), null);
        }

        [Fact]
        public async Task NumberParseFailure_NamesTheArgument()
        {
            var parse = await ParseAsync("liczba pierwszy");

            Assert.Equal(CommandError.ParseFailed, parse.Error);
            Assert.Equal("Failed to parse Int32.", parse.ErrorReason);
            Assert.Equal("w miejscu „nr pakietu kart” musi być liczba całkowita.",
                CommandErrors.Translate(parse.ErrorReason, parse.ErrorParameter?.Type, parse.ErrorParameter?.Summary));
        }

        [Fact]
        public async Task MissingUser_IsTranslated()
        {
            var parse = await ParseAsync("gracz Karnaa");

            Assert.Equal(CommandError.ObjectNotFound, parse.Error);
            Assert.Equal("nie znaleziono takiego użytkownika. Oznacz go albo podaj jego ID.",
                CommandErrors.Translate(parse.ErrorReason, parse.ErrorParameter?.Type, parse.ErrorParameter?.Summary));
        }

        [Theory]
        [InlineData("Channel not found.", "nie znaleziono takiego kanału.")]
        [InlineData("Role not found.", "nie znaleziono takiej roli.")]
        [InlineData("Message not found.", "nie znaleziono takiej wiadomości.")]
        [InlineData("Nie rozpoznano przedmiotu!", "Nie rozpoznano przedmiotu!")]
        public void Translate_KeepsPolishMessages(string reason, string expected)
        {
            Assert.Equal(expected, CommandErrors.Translate(reason, null, null));
        }

        [Theory]
        [InlineData(typeof(ulong), "w miejscu „id” musi być liczba całkowita nie mniejsza od zera.")]
        [InlineData(typeof(double), "w miejscu „id” musi być liczba.")]
        [InlineData(typeof(System.TimeSpan), "podano nieprawidłową wartość.")]
        public void Translate_DescribesTheExpectedValue(System.Type type, string expected)
        {
            Assert.Equal(expected, CommandErrors.Translate($"Failed to parse {type.Name}.", type, "id"));
        }

        [Fact]
        public void FormatArguments_NumbersAndMarksOptional()
        {
            Assert.Equal("**1.** nazwa użytkownika\n**2.** tag *(opcjonalnie)*",
                CommandErrors.FormatArguments(new[] { ("nazwa użytkownika", false), ("tag", true) }));
            Assert.Equal("", CommandErrors.FormatArguments(new (string, bool)[0]));
        }

        [Fact]
        public void SafariFailed_ExplainsThatTheCardFled()
        {
            var embed = Spawn.SafariFailed(new EmbedBuilder().WithImageUrl("https://x/safari.png"), "Na polowanie nie stawił się żaden łowca, karta uciekła.");

            Assert.Equal("🎯 Polowanie zakończone", embed.Title);
            Assert.Equal("Na polowanie nie stawił się żaden łowca, karta uciekła.", embed.Description);
            Assert.Equal("https://x/safari.png", embed.Image?.Url);
        }
    }
}
