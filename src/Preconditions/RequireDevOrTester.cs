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
    public class RequireDevOrTester : PreconditionAttribute
    {
        public async override Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command, IServiceProvider services)
        {
            var config = (IConfig)services.GetService(typeof(IConfig));
            if (config.Get().Dev.Any(x => x == context.User.Id))
                return PreconditionResult.FromSuccess();

            var user = context.User as SocketGuildUser;
            if (user == null) return PreconditionResult.FromError($"To polecenie działa tylko z poziomu serwera.");
            using (var db = new Database.DatabaseContext(config))
            {
                var gConfig = await db.GetCachedGuildFullConfigAsync(context.Guild.Id);
                if (gConfig != null)
                {
                    var role = context.Guild.GetRole(gConfig.TesterRole);
                    if (role != null && user.Roles.Any(x => x.Id == role.Id)) return PreconditionResult.FromSuccess();
                }
            }

            await Task.CompletedTask;

            return PreconditionResult.FromError($"|IMAGE|https://i.giphy.com/d1E1msx7Yw5Ne1Fe.gif");
        }
    }
}