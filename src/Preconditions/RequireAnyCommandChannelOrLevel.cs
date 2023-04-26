#pragma warning disable 1591

using Discord.Commands;
using Discord.WebSocket;
using Sanakan.Config;
using Sanakan.Extensions;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Sanakan.Preconditions
{
    public class RequireAnyCommandChannelOrLevel : PreconditionAttribute
    {
        private readonly long _level;

        public RequireAnyCommandChannelOrLevel(long level) => _level = level;

        public async override Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command, IServiceProvider services)
        {
            var user = context.User as SocketGuildUser;
            if (user == null) return PreconditionResult.FromSuccess();

            if (user.GuildPermissions.Administrator)
                return PreconditionResult.FromSuccess();

            var config = (IConfig)services.GetService(typeof(IConfig));
            using (var db = new Database.DatabaseContext(config))
            {
                var botUser = await db.GetBaseUserAndDontTrackAsync(user.Id);
                if (botUser != null)
                {
                    if (botUser.IsBlacklisted)
                        return PreconditionResult.FromError($"{user.Mention} znajdujesz się na czarnej liście bota i nie możesz uzyć tego polecenia.");

                    if (botUser.Level >= _level)
                        return PreconditionResult.FromSuccess();
                }

                var gConfig = await db.GetCachedGuildFullConfigAsync(context.Guild.Id);
                if (gConfig == null) return PreconditionResult.FromSuccess();

                if (!gConfig.CommandChannels.IsNullOrEmpty())
                {
                    if (gConfig.CommandChannels.Any(x => x.Channel == context.Channel.Id))
                        return PreconditionResult.FromSuccess();

                    if (!gConfig?.WaifuConfig?.CommandChannels.IsNullOrEmpty() ?? false)
                    {
                        if (gConfig.WaifuConfig.CommandChannels.Any(x => x.Channel == context.Channel.Id))
                            return PreconditionResult.FromSuccess();
                    }

                    var channel = await context.Guild.GetTextChannelAsync(gConfig.CommandChannels.First().Channel);
                    return PreconditionResult.FromError($"|IMAGE|https://sanakan.pl/i/gif/nope.gif|To polecenie działa na kanale {channel?.Mention}, możesz użyć go tutaj po osiągnięciu {_level} poziomu.");
                }
                return PreconditionResult.FromSuccess();
            }
        }
    }
}