#pragma warning disable 1591

using Discord.Commands;
using Sanakan.Services.PocketWaifu;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Sanakan.TypeReaders
{
    public class ModifyTagActionTypeReader : TypeReader
    {
        private static readonly Dictionary<string, ModifyTagActionType> _values = new Dictionary<string, ModifyTagActionType>
        {
            { "delete",      ModifyTagActionType.Delete },
            { "skasuj",      ModifyTagActionType.Delete },
            { "usuń",        ModifyTagActionType.Delete },
            { "usun",        ModifyTagActionType.Delete },
            { "rename",      ModifyTagActionType.Rename },
            { "zmień",       ModifyTagActionType.Rename },
            { "zmien",       ModifyTagActionType.Rename },
            { "zmien nazwe", ModifyTagActionType.Rename },
            { "zmień nazwe", ModifyTagActionType.Rename },
            { "zmien nazwę", ModifyTagActionType.Rename },
            { "zmień nazwę", ModifyTagActionType.Rename },
        };

        public override Task<TypeReaderResult> ReadAsync(ICommandContext context, string input, IServiceProvider services)
        {
            if (_values.TryGetValue(input.ToLower(), out var value))
                return Task.FromResult(TypeReaderResult.FromSuccess(value));

            return Task.FromResult(TypeReaderResult.FromError(CommandError.ParseFailed, "Nie rozpoznano wartości!"));
        }
    }
}