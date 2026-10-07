using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Moq;
using Sanakan.Database.Models;
using Sanakan.Services.PocketWaifu;
using Sanakan.TypeReaders;
using Xunit;

namespace Artifacts
{
    public class ProfileConfigTypeReaderTests
    {
        private static ICommandContext Context(params IAttachment[] attachments)
        {
            var message = new Mock<IUserMessage>();
            message.SetupGet(x => x.Attachments).Returns(attachments.ToList());

            var context = new Mock<ICommandContext>();
            context.SetupGet(x => x.Message).Returns(message.Object);
            return context.Object;
        }

        private static TypeReaderResult Read(string input, params IAttachment[] attachments)
            => new ProfileConfigTypeReader().ReadAsync(Context(attachments), input, null).GetAwaiter().GetResult();

        private static ProfileConfig Value(TypeReaderResult result)
            => (ProfileConfig)result.Values.First().Value;

        // --- info ---

        [Fact]
        public void Info_WithoutArgument_ReturnsSuccess()
        {
            var result = Read("info");

            Assert.True(result.IsSuccess);
            Assert.Equal(ProfileConfigType.ShowInfo, Value(result).Type);
            Assert.Equal(0, Value(result).Value);
        }

        [Fact]
        public void Info_WithOptionNumber_ReturnsChosenOption()
        {
            var result = Read("info 5");

            Assert.True(result.IsSuccess);
            Assert.Equal(5, Value(result).Value);
        }

        [Fact]
        public void Info_WithZero_ClampsToOne()
        {
            Assert.True(Read("info 0").IsSuccess);
            Assert.Equal(1, Value(Read("info 0")).Value);
        }

        [Fact]
        public void Info_WithGarbage_FallsBackToDefault()
        {
            var result = Read("info abc");

            Assert.True(result.IsSuccess);
            Assert.Equal(0, Value(result).Value);
        }

        // --- białe znaki / niekompletne wielowyrazowe opcje (regresja: IndexOutOfRangeException) ---

        [Theory]
        [InlineData("styl ")]
        [InlineData("style ")]
        [InlineData("jestem leniwy ")]
        [InlineData("ramka awatara ")]
        [InlineData("avatar border ")]
        public void TrailingWhitespace_DoesNotThrow(string input)
        {
            var result = Read(input);

            Assert.False(result.IsSuccess);
            Assert.Equal(CommandError.ParseFailed, result.Error);
        }

        [Fact]
        public void Style_WithoutArgument_ReturnsParseFailed()
        {
            var result = Read("styl");

            Assert.False(result.IsSuccess);
            Assert.Equal(CommandError.ParseFailed, result.Error);
        }

        [Fact]
        public void AvatarBorder_WithoutArgument_ReturnsParseFailed()
        {
            var result = Read("ramka awatara");

            Assert.False(result.IsSuccess);
            Assert.Equal(CommandError.ParseFailed, result.Error);
        }

        // --- style ---

        [Theory]
        [InlineData("styl statystyki", ProfileType.Stats)]
        [InlineData("styl 1", ProfileType.Stats)]
        [InlineData("styl duża galeria", ProfileType.Cards)]
        [InlineData("styl 4", ProfileType.Cards)]
        public void Style_WithNamedOrIndexedValue_IsParsed(string input, ProfileType expected)
        {
            var result = Read(input);

            Assert.True(result.IsSuccess);
            Assert.Equal(expected, Value(result).Style);
        }

        [Fact]
        public void Style_NeedingUrl_WithoutUrl_ReturnsParseFailed()
        {
            Assert.False(Read("styl obrazek").IsSuccess);
        }

        [Fact]
        public void Style_NeedingUrl_WithUrl_IsParsed()
        {
            var result = Read("styl obrazek https://example.com/a.png");

            Assert.True(result.IsSuccess);
            Assert.Equal(ProfileType.Img, Value(result).Style);
            Assert.Equal("https://example.com/a.png", Value(result).Url);
        }

        // --- tło / nakładka ---

        [Fact]
        public void Background_WithUrl_IsParsed()
        {
            var result = Read("tło https://example.com/a.png");

            Assert.True(result.IsSuccess);
            Assert.Equal("https://example.com/a.png", Value(result).Url);
        }

        [Fact]
        public void Background_WithAttachmentKeywordButNoAttachment_ReturnsParseFailed()
        {
            var result = Read("tło att");

            Assert.False(result.IsSuccess);
            Assert.Equal(CommandError.ParseFailed, result.Error);
        }

        [Fact]
        public void Background_WithAttachment_UsesFirstAttachmentUrl()
        {
            var attachment = new Mock<IAttachment>();
            attachment.SetupGet(x => x.Url).Returns("https://cdn.example.com/x.png");

            var result = Read("tło att", attachment.Object);

            Assert.True(result.IsSuccess);
            Assert.Equal("https://cdn.example.com/x.png", Value(result).Url);
        }

        // --- wartości liczbowe ---

        [Theory]
        [InlineData("przeźroczystość cieni 0", 0)]
        [InlineData("przeźroczystość cieni 50", 50)]
        [InlineData("przeźroczystość cieni 100", 100)]
        public void ShadowsOpacity_InRange_IsParsed(string input, int expected)
        {
            var result = Read(input);

            Assert.True(result.IsSuccess);
            Assert.Equal(expected, Value(result).Value);
        }

        [Theory]
        [InlineData("przeźroczystość cieni 101")]
        [InlineData("przeźroczystość cieni -1")]
        [InlineData("przeźroczystość cieni abc")]
        public void ShadowsOpacity_OutOfRange_ReturnsParseFailed(string input)
        {
            var result = Read(input);

            Assert.False(result.IsSuccess);
            Assert.Equal(CommandError.ParseFailed, result.Error);
        }

        // --- przełączniki ---

        [Theory]
        [InlineData("pasek")]
        [InlineData("mini waifu")]
        [InlineData("anime")]
        [InlineData("manga")]
        [InlineData("karcianka")]
        [InlineData("mini galeria")]
        [InlineData("zamiana paneli")]
        [InlineData("ramka na poziom")]
        [InlineData("okrągły awatar")]
        [InlineData("przeźroczysty pasek")]
        [InlineData("widoczność nakładki")]
        public void ToggleOptions_AreParsed(string input)
        {
            Assert.True(Read(input).IsSuccess);
        }

        // --- ramka awatara ---

        [Theory]
        [InlineData("ramka awatara złota", AvatarBorder.Gold)]
        [InlineData("ramka awatara 14", AvatarBorder.Gold)]
        [InlineData("ramka awatara brak", AvatarBorder.None)]
        public void AvatarBorder_IsParsed(string input, AvatarBorder expected)
        {
            var result = Read(input);

            Assert.True(result.IsSuccess);
            Assert.Equal(expected, Value(result).Border);
        }

        // --- nieznane ---

        [Theory]
        [InlineData("nieznana opcja")]
        [InlineData("123")]
        public void UnknownOption_ReturnsParseFailed(string input)
        {
            var result = Read(input);

            Assert.False(result.IsSuccess);
            Assert.Equal(CommandError.ParseFailed, result.Error);
        }

        [Fact]
        public void EmptyInput_ReturnsDefaultConfig()
        {
            var result = Read("");

            Assert.True(result.IsSuccess);
            Assert.Equal(ProfileConfigType.ShowInfo, Value(result).Type);
        }
    }
}
