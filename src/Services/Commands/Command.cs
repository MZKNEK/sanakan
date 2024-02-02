#pragma warning disable 1591

using Discord;
using Discord.Commands;
using Sanakan.Services.Executor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Sanakan.Services.Commands
{
    public class Command : IExecutable
    {
        private readonly Priority _priority;

        public Command(CommandMatch match, ParseResult result, ICommandContext context, Priority priority)
        {
            Match = match;
            Result = result;
            Context = context;
            _priority = priority;
        }

        public Priority GetPriority() => _priority;

        public string GetName() => $"cmd-{Match.Command.Name}";

        public IEnumerable<ulong> GetOwners()
        {
            var owners = new List<ulong>() { Context.User.Id };
            var users = Result.ArgValues.Where(x => x.Values.Any(c => c.Value is Discord.IUser)).SelectMany(x => x.Values);
            foreach (var user in users)
            {
                if (user.Value is Discord.IUser s)
                    owners.Add(s.Id);
            }
            return owners;
        }

        public CommandMatch Match { get; private set; }
        public ParseResult Result { get; private set; }
        public ICommandContext Context { get; private set; }

        public async Task<bool> ExecuteAsync(IServiceProvider provider)
        {
            var result = await Match.ExecuteAsync(Context, Result, provider).ConfigureAwait(false);
            if (result.IsSuccess) return true;

            throw new Exception(result.ErrorReason);
        }
    }
}
