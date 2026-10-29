#pragma warning disable 1591

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Discord;
using Newtonsoft.Json;

namespace Sanakan.Services
{
    // Discord API message shape (snake_case, snowflakes as strings), limited to what the ingest needs.
    public class IngestMessage
    {
        private static readonly JsonSerializerSettings _json = new JsonSerializerSettings { Formatting = Formatting.None };

        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("channel_id")] public string ChannelId { get; set; }
        [JsonProperty("type")] public int Type { get; set; }
        [JsonProperty("flags")] public int Flags { get; set; }
        [JsonProperty("timestamp")] public string Timestamp { get; set; }
        [JsonProperty("author")] public IngestAuthor Author { get; set; }
        [JsonProperty("content")] public string Content { get; set; }
        [JsonProperty("embeds")] public List<IngestEmbed> Embeds { get; set; }
        [JsonProperty("components")] public List<IngestComponent> Components { get; set; }
        [JsonProperty("message_reference")] public IngestReference MessageReference { get; set; }
        [JsonProperty("webhook_id")] public string WebhookId { get; set; }
        [JsonProperty("interaction_metadata")] public IngestInteraction InteractionMetadata { get; set; }

        public static string Serialize(IMessage msg) => JsonConvert.SerializeObject(From(msg), _json);

        public static IngestMessage From(IMessage msg)
        {
            var reference = msg.Reference;
            var interaction = (msg as IUserMessage)?.InteractionMetadata;

            return new IngestMessage
            {
                Id = msg.Id.ToString(),
                ChannelId = msg.Channel.Id.ToString(),
                Type = (int)msg.Type,
                Flags = (int)(msg.Flags ?? MessageFlags.None),
                Timestamp = Format(msg.Timestamp),
                Author = new IngestAuthor
                {
                    Id = msg.Author.Id.ToString(),
                    Username = msg.Author.Username,
                    Bot = msg.Author.IsBot || msg.Author.IsWebhook,
                },
                Content = msg.Content ?? "",
                Embeds = msg.Embeds?.Select(IngestEmbed.From).ToList() ?? new List<IngestEmbed>(),
                Components = IngestComponent.LinkRows(msg.Components),
                MessageReference = reference == null ? null : new IngestReference
                {
                    GuildId = reference.GuildId.IsSpecified ? reference.GuildId.Value.ToString() : null,
                    ChannelId = reference.ChannelId != 0 ? reference.ChannelId.ToString() : null,
                    MessageId = reference.MessageId.IsSpecified ? reference.MessageId.Value.ToString() : null,
                },
                WebhookId = msg.Author is IWebhookUser webhook ? webhook.WebhookId.ToString() : null,
                // the user who ran the command is left out on purpose - only presence matters to the receiver
                InteractionMetadata = interaction == null ? null : new IngestInteraction
                {
                    Id = interaction.Id.ToString(),
                    Type = (int)interaction.Type,
                },
            };
        }

        internal static string Format(System.DateTimeOffset time)
            => time.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'+00:00'", CultureInfo.InvariantCulture);
    }

    public class IngestAuthor
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("username")] public string Username { get; set; }
        [JsonProperty("bot")] public bool Bot { get; set; }
    }

    [JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
    public class IngestReference
    {
        [JsonProperty("guild_id")] public string GuildId { get; set; }
        [JsonProperty("channel_id")] public string ChannelId { get; set; }
        [JsonProperty("message_id")] public string MessageId { get; set; }
    }

    public class IngestInteraction
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("type")] public int Type { get; set; }
    }

    [JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
    public class IngestEmbed
    {
        [JsonProperty("type")] public string Type { get; set; }
        [JsonProperty("title")] public string Title { get; set; }
        [JsonProperty("description")] public string Description { get; set; }
        [JsonProperty("url")] public string Url { get; set; }
        [JsonProperty("timestamp")] public string Timestamp { get; set; }
        [JsonProperty("color")] public uint? Color { get; set; }
        [JsonProperty("footer")] public IngestEmbedFooter Footer { get; set; }
        [JsonProperty("image")] public IngestEmbedMedia Image { get; set; }
        [JsonProperty("thumbnail")] public IngestEmbedMedia Thumbnail { get; set; }
        [JsonProperty("video")] public IngestEmbedMedia Video { get; set; }
        [JsonProperty("provider")] public IngestEmbedProvider Provider { get; set; }
        [JsonProperty("author")] public IngestEmbedAuthor Author { get; set; }
        [JsonProperty("fields")] public List<IngestEmbedField> Fields { get; set; }

        public static IngestEmbed From(IEmbed embed) => new IngestEmbed
        {
            Type = embed.Type == EmbedType.Unknown ? null : embed.Type.ToString().ToLowerInvariant(),
            Title = embed.Title,
            Description = embed.Description,
            Url = embed.Url,
            Timestamp = embed.Timestamp.HasValue ? IngestMessage.Format(embed.Timestamp.Value) : null,
            Color = embed.Color?.RawValue,
            Footer = embed.Footer.HasValue ? new IngestEmbedFooter { Text = embed.Footer.Value.Text, IconUrl = embed.Footer.Value.IconUrl } : null,
            Image = embed.Image.HasValue ? new IngestEmbedMedia { Url = embed.Image.Value.Url, Width = embed.Image.Value.Width, Height = embed.Image.Value.Height } : null,
            Thumbnail = embed.Thumbnail.HasValue ? new IngestEmbedMedia { Url = embed.Thumbnail.Value.Url, Width = embed.Thumbnail.Value.Width, Height = embed.Thumbnail.Value.Height } : null,
            Video = embed.Video.HasValue ? new IngestEmbedMedia { Url = embed.Video.Value.Url, Width = embed.Video.Value.Width, Height = embed.Video.Value.Height } : null,
            Provider = embed.Provider.HasValue ? new IngestEmbedProvider { Name = embed.Provider.Value.Name, Url = embed.Provider.Value.Url } : null,
            Author = embed.Author.HasValue ? new IngestEmbedAuthor { Name = embed.Author.Value.Name, Url = embed.Author.Value.Url, IconUrl = embed.Author.Value.IconUrl } : null,
            Fields = embed.Fields.Length > 0 ? embed.Fields.Select(x => new IngestEmbedField { Name = x.Name, Value = x.Value, Inline = x.Inline }).ToList() : null,
        };
    }

    [JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
    public class IngestEmbedFooter
    {
        [JsonProperty("text")] public string Text { get; set; }
        [JsonProperty("icon_url")] public string IconUrl { get; set; }
    }

    [JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
    public class IngestEmbedMedia
    {
        [JsonProperty("url")] public string Url { get; set; }
        [JsonProperty("width")] public int? Width { get; set; }
        [JsonProperty("height")] public int? Height { get; set; }
    }

    [JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
    public class IngestEmbedProvider
    {
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("url")] public string Url { get; set; }
    }

    [JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
    public class IngestEmbedAuthor
    {
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("url")] public string Url { get; set; }
        [JsonProperty("icon_url")] public string IconUrl { get; set; }
    }

    public class IngestEmbedField
    {
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("value")] public string Value { get; set; }
        [JsonProperty("inline")] public bool Inline { get; set; }
    }

    [JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
    public class IngestComponent
    {
        [JsonProperty("type")] public int Type { get; set; }
        [JsonProperty("style")] public int? Style { get; set; }
        [JsonProperty("label")] public string Label { get; set; }
        [JsonProperty("url")] public string Url { get; set; }
        [JsonProperty("components")] public List<IngestComponent> Components { get; set; }

        public static List<IngestComponent> LinkRows(IEnumerable<IMessageComponent> components)
        {
            var rows = new List<IngestComponent>();
            foreach (var row in components?.OfType<ActionRowComponent>() ?? Enumerable.Empty<ActionRowComponent>())
            {
                var links = row.Components.OfType<ButtonComponent>()
                    .Where(x => x.Style == ButtonStyle.Link && !string.IsNullOrEmpty(x.Url))
                    .Select(x => new IngestComponent { Type = (int)ComponentType.Button, Style = (int)ButtonStyle.Link, Label = x.Label, Url = x.Url })
                    .ToList();

                if (links.Count > 0)
                    rows.Add(new IngestComponent { Type = (int)ComponentType.ActionRow, Components = links });
            }
            return rows;
        }
    }
}
