#pragma warning disable 1591

using Discord.Commands;
using Sanakan.Config;
using Sanakan.Extensions;
using System;
using System.Threading.Tasks;

namespace Sanakan.Preconditions
{
    public class RequireAnyCommandOrDuelChannel : RequireAnyCommandChannel
    {
        public async override Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command, IServiceProvider services)
        {
            if (context.Guild != null)
            {
                var config = (IConfig)services.GetService(typeof(IConfig));
                using (var db = new Database.DatabaseContext(config))
                {
                    var gConfig = await db.GetCachedGuildFullConfigAsync(context.Guild.Id);
                    if (gConfig?.WaifuConfig?.DuelChannel is > 0 && gConfig.WaifuConfig.DuelChannel == context.Channel.Id)
                        return PreconditionResult.FromSuccess();
                }
            }
            return await base.CheckPermissionsAsync(context, command, services);
        }
    }
}
