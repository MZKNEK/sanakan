#pragma warning disable 1591

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Sanakan.Config;
using Sanakan.Extensions;

namespace Sanakan.Services
{
    public class DeletedLog
    {
        private static readonly ConcurrentDictionary<ulong, byte> _skipped = new ConcurrentDictionary<ulong, byte>();

        private DiscordSocketClient _client;
        private IConfig _config;
        private ILogger _logger;

        public enum VoiceActionType
        {
            Join, Left, Changed
        }

        public DeletedLog(DiscordSocketClient client, IConfig config, ILogger logger)
        {
            _client = client;
            _config = config;
            _logger = logger;

            _client.MessageDeleted += HandleDeletedMsgAsync;
            _client.MessageUpdated += HandleUpdatedMsgAsync;
            _client.UserVoiceStateUpdated += HandleVoiceStateUpdatedAsync;
        }

        // The bot deletes messages containing secrets; their content must not be reposted to the log channel.
        public static void Skip(ulong messageId) => _skipped.TryAdd(messageId, 0);

        public static bool ConsumeSkip(ulong messageId) => _skipped.TryRemove(messageId, out _);

        private Task HandleVoiceStateUpdatedAsync(SocketUser user, SocketVoiceState oldState, SocketVoiceState newState)
        {
            if (user == null || user.IsBot || user.IsWebhook)
                return Task.CompletedTask;

            bool userJoined = oldState.VoiceChannel == null && newState.VoiceChannel != null;
            bool userLeft = newState.VoiceChannel == null && oldState.VoiceChannel != null;

            var action = userJoined ? VoiceActionType.Join : (userLeft ? VoiceActionType.Left : VoiceActionType.Changed);
            if (action == VoiceActionType.Changed && (oldState.VoiceChannel?.Id ?? 0) == (newState.VoiceChannel?.Id ?? 0))
                return Task.CompletedTask;

            _ = Task.Run(async () =>
            {
                try
                {
                    await LogVoiceChange(user, action, oldState.VoiceChannel, newState.VoiceChannel);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"DeletedLog: voice: {ex}");
                }
            });

            return Task.CompletedTask;
        }

        private Task HandleUpdatedMsgAsync(Cacheable<IMessage, ulong> oldMessage, SocketMessage newMessage, ISocketMessageChannel channel)
        {
            if (!oldMessage.HasValue) return Task.CompletedTask;

            if (newMessage.Author.IsBot || newMessage.Author.IsWebhook) return Task.CompletedTask;

            if (oldMessage.Value.Content.Equals(newMessage.Content, StringComparison.CurrentCultureIgnoreCase)) return Task.CompletedTask;

            if (newMessage.Channel is SocketGuildChannel gChannel)
            {
                if (_config.Get().BlacklistedGuilds.Any(x => x == gChannel.Guild.Id))
                    return Task.CompletedTask;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await LogMessageAsync(gChannel, oldMessage.Value, newMessage);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"DeletedLog: updated: {ex}");
                    }
                });
            }

            return Task.CompletedTask;
        }

        private Task HandleDeletedMsgAsync(Cacheable<IMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel)
        {
            if (ConsumeSkip(message.Id)) return Task.CompletedTask;

            if (!message.HasValue) return Task.CompletedTask;

            if (message.Value.Author.IsBot || message.Value.Author.IsWebhook) return Task.CompletedTask;

            if (message.Value.Content.Length < 4 && message.Value.Attachments.Count < 1) return Task.CompletedTask;

            if (message.Value.Channel is SocketGuildChannel gChannel)
            {
                if (_config.Get().BlacklistedGuilds.Any(x => x == gChannel.Guild.Id))
                    return Task.CompletedTask;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await LogMessageAsync(gChannel, message.Value);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"DeletedLog: deleted: {ex}");
                    }
                });
            }

            return Task.CompletedTask;
        }

        private async Task LogVoiceChange(SocketUser user, VoiceActionType action, SocketVoiceChannel oldChannel, SocketVoiceChannel newChannel)
        {
            using (var db = new Database.DatabaseContext(_config))
            {
                var guild = oldChannel?.Guild ?? newChannel?.Guild;
                if (guild == null) return;

                var config = await db.GetCachedGuildFullConfigAsync(guild.Id);
                if (config == null) return;

                var ch = guild.GetTextChannel(config.LogChannel);
                if (ch == null) return;

                await ch.SendMessageAsync("", embed: BuildVoiceMessage(user, action, oldChannel?.Id, newChannel?.Id));
            }
        }

        private static Embed BuildVoiceMessage(IUser user, VoiceActionType action, ulong? oldChannel, ulong? newChannel)
            => new EmbedBuilder
            {
                Color = action switch
                {
                    VoiceActionType.Join => JoinedColor,
                    VoiceActionType.Left => DeletedColor,
                    _ => EditedColor
                },
                Author = new EmbedAuthorBuilder().WithUser(user, true),
                Description = VoiceText(action, oldChannel, newChannel),
            }.Build();

        public static string VoiceText(VoiceActionType action, ulong? oldChannel, ulong? newChannel) => action switch
        {
            VoiceActionType.Left => $"📤 Wyszedł z {ChannelMention(oldChannel)}",
            VoiceActionType.Join => $"🎙️ Dołączył do {ChannelMention(newChannel)}",
            _ => $"🔀 Przeszedł z {ChannelMention(oldChannel)} na {ChannelMention(newChannel)}"
        };

        private static string ChannelMention(ulong? id) => id.HasValue ? $"<#{id.Value}>" : "??";

        private static readonly Color JoinedColor = new Color(59, 165, 93);

        private async Task LogMessageAsync(SocketGuildChannel channel, IMessage oldMessage, IMessage newMessage = null)
        {
            if (oldMessage.Content.IsEmotikunEmote() && newMessage == null) return;

            using (var db = new Database.DatabaseContext(_config))
            {
                var config = await db.GetCachedGuildFullConfigAsync(channel.Guild.Id);
                if (config == null) return;

                var ch = channel.Guild.GetTextChannel(config.LogChannel);
                if (ch == null) return;

                await ch.SendMessageAsync("", embed: BuildMessage(oldMessage, newMessage, DateTimeOffset.Now));
            }
        }

        public static Embed BuildMessage(IMessage oldMessage, IMessage newMessage, DateTimeOffset now)
        {
            var deleted = newMessage == null;
            var content = deleted ? TextOrPlaceholder(oldMessage.Content)
                : $"**Stara:**\n{TextOrPlaceholder(oldMessage.Content)}\n\n**Nowa:**\n{TextOrPlaceholder(newMessage.Content)}";

            var embed = new EmbedBuilder
            {
                Title = deleted ? "🗑️ Usunięta wiadomość" : "✏️ Edytowana wiadomość",
                Url = deleted ? null : newMessage.GetJumpUrl(),
                Color = deleted ? DeletedColor : EditedColor,
                Author = new EmbedAuthorBuilder().WithUser(oldMessage.Author, true),
                Description = content.TrimToLength(),
                Footer = new EmbedFooterBuilder().WithText(deleted ? "Usunięto" : "Edytowano"),
                Timestamp = deleted ? now : newMessage.EditedTimestamp ?? now,
            }
            .AddField("Kanał", $"<#{oldMessage.Channel.Id}>", true)
            .AddField("Napisano", $"<t:{oldMessage.CreatedAt.ToUnixTimeSeconds()}:f>", true);

            var attachments = oldMessage.Attachments?.Count ?? 0;
            if (deleted && attachments > 0)
                embed.AddField("Załączniki", attachments, true);

            return embed.Build();
        }

        private static readonly Color DeletedColor = new Color(229, 72, 77);
        private static readonly Color EditedColor = new Color(91, 141, 239);

        private static string TextOrPlaceholder(string content)
            => string.IsNullOrWhiteSpace(content) ? "*brak tekstu*" : content;
    }
}
