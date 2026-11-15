#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Sanakan.Config;
using Sanakan.Extensions;

namespace Sanakan.Services
{
    public class Chaos
    {
        private DiscordSocketClient _client;
        private volatile bool _isEnabled;
        private IConfig _config;
        private ILogger _logger;
        private Timer _timer;

        public Chaos(DiscordSocketClient client, IConfig config, ILogger logger)
        {
            _client = client;
            _config = config;
            _logger = logger;
            _isEnabled = false;
            _timer = new Timer(_ =>
            {
                _isEnabled = !_isEnabled;
            },
            null,
            TimeSpan.FromMinutes(10),
            TimeSpan.FromMinutes(4));

            _client.MessageReceived += HandleMessageAsync;
        }

        private async Task HandleMessageAsync(SocketMessage message)
        {
            var msg = message as SocketUserMessage;
            if (msg == null) return;

            if (msg.Author.IsBot || msg.Author.IsWebhook) return;

            var user = msg.Author as SocketGuildUser;
            if (user == null) return;

            var config = _config.Get();
            if (config.BlacklistedGuilds.Any(x => x == user.Guild.Id))
                return;

            var prefix = config.Prefix;
            using (var db = new Database.DatabaseContext(_config))
            {
                var gConfig = await db.GetCachedGuildFullConfigAsync(user.Guild.Id);
                if (gConfig == null) return;

                if (!gConfig.ChaosMode) return;

                prefix = string.IsNullOrEmpty(gConfig.Prefix) ? prefix : gConfig.Prefix;
            }

            if ((_isEnabled || Fun.IsAF()) && Fun.TakeATry(10d) && !message.Content.IsCommand(prefix))
            {
                var botRoles = user.Guild.CurrentUser?.Roles.Select(x => x.Id).ToHashSet() ?? new HashSet<ulong>();
                var emotes = user.Guild.Emotes.Where(x => CanUse(x.IsAvailable, x.RoleIds, botRoles)).ToList();
                if (emotes.Count == 0) return;

                var emote = Fun.GetOneRandomFrom(emotes);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(Fun.GetRandomValue(1, 4)));
                        await message.AddReactionAsync(emote);
                    }
                    catch (Exception)
                    {
                        _logger.Log($"Chaos: missing emote {emote.Name}");
                    }
                });
            }
        }

        // emotes lost with a boost level are unavailable, role-locked ones need one of their roles
        internal static bool CanUse(bool? isAvailable, IReadOnlyCollection<ulong> roleIds, IReadOnlySet<ulong> botRoles)
            => isAvailable != false && (roleIds == null || roleIds.Count == 0 || roleIds.Any(botRoles.Contains));
    }
}
