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
using Shinden.Logger;

namespace Sanakan.Services
{
    public class Chaos
    {
        private static List<Emote> _emtoes = new List<Emote>
        {
            Emote.Parse("<a:turlaj_bulbe:650258354358321173>"),
            Emote.Parse("<a:turlaj_chomika:973012917429600338>"),
            Emote.Parse("<a:turlaj_lame:754108144208183396>"),
            Emote.Parse("<a:turlaj_owce:613393896390656011>"),
            Emote.Parse("<a:padaczka:1058736063973179474>"),
            Emote.Parse("<a:kolejny_pedofil_wykryty:989269381538279525>"),
            Emote.Parse("<a:Confused_Dog:575413848106860544>"),
            Emote.Parse("<a:nienie:1078334400254726194>"),
            Emote.Parse("<a:nie_nie_nie:606195098169901108>"),
            Emote.Parse("<a:DameDaNe:754108167650148462>"),
            Emote.Parse("<a:okidoki:575415074399977472>"),
        };

        private DiscordSocketClient _client;
        private bool _isEnabled;
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
            TimeSpan.FromMinutes(30),
            TimeSpan.FromMinutes(10));

#if !DEBUG
            _client.MessageReceived += HandleMessageAsync;
#endif
        }

        private async Task HandleMessageAsync(SocketMessage message)
        {
            var msg = message as SocketUserMessage;
            if (msg == null) return;

            if (msg.Author.IsBot || msg.Author.IsWebhook) return;

            var user = msg.Author as SocketGuildUser;
            if (user == null) return;

            if (_config.Get().BlacklistedGuilds.Any(x => x == user.Guild.Id))
                return;

            using (var db = new Database.DatabaseContext(_config))
            {
                var gConfig = await db.GetCachedGuildFullConfigAsync(user.Guild.Id);
                if (gConfig == null) return;

                if (!gConfig.ChaosMode) return;
            }

            if (!_isEnabled) return;

            if (Fun.TakeATry(10))
            {
                var emote = Fun.GetOneRandomFrom(_emtoes);
                try
                {
                    await message.AddReactionAsync(emote);
                }
                catch (Exception)
                {
                    _logger.Log($"Chaos: Missing emote - {emote.Name}");
                }
            }
        }
    }
}
