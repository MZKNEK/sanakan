#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Discord;
using Sanakan.Extensions;

namespace Sanakan.Services
{
    public static class ReportFormat
    {
        public static readonly Color OpenColor = new Color(229, 72, 77);
        public static readonly Color AcceptedColor = new Color(59, 165, 93);
        public static readonly Color RejectedColor = new Color(128, 132, 142);

        // reports posted before the redesign use the old field names
        private static readonly string[] _idFields = { "Id zgłoszenia", "Id zgloszenia:" };
        private static readonly string[] _reasonFields = { "Powód", "Powód:" };
        private static readonly string[] _attachmentFields = { "Załączniki", "Załączniki:" };

        public static Embed Build(IMessage message, string reporter, string reason, ulong reportId)
        {
            var embed = new EmbedBuilder
            {
                Title = "🚩 Zgłoszenie",
                Url = message.GetJumpUrl(),
                Color = OpenColor,
                Author = new EmbedAuthorBuilder().WithUser(message.Author),
                Description = string.IsNullOrWhiteSpace(message.Content) ? "*sam załącznik*" : PenaltyFormat.Quote(message.Content).TrimToLength(2500),
                Footer = new EmbedFooterBuilder().WithText($"Zgłasza: {reporter}".TrimToLength(EmbedFooterBuilder.MaxFooterTextLength)),
            }
            .AddField("Kanał", $"<#{message.Channel.Id}>", true)
            .AddField("Napisano", $"<t:{message.CreatedAt.ToUnixTimeSeconds()}:R>", true)
            .AddField(_idFields[0], $"`{reportId}`", true)
            .AddField(_reasonFields[0], string.IsNullOrWhiteSpace(reason) ? "nie podano" : reason.TrimToLength(EmbedFieldBuilder.MaxFieldValueLength));

            var attachments = AttachmentLinks(message.Attachments, EmbedFieldBuilder.MaxFieldValueLength);
            if (attachments.Length > 0)
                embed.AddField(_attachmentFields[0], attachments);

            return embed.Build();
        }

        public static string AttachmentLinks(IEnumerable<IAttachment> attachments, int limit)
        {
            var text = new StringBuilder();
            foreach (var link in attachments.Select(x => $"[{x.Filename.Replace("[", "(").Replace("]", ")")}]({x.Url})"))
            {
                var separator = text.Length > 0 ? "\n" : "";
                if (text.Length + separator.Length + link.Length > limit)
                    break;

                text.Append(separator).Append(link);
            }
            return text.ToString();
        }

        public static string GetReason(EmbedBuilder report) => Find(report, _reasonFields)?.Value?.ToString();

        public static EmbedBuilder Resolve(EmbedBuilder report, bool accepted, string decision, string byWho)
        {
            report.Title = accepted ? "✅ Zgłoszenie rozpatrzone" : "❌ Zgłoszenie odrzucone";
            report.Color = accepted ? AcceptedColor : RejectedColor;

            report.Fields.RemoveAll(x => _attachmentFields.Contains(x.Name) && x.Value?.ToString() == "brak");

            var id = Find(report, _idFields);
            if (id != null)
            {
                id.Name = "Decyzja";
                id.Value = decision;
            }
            else
            {
                report.AddField("Decyzja", decision, true);
            }

            var reporter = report.Footer?.Text;
            var resolvedBy = $"Rozpatrzył: {byWho}";
            report.Footer = new EmbedFooterBuilder().WithText(
                (string.IsNullOrEmpty(reporter) ? resolvedBy : $"{reporter} • {resolvedBy}").TrimToLength(EmbedFooterBuilder.MaxFooterTextLength));

            return report;
        }

        private static EmbedFieldBuilder Find(EmbedBuilder report, string[] names)
            => report?.Fields?.FirstOrDefault(x => names.Contains(x.Name));
    }
}
