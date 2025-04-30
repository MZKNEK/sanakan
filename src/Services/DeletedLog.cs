#pragma warning disable 1591

using System;
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
        private DiscordSocketClient _client;
        private IConfig _config;

        private enum VoiceActionType
        {
            Join, Left, Changed
        }

        public DeletedLog(DiscordSocketClient client, IConfig config)
        {
            _client = client;
            _config = config;

            _client.MessageDeleted += HandleDeletedMsgAsync;
            _client.MessageUpdated += HandleUpdatedMsgAsync;
            _client.UserVoiceStateUpdated += HandleVoiceStateUpdatedAsync;
        }

        private Task HandleVoiceStateUpdatedAsync(SocketUser user, SocketVoiceState oldState, SocketVoiceState newState)
        {
            bool userJoined = oldState.VoiceChannel == null && newState.VoiceChannel != null;
            bool userLeft = newState.VoiceChannel == null && oldState.VoiceChannel != null;

            var action = userJoined ? VoiceActionType.Join : (userLeft ? VoiceActionType.Left : VoiceActionType.Changed);
            _ = Task.Run(async () =>
            {
                await LogVoiceChange(user, action, oldState.VoiceChannel, newState.VoiceChannel);
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
                    await LogMessageAsync(gChannel, oldMessage.Value, newMessage);
                });
            }

            return Task.CompletedTask;
        }

        private Task HandleDeletedMsgAsync(Cacheable<IMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel)
        {
            if (!message.HasValue) return Task.CompletedTask;

            if (message.Value.Author.IsBot || message.Value.Author.IsWebhook) return Task.CompletedTask;

            if (message.Value.Content.Length < 4 && message.Value.Attachments.Count < 1) return Task.CompletedTask;

            if (message.Value.Channel is SocketGuildChannel gChannel)
            {
                if (_config.Get().BlacklistedGuilds.Any(x => x == gChannel.Guild.Id))
                    return Task.CompletedTask;

                _ = Task.Run(async () =>
                {
                    await LogMessageAsync(gChannel, message.Value);
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

                await ch.SendMessageAsync("", embed: BuildVoiceMessage(user, action, oldChannel?.Name ?? "??", newChannel?.Name ?? "??"));
            }
        }

        private Embed BuildVoiceMessage(SocketUser user, VoiceActionType action, string newChannel, string oldChannel)
        {
            var color = action switch
            {
                VoiceActionType.Join => EMType.Success.Color(),
                VoiceActionType.Left => EMType.Error.Color(),
                _ => EMType.Bot.Color()
            };

            var text = action switch
            {
                VoiceActionType.Left => $"[VA] Opuścił kanał: {newChannel}",
                VoiceActionType.Join => $"[VA] Dołączył do kanału: {oldChannel}",
                _ => $"[VA] Zmienił kanał z: {oldChannel} na: {newChannel}"
            };

            return new EmbedBuilder
            {
                Color = color,
                Author = new EmbedAuthorBuilder().WithUser(user, true),
                Description = text,
            }.Build();
        }

        private async Task LogMessageAsync(SocketGuildChannel channel, IMessage oldMessage, IMessage newMessage = null)
        {
            if (oldMessage.Content.IsEmotikunEmote() && newMessage == null) return;

            using (var db = new Database.DatabaseContext(_config))
            {
                var config = await db.GetCachedGuildFullConfigAsync(channel.Guild.Id);
                if (config == null) return;

                var ch = channel.Guild.GetTextChannel(config.LogChannel);
                if (ch == null) return;

                var jump = (newMessage == null) ? "" : $"{newMessage.GetJumpUrl()}";
                await ch.SendMessageAsync(jump, embed: BuildMessage(oldMessage, newMessage));
            }
        }

        private Embed BuildMessage(IMessage oldMessage, IMessage newMessage)
        {
            string content = (newMessage == null) ? oldMessage.Content
                : $"**Stara:**\n{oldMessage.Content}\n\n**Nowa:**\n{newMessage.Content}";

            return new EmbedBuilder
            {
                Color = (newMessage == null) ? EMType.Warning.Color() : EMType.Info.Color(),
                Author = new EmbedAuthorBuilder().WithUser(oldMessage.Author, true),
                Fields = GetFields(oldMessage, newMessage == null),
                Description = content.TrimToLength(),
            }.Build();
        }

        private List<EmbedFieldBuilder> GetFields(IMessage message, bool deleted)
        {
            var fields = new List<EmbedFieldBuilder>
            {
                new EmbedFieldBuilder
                {
                    IsInline = true,
                    Name = deleted ? "Napisano:" : "Edytowano:",
                    Value = message.GetLocalCreatedAtShortDateTime()
                },
                new EmbedFieldBuilder
                {
                    IsInline = true,
                    Name = "Kanał:",
                    Value = message.Channel.Name
                }
            };

            if (deleted)
            {
                fields.Add(new EmbedFieldBuilder
                {
                    IsInline = true,
                    Name = "Załączniki:",
                    Value = message.Attachments?.Count
                });
            }

            return fields;
        }
    }
}
