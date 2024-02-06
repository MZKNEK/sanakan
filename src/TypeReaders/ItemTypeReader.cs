#pragma warning disable 1591

using Discord.Commands;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using System;
using System.Threading.Tasks;

namespace Sanakan.TypeReaders
{
    public class ItemTypeReader : TypeReader
    {
        public override Task<TypeReaderResult> ReadAsync(ICommandContext context, string input, IServiceProvider services)
        {
            foreach (ItemType type in Enum.GetValues(typeof(ItemType)))
            {
                if (type.Name().Equals(input, StringComparison.CurrentCultureIgnoreCase))
                    return Task.FromResult(TypeReaderResult.FromSuccess(type));

                if (type.ToString().Equals(input, StringComparison.CurrentCultureIgnoreCase))
                    return Task.FromResult(TypeReaderResult.FromSuccess(type));

                if (((int)type).ToString().Equals(input, StringComparison.CurrentCultureIgnoreCase))
                    return Task.FromResult(TypeReaderResult.FromSuccess(type));
            }
            return Task.FromResult(TypeReaderResult.FromError(CommandError.ParseFailed, "Nie rozpoznano przedmiotu!"));
        }
    }
}