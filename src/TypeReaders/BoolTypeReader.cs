#pragma warning disable 1591

using Discord.Commands;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Sanakan.TypeReaders
{
    public class BoolTypeReader : TypeReader
    {
        private static readonly Dictionary<string, bool> _values = new Dictionary<string, bool>
        {
            { "1",      true  },
            { "tak",    true  },
            { "true",   true  },
            { "prawda", true  },
            { "0",      false },
            { "nie",    false },
            { "false",  false },
            { "falsz",  false },
            { "fałsz",  false },
        };

        public override Task<TypeReaderResult> ReadAsync(ICommandContext context, string input, IServiceProvider services)
        {
            if (_values.TryGetValue(input.ToLower(), out var value))
                return Task.FromResult(TypeReaderResult.FromSuccess(value));

            return Task.FromResult(TypeReaderResult.FromError(CommandError.ParseFailed, "Nie rozpoznano wartości!"));
        }
    }
}