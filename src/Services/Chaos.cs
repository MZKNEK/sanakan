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
            Emote.Parse("<a:uuu_uuu:989276537876533348>"),
            Emote.Parse("<a:fala:1079073376926175264>"),
            Emote.Parse("<a:hanamaru:613392773273354251>"),
            Emote.Parse("<a:ohwow:538070145763901450>"),
            Emote.Parse("<a:mnie_tu_nie_bylo:467494048878559243>"),
            Emote.Parse("<a:MoonaWink:772826033896161310>"),
            Emote.Parse("<a:fruwa_mi_to:1003069441153695824>"),
            Emote.Parse("<a:pepeshirt:826132578821603448>"),
            Emote.Parse("<:nic_mnie_to_nie_obchodzi:613391974078218250>"),
            Emote.Parse("<:uwu:584839709126164646>"),
            Emote.Parse("<:Seya:605777855925977098>"),
            Emote.Parse("<:nawet_zabawne:826049216539394058>"),
            Emote.Parse("<:przykro_mi:709706569847275541>"),
            Emote.Parse("<:teraz_juz_osadzam:709706570006659148>"),
            Emote.Parse("<:eeeh:650258354517704705>"),
            Emote.Parse("<:japonski_demon_rzeczny:479708998610976788>"),
            Emote.Parse("<:ojojojojoj:483289508868259850>"),
        }.Shuffle().ToList();

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
            TimeSpan.FromMinutes(15),
            TimeSpan.FromMinutes(10));

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

            if (_isEnabled && Fun.TakeATry(8) && !message.Content.IsCommand(prefix))
            {
                var emote = Fun.GetOneRandomFrom(_emtoes);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(Fun.GetRandomValue(1, 4)));
                        await message.AddReactionAsync(emote);
                    }
                    catch (Exception)
                    {
                        _logger.Log($"Chaos: Missing emote - {emote.Name}");
                    }
                });
            }
        }
    }
}
