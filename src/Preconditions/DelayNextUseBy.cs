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

        public enum ResType
        {
            Nothing, MinSec, HourMin
        }

        private readonly ResType _responseType;
        private readonly DelayMethod _method;
        private readonly TimeSpan _time;

        private static readonly ConcurrentDictionary<(string, ulong), DateTime> _entries = new ConcurrentDictionary<(string, ulong), DateTime>();
        private const int MaxEntries = 2000;
        private static readonly TimeSpan EntryMaxAge = TimeSpan.FromHours(24);

        public DelayNextUseBy(double time_min, ResType resType = ResType.MinSec, DelayMethod method = DelayMethod.PerUser)
        {
            _time = TimeSpan.FromMinutes(time_min);
            _responseType = resType;
            _method = method;
        }

        public async override Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command, IServiceProvider services)
        {
#if DEBUG
            await Task.CompletedTask;
            return PreconditionResult.FromSuccess();
#else
            var user = context.User as SocketGuildUser;
            if (user == null) return PreconditionResult.FromError($"To polecenie działa tylko z poziomu serwera.");

            if (user.GuildPermissions.Administrator)
                return PreconditionResult.FromSuccess();

            var config = (IConfig)services.GetService(typeof(IConfig));
            using (var db = new Database.DatabaseContext(config))
            {
                var gConfig = await db.GetCachedGuildFullConfigAsync(context.Guild.Id);
                if (gConfig != null)
                {
                    var role = context.Guild.GetRole(gConfig.AdminRole);
                    if (role != null)
                    {
                        if (user.Roles.Any(x => x.Id == role.Id))
                            return PreconditionResult.FromSuccess();
                    }

                    var srole = context.Guild.GetRole(gConfig.SemiAdminRole);
                    if (srole != null)
                    {
                        if (user.Roles.Any(x => x.Id == srole.Id))
                            return PreconditionResult.FromSuccess();
                    }

                    var trole = context.Guild.GetRole(gConfig.TesterRole);
                    if (trole != null)
                    {
                        if (user.Roles.Any(x => x.Id == trole.Id))
                            return PreconditionResult.FromSuccess();
                    }
                }
            }

            var tService = (ISystemTime)services.GetService(typeof(ISystemTime));
            var cmdKey = BuildKey(command.Name, _method, user.Id);
            var now = tService.Now();

            // atomowe sprawdzenie i aktualizacja licznika (CAS) - dwa równoległe wywołania nie przejdą jednocześnie
            while (true)
            {
                if (_entries.TryGetValue(cmdKey, out var lastUse))
                {
                    if (lastUse + _time > now)
                        return Denied(context, (lastUse + _time) - now);

                    if (_entries.TryUpdate(cmdKey, now, lastUse))
                        break;
                }
                else if (_entries.TryAdd(cmdKey, now))
                {
                    break;
                }
            }

            Cleanup(now);

            return PreconditionResult.FromSuccess();
#endif
        }

        private PreconditionResult Denied(ICommandContext context, TimeSpan left)
        {
            switch (_responseType)
            {
                case ResType.Nothing:
                    return PreconditionResult.FromError($"{context.User.Mention} tego polecenia możesz użyć raz na jakiś czas.");

                case ResType.HourMin:
                    var min = (int)left.TotalMinutes;
                    return PreconditionResult.FromError($"{context.User.Mention} tego polecenia możesz użyć za {min / 60}h {min % 60}m.");

                default:
                case ResType.MinSec:
                    var sec = (int)left.TotalSeconds;
                    return PreconditionResult.FromError($"{context.User.Mention} tego polecenia możesz użyć za {sec / 60}m {sec % 60}s.");
            }
        }

        private void Cleanup(DateTime now)
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

        // Global dzieli jeden klucz dla wszystkich; PerUser kluczuje po uzytkowniku
        public static (string Command, ulong User) BuildKey(string command, DelayMethod method, ulong userId)
            => (command, method == DelayMethod.PerUser ? userId : 1UL);
    }
}