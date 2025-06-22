#pragma warning disable 1591

using Discord.Commands;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using System;
using System.Threading.Tasks;

namespace Sanakan.TypeReaders
{
    public class ActionAfterExpedtionTypeReader : TypeReader
    {
        public override Task<TypeReaderResult> ReadAsync(ICommandContext context, string input, IServiceProvider services)
        {
            foreach (ActionAfterExpedition action in Enum.GetValues(typeof(ActionAfterExpedition)))
            {
                if (action.ToName().Equals(input, StringComparison.CurrentCultureIgnoreCase))
                    return Task.FromResult(TypeReaderResult.FromSuccess(action));

                if (action.ToString().Equals(input, StringComparison.CurrentCultureIgnoreCase))
                    return Task.FromResult(TypeReaderResult.FromSuccess(action));
            }
            return Task.FromResult(TypeReaderResult.FromError(CommandError.ParseFailed, "Nie rozpoznano akcji!"));
        }
    }
}