#pragma warning disable 1591

using System;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Sanakan.Config;
using Sanakan.Extensions;

namespace Sanakan.Services.Commands
{
    public abstract class SanakanModuleBase<T> : ModuleBase<T> where T : class, ICommandContext
    {
        private const int MAX_SNED_ATTEMPTS = 3;
        private const int DELAY_MS = 75;

        public IConfig Config { get; set; }
        public ILogger Logger { get; set; }

        private string _command;

        // the saves of this command are counted under its name, also those it queues for later (Executable)
        protected override void BeforeExecute(CommandInfo command)
        {
            _command = command.Name;
            GameTelemetry.Reason = command.Name;
            base.BeforeExecute(command);
        }

        public async Task<IUserMessage> SafeReplyAsync(string message = null, bool isTTS = false, Embed embed = null, RequestOptions options = null, AllowedMentions allowedMentions = null, MessageReference messageReference = null, MessageComponent components = null, ISticker[] stickers = null, Embed[] embeds = null, MessageFlags flags = MessageFlags.None)
        {
            if (_command != null && embed?.Color == EMType.Error.Color())
                GameTelemetry.Default.ErrorReply(_command);

            IUserMessage msg = null;
            Exception last = null;
            for (int i = 0; i < MAX_SNED_ATTEMPTS; i++)
            {
                try
                {
                    msg = await ReplyAsync(message, isTTS, embed, options, allowedMentions, messageReference, components, stickers, embeds, flags);
                    if (msg != null)
                        return msg;
                }
                catch (Exception ex)
                {
                    last = ex;
                }
                await Task.Delay(DELAY_MS);
            }
            if (last != null)
                Logger?.LogError($"Cmd: reply failed: {last.Message}");
            return msg;
        }

        public Task<IUserMessage> SafeReplyWithTextFileAsync(Embed embed, string fileName, string fileContent)
            => SafeSendTextFileAsync(Context.Channel, fileName, fileContent, embed);

        public async Task<IUserMessage> SafeSendTextFileAsync(IMessageChannel channel, string fileName, string fileContent, Embed embed = null)
        {
            Exception last = null;
            for (int i = 0; i < MAX_SNED_ATTEMPTS; i++)
            {
                try
                {
                    using var stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(fileContent));
                    var msg = await channel.SendFileAsync(stream, fileName, embed: embed);
                    if (msg != null)
                        return msg;
                }
                catch (Exception ex)
                {
                    last = ex;
                }
                await Task.Delay(DELAY_MS);
            }
            if (last != null)
                Logger?.LogError($"Cmd: reply with file failed: {last.Message}");
            return null;
        }

        public async Task<bool> SafeAddReactionToMsg(IUserMessage msg, IEmote emote)
        {
            Exception last = null;
            for (int i = 0; i < MAX_SNED_ATTEMPTS; i++)
            {
                try
                {
                    await msg.AddReactionAsync(emote);
                    return true;
                }
                catch (Exception ex)
                {
                    last = ex;
                }
                await Task.Delay(DELAY_MS);
            }
            Logger?.LogError($"Cmd: add reaction failed: {last?.Message}");
            return false;
        }

        // sends an image generated earlier with a note about its age, false when there is none
        public async Task<bool> TrySendSavedImageAsync(string path, Func<DateTime, string> note, MessageComponent components = null)
        {
            if (!System.IO.File.Exists(path))
                return false;

            try
            {
                await Context.Channel.SendFileAsync(path, note(System.IO.File.GetLastWriteTime(path)), components: components);
                return true;
            }
            catch (Exception ex)
            {
                Logger?.LogError($"Cmd: saved image {path}: {ex.Message}");
                return false;
            }
        }
    }
}
