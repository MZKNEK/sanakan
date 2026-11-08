using System;
using System.Collections.Generic;
using System.Linq;
using Discord;
using Moq;
using Sanakan.Services;
using Xunit;

namespace Artifacts
{
    public class ReportFormatTests
    {
        private static readonly DateTimeOffset Written = new DateTimeOffset(2026, 11, 8, 14, 3, 0, TimeSpan.Zero);

        private static IMessage Message(string content, params IAttachment[] attachments)
        {
            var channel = new Mock<ITextChannel>();
            channel.SetupGet(x => x.Id).Returns(200);
            channel.SetupGet(x => x.GuildId).Returns(100);

            var author = new Mock<IUser>();
            author.SetupGet(x => x.Id).Returns(42);
            author.SetupGet(x => x.Username).Returns("PrzykładowyNick");
            author.Setup(x => x.GetAvatarUrl(It.IsAny<ImageFormat>(), It.IsAny<ushort>())).Returns("https://cdn.example/avatar.png");

            var message = new Mock<IMessage>();
            message.SetupGet(x => x.Id).Returns(300);
            message.SetupGet(x => x.Content).Returns(content);
            message.SetupGet(x => x.Channel).Returns(channel.Object);
            message.SetupGet(x => x.Author).Returns(author.Object);
            message.SetupGet(x => x.CreatedAt).Returns(Written);
            message.SetupGet(x => x.Attachments).Returns(attachments.ToList());
            return message.Object;
        }

        private static IAttachment Attachment(string name, string url)
        {
            var attachment = new Mock<IAttachment>();
            attachment.SetupGet(x => x.Filename).Returns(name);
            attachment.SetupGet(x => x.Url).Returns(url);
            return attachment.Object;
        }

        [Fact]
        public void Build_LinksTheMessageAndSkipsMissingAttachments()
        {
            var embed = ReportFormat.Build(Message("jesteście beznadziejni"), "InnyGracz (7)", "obraża innych", 555);

            Assert.Equal("🚩 Zgłoszenie", embed.Title);
            Assert.Equal("https://discord.com/channels/100/200/300", embed.Url);
            Assert.Equal("> jesteście beznadziejni", embed.Description);
            Assert.Equal("<#200>", embed.Fields.Single(x => x.Name == "Kanał").Value);
            Assert.Equal($"<t:{Written.ToUnixTimeSeconds()}:R>", embed.Fields.Single(x => x.Name == "Napisano").Value);
            Assert.Equal("`555`", embed.Fields.Single(x => x.Name == "Id zgłoszenia").Value);
            Assert.Equal("obraża innych", embed.Fields.Single(x => x.Name == "Powód").Value);
            Assert.DoesNotContain(embed.Fields, x => x.Name == "Załączniki");
            Assert.Equal("Zgłasza: InnyGracz (7)", embed.Footer?.Text);
        }

        [Fact]
        public void Build_MarksAttachmentOnlyMessages()
        {
            var embed = ReportFormat.Build(Message("", Attachment("obrazek.png", "https://cdn.example/obrazek.png")), "InnyGracz (7)", "nsfw", 555);

            Assert.Equal("*sam załącznik*", embed.Description);
            Assert.Equal("[obrazek.png](https://cdn.example/obrazek.png)", embed.Fields.Single(x => x.Name == "Załączniki").Value);
        }

        [Fact]
        public void AttachmentLinks_KeepWholeLinksWithinTheLimit()
        {
            var attachments = new[] { Attachment("a.png", "https://x/a.png"), Attachment("b.png", "https://x/b.png") };

            Assert.Equal("[a.png](https://x/a.png)\n[b.png](https://x/b.png)", ReportFormat.AttachmentLinks(attachments, 1000));
            Assert.Equal("[a.png](https://x/a.png)", ReportFormat.AttachmentLinks(attachments, 30));
        }

        [Fact]
        public void Resolve_ReplacesTheIdWithTheDecision()
        {
            var report = ReportFormat.Build(Message("tekst"), "InnyGracz (7)", "obraża innych", 555).ToEmbedBuilder();

            Assert.Equal("obraża innych", ReportFormat.GetReason(report));

            var resolved = ReportFormat.Resolve(report, true, "wyciszenie na 1 dzień", "ModNick").Build();

            Assert.Equal("✅ Zgłoszenie rozpatrzone", resolved.Title);
            Assert.Equal(ReportFormat.AcceptedColor, resolved.Color);
            Assert.DoesNotContain(resolved.Fields, x => x.Name == "Id zgłoszenia");
            Assert.Equal("wyciszenie na 1 dzień", resolved.Fields.Single(x => x.Name == "Decyzja").Value);
            Assert.Equal("Zgłasza: InnyGracz (7) • Rozpatrzył: ModNick", resolved.Footer?.Text);
        }

        [Fact]
        public void Resolve_HandlesReportsPostedBeforeTheRedesign()
        {
            var report = new EmbedBuilder()
                .WithFooter("Zgłasza: InnyGracz(7)")
                .AddField("Kanał:", "ogólny", true)
                .AddField("Napisano:", "08.11.2026 14:03", true)
                .AddField("Id zgloszenia:", "555", true)
                .AddField("Powód:", "spam")
                .AddField("Załączniki:", "brak");

            Assert.Equal("spam", ReportFormat.GetReason(report));

            var resolved = ReportFormat.Resolve(report, false, "odrzucone", "ModNick").Build();

            Assert.Equal("❌ Zgłoszenie odrzucone", resolved.Title);
            Assert.Equal(ReportFormat.RejectedColor, resolved.Color);
            Assert.Equal("odrzucone", resolved.Fields.Single(x => x.Name == "Decyzja").Value);
            Assert.DoesNotContain(resolved.Fields, x => x.Name == "Id zgloszenia:" || x.Name == "Załączniki:");
            Assert.Equal("Zgłasza: InnyGracz(7) • Rozpatrzył: ModNick", resolved.Footer?.Text);
        }

        [Fact]
        public void DeletedLog_ShowsWhenTheMessageWasWrittenAndEdited()
        {
            var old = Message("mam 3 karty SSS na wymiane");
            var edited = new Mock<IMessage>();
            edited.SetupGet(x => x.Id).Returns(300);
            edited.SetupGet(x => x.Content).Returns("mam 3 karty SS na wymianę");
            edited.SetupGet(x => x.Channel).Returns(old.Channel);
            edited.SetupGet(x => x.EditedTimestamp).Returns(Written.AddMinutes(28));

            var embed = DeletedLog.BuildMessage(old, edited.Object, Written.AddHours(1));

            Assert.Equal("✏️ Edytowana wiadomość", embed.Title);
            Assert.Equal("https://discord.com/channels/100/200/300", embed.Url);
            Assert.Equal(Written.AddMinutes(28), embed.Timestamp);
            Assert.Equal($"<t:{Written.ToUnixTimeSeconds()}:f>", embed.Fields.Single(x => x.Name == "Napisano").Value);
            Assert.Equal("<#200>", embed.Fields.Single(x => x.Name == "Kanał").Value);
        }

        [Fact]
        public void ChannelReference_NamesTheServerOnlyForAnotherServer()
        {
            var guild = new Mock<IGuild>();
            guild.SetupGet(x => x.Name).Returns("Inny serwer");

            var channel = new Mock<ITextChannel>();
            channel.SetupGet(x => x.Id).Returns(200);
            channel.SetupGet(x => x.Name).Returns("pomoc");
            channel.SetupGet(x => x.GuildId).Returns(100);
            channel.SetupGet(x => x.Guild).Returns(guild.Object);

            Assert.Equal("<#200>", Moderator.ChannelReference(channel.Object, 100));
            Assert.Equal("#pomoc (Inny serwer)", Moderator.ChannelReference(channel.Object, 101));
        }

        [Theory]
        [InlineData(DeletedLog.VoiceActionType.Join, null, 5ul, "🎙️ Dołączył do <#5>")]
        [InlineData(DeletedLog.VoiceActionType.Left, 4ul, null, "📤 Wyszedł z <#4>")]
        [InlineData(DeletedLog.VoiceActionType.Changed, 4ul, 5ul, "🔀 Przeszedł z <#4> na <#5>")]
        public void VoiceText_MentionsTheChannels(DeletedLog.VoiceActionType action, ulong? oldChannel, ulong? newChannel, string expected)
        {
            Assert.Equal(expected, DeletedLog.VoiceText(action, oldChannel, newChannel));
        }

        [Fact]
        public void SupervisionStatus_ShowsTheState()
        {
            Assert.Equal("🛡️ Nadzór na tym kanale jest **aktywny**.", Sanakan.Services.Supervisor.Supervisor.SupervisionStatus(true, false).Description);
            Assert.Equal("🛡️ Nadzór na tym kanale jest **nieaktywny**.", Sanakan.Services.Supervisor.Supervisor.SupervisionStatus(false, false).Description);
            Assert.Contains("tymczasowo włączony", Sanakan.Services.Supervisor.Supervisor.SupervisionStatus(true, true).Description);
        }

        [Fact]
        public void DeletedLog_CountsAttachmentsOnlyWhenThereAreAny()
        {
            var now = Written.AddMinutes(17);

            var text = DeletedLog.BuildMessage(Message("ktoś chce wymienić się kartami?"), null, now);
            Assert.Equal("🗑️ Usunięta wiadomość", text.Title);
            Assert.Null(text.Url);
            Assert.Equal(now, text.Timestamp);
            Assert.DoesNotContain(text.Fields, x => x.Name == "Załączniki");

            var image = DeletedLog.BuildMessage(Message("", Attachment("a.png", "https://x/a.png")), null, now);
            Assert.Equal("*brak tekstu*", image.Description);
            Assert.Equal("1", image.Fields.Single(x => x.Name == "Załączniki").Value);
        }
    }
}
