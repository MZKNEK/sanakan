#pragma warning disable 1591

using System;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Shinden.Logger;

namespace Sanakan.Services
{
    // Deletes the bot's own DM message when the user reacts to it with a wastebasket.
    public class DmTrash
    {
        private const string Wastebasket = "\U0001F5D1";

        private readonly DiscordSocketClient _client;
        private readonly ILogger _logger;

        public DmTrash(DiscordSocketClient client, ILogger logger)
        {
            _client = client;
            _logger = logger;

            _client.ReactionAdded += HandleReactionAddedAsync;
        }

        public static bool IsTrash(IEmote emote)
            => emote is Emoji emoji && emoji.Name.Replace("️", "") == Wastebasket;

        public static bool ShouldDelete(IMessage message, ulong botId, ulong reactingUserId)
            => message?.Channel is IDMChannel && message.Author?.Id == botId && reactingUserId != botId;

        private Task HandleReactionAddedAsync(Cacheable<IUserMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel, SocketReaction reaction)
        {
            var botId = _client.CurrentUser?.Id ?? 0;
            if (!IsTrash(reaction.Emote) || reaction.UserId == botId)
                return Task.CompletedTask;

            if (channel.HasValue && channel.Value is not IDMChannel)
                return Task.CompletedTask;

            _ = Task.Run(async () =>
            {
                try
                {
                    var msg = await message.GetOrDownloadAsync();
                    if (ShouldDelete(msg, botId, reaction.UserId))
                        await msg.DeleteAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError($"DmTrash: {ex.Message}");
                }
            });

            return Task.CompletedTask;
        }
    }
}
