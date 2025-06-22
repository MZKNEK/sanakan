#pragma warning disable 1591

using System;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Sanakan.Config;

namespace Sanakan.Services.Commands
{
    public abstract class SanakanModuleBase<T> : ModuleBase<T> where T : class, ICommandContext
    {
        private const int MAX_SNED_ATTEMPTS = 3;

        public IConfig Config { get; set; }

        public async Task<IUserMessage> SafeReplyAsync(string message = null, bool isTTS = false, Embed embed = null, RequestOptions options = null, AllowedMentions allowedMentions = null, MessageReference messageReference = null, MessageComponent components = null, ISticker[] stickers = null, Embed[] embeds = null, MessageFlags flags = MessageFlags.None)
        {
            IUserMessage msg = null;
            for (int i = 0; i < MAX_SNED_ATTEMPTS; i++)
            {
                try
                {
                    msg = await ReplyAsync(message, isTTS, embed, options, allowedMentions, messageReference, components, stickers, embeds, flags);
                    if (msg != null)
                        break;

                    await Task.Delay(100);
                }
                catch (Exception) {}
            }
            return msg;
        }
    }
}
