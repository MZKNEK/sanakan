#pragma warning disable 1591

using Discord.Commands;
using Discord.WebSocket;
using Sanakan.Config;
using Sanakan.Extensions;
using Sanakan.Services.Time;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Sanakan.Preconditions
{
    public class DelayNextUseBy : PreconditionAttribute
    {
        public enum DelayMethod
        {
            PerUser, Global
        }

        private readonly DelayMethod _method;
        private readonly TimeSpan _time;

        private static readonly ConcurrentDictionary<(string, ulong), DateTime> _entries = new ConcurrentDictionary<(string, ulong), DateTime>();
        private const int MaxEntries = 2000;
        private static readonly TimeSpan EntryMaxAge = TimeSpan.FromHours(24);

        public DelayNextUseBy(double time_min, DelayMethod method = DelayMethod.PerUser)
        {
            _time = TimeSpan.FromMinutes(time_min);
            _method = method;
        }

        public async override Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command, IServiceProvider services)
        {
            var user = context.User as SocketGuildUser;
            if (user == null) return PreconditionResult.FromError($"To polecenie działa tylko z poziomu serwera.");

            var availableAt = await TryUseAsync(user, command.Name, _time, (IConfig)services.GetService(typeof(IConfig)),
                (ISystemTime)services.GetService(typeof(ISystemTime)), _method);

            return availableAt == null ? PreconditionResult.FromSuccess() : PreconditionResult.FromError(CooldownMessage(user, availableAt.Value));
        }

        public static string CooldownMessage(Discord.IUser user, DateTime availableAt)
            => $"{user.Mention} tego polecenia możesz znowu użyć {availableAt.ToRemTime()}.";

        // marks the use and returns null when the command can be used now, otherwise the time it will be available again
        public static async Task<DateTime?> TryUseAsync(SocketGuildUser user, string commandName, TimeSpan time, IConfig config, ISystemTime clock,
            DelayMethod method = DelayMethod.PerUser)
        {
#if DEBUG
            await Task.CompletedTask;
            return null;
#else
            if (user.GuildPermissions.Administrator)
                return null;

            using (var db = new Database.DatabaseContext(config))
            {
                var gConfig = await db.GetCachedGuildFullConfigAsync(user.Guild.Id);
                if (gConfig != null)
                {
                    var role = user.Guild.GetRole(gConfig.AdminRole);
                    if (role != null)
                    {
                        if (user.Roles.Any(x => x.Id == role.Id))
                            return null;
                    }

                    var srole = user.Guild.GetRole(gConfig.SemiAdminRole);
                    if (srole != null)
                    {
                        if (user.Roles.Any(x => x.Id == srole.Id))
                            return null;
                    }

                    var trole = user.Guild.GetRole(gConfig.TesterRole);
                    if (trole != null)
                    {
                        if (user.Roles.Any(x => x.Id == trole.Id))
                            return null;
                    }
                }
            }

            var cmdKey = BuildKey(commandName, method, user.Id);
            var now = clock.Now();

            // atomowe sprawdzenie i aktualizacja licznika (CAS) - dwa równoległe wywołania nie przejdą jednocześnie
            while (true)
            {
                if (_entries.TryGetValue(cmdKey, out var lastUse))
                {
                    if (lastUse + time > now)
                        return lastUse + time;

                    if (_entries.TryUpdate(cmdKey, now, lastUse))
                        break;
                }
                else if (_entries.TryAdd(cmdKey, now))
                {
                    break;
                }
            }

            Cleanup(now);

            return null;
#endif
        }

        // gives the cooldown back when the command did not do its job
        public static void Release(string commandName, ulong userId, DelayMethod method = DelayMethod.PerUser)
            => _entries.TryRemove(BuildKey(commandName, method, userId), out _);

        private static void Cleanup(DateTime now)
        {
            if (_entries.Count <= MaxEntries)
                return;

            // najpierw usuń wpisy starsze niż dopuszczalny wiek
            foreach (var entry in _entries)
                if (now - entry.Value > EntryMaxAge)
                    _entries.TryRemove(entry.Key, out _);

            if (_entries.Count <= MaxEntries)
                return;

            // twardy limit rozmiaru - usuń najstarsze wpisy, aby słownik nie rósł w nieskończoność
            foreach (var entry in _entries.OrderBy(x => x.Value).Take(_entries.Count - MaxEntries).ToList())
                _entries.TryRemove(entry.Key, out _);
        }

        // preconditions run before argument parsing, so a command that is not executed must give its cooldown back
        public static void Refund(CommandInfo command, ulong userId)
        {
            foreach (var delay in command.Preconditions.OfType<DelayNextUseBy>())
                _entries.TryRemove(BuildKey(command.Name, delay._method, userId), out _);
        }

        // Global dzieli jeden klucz dla wszystkich; PerUser kluczuje po uzytkowniku
        public static (string Command, ulong User) BuildKey(string command, DelayMethod method, ulong userId)
            => (command, method == DelayMethod.PerUser ? userId : 1UL);
    }
}