#pragma warning disable 1591

using Discord.Commands;
using System;
using System.Threading.Tasks;

namespace Sanakan.TypeReaders
{
    public class ItemCountPairTypeReader : TypeReader
    {
        public override Task<TypeReaderResult> ReadAsync(ICommandContext context, string input, IServiceProvider services)
        {
            if (!string.IsNullOrEmpty(input))
            {
                var ret = new Services.PocketWaifu.ItemCountPair
                {
                    Force = input.StartsWith('!')
                };

                var spr = input.Split(':');
                if (spr.Length > 0 && uint.TryParse(spr[0].AsSpan().Slice(ret.Force ? 1 : 0), out ret.Item))
                {
                    if (spr.Length > 1 && uint.TryParse(spr[1], out var ic))
                        ret.Count = ic;

                    return Task.FromResult(TypeReaderResult.FromSuccess(ret));
                }
            }
            return Task.FromResult(TypeReaderResult.FromError(CommandError.ParseFailed, "Nie rozpoznano wartości!"));
        }
    }
}