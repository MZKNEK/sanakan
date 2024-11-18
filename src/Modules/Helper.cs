#pragma warning disable 1591

using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Sanakan.Config;
using Sanakan.Extensions;
using Sanakan.Preconditions;
using Sanakan.Services;
using Sanakan.Services.Commands;
using Sanakan.Services.Session;
using Sanakan.Services.Session.Models;
using Sanakan.Services.Time;
using Shinden.Logger;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Sanakan.Modules
{
    [Name("Ogólne")]
    public class Helper : SanakanModuleBase<SocketCommandContext>
    {
        private class FixableHosting
        {
            public string Name;
            public bool Enabled;
            public DateTime Threshold;
            public DomainData[] Host;
        }

        private readonly FixableHosting[] _fixableHostings =
        {
            new FixableHosting { Name = "imgur",   Enabled = true, Threshold = new DateTime(2024, 10, 15),  Host = new []{ new DomainData("imgur.com") } },
            new FixableHosting { Name = "imgur i",   Enabled = true, Threshold = new DateTime(2024, 10, 15),  Host = new []{ new DomainData("i.imgur.com") } },
            new FixableHosting { Name = "discord cdn", Enabled = true, Threshold = new DateTime(2023, 11, 13), Host = new []{ new DomainData("cdn.discordapp.com") } },
            new FixableHosting { Name = "discord madia", Enabled = true, Threshold = new DateTime(2023, 11, 13), Host = new []{ new DomainData("media.discordapp.net") } },
            new FixableHosting { Name = "google",  Enabled = true, Threshold = new DateTime(2024, 02, 13), Host = new []{ new DomainData("drive.google.com") } },
        };

        private Services.PocketWaifu.Waifu _waifu;
        private SessionManager _session;
        private Services.Helper _helper;
        private Moderator _moderation;
        private ImageProcessing _img;
        private ISystemTime _time;
        private ILogger _logger;
        private IConfig _config;

        public Helper(Services.Helper helper, Services.Moderator moderation, SessionManager session,
            ILogger logger, IConfig config, Services.PocketWaifu.Waifu  waifu, ISystemTime time, ImageProcessing img)
        {
            _moderation = moderation;
            _session = session;
            _helper = helper;
            _logger = logger;
            _config = config;
            _waifu = waifu;
            _time = time;
            _img = img;
        }

        [Command("pomoc", RunMode = RunMode.Async)]
        [Alias("h", "help")]
        [Summary("wyświetla listę poleceń")]
        [Remarks("odcinki"), RequireAnyCommandChannelOrLevel(20)]
        public async Task GiveHelpAsync([Summary("nazwa polecenia")][Remainder] string command = null)
        {
            var gUser = Context.User as SocketGuildUser;
            if (gUser == null) return;

            if (command != null)
            {
                try
                {
                    bool admin = false;
                    bool dev = false;

                    string prefix = _config.Get().Prefix;
                    if (Context.Guild != null)
                    {
                        using (var db = new Database.DatabaseContext(_config))
                        {
                            var gConfig = await db.GetCachedGuildFullConfigAsync(Context.Guild.Id);
                            if (gConfig?.Prefix != null) prefix = gConfig.Prefix;

                            admin = (gUser.Roles.Any(x => x.Id == gConfig?.AdminRole) || gUser.GuildPermissions.Administrator);
                            dev = _config.Get().Dev.Any(x => x == gUser.Id);
                        }
                    }

                    await SafeReplyAsync(_helper.GiveHelpAboutPublicCmd(command, prefix, admin, dev));
                }
                catch (Exception ex)
                {
                    await SafeReplyAsync("", embed: ex.Message.ToEmbedMessage(EMType.Error).Build());
                }

                return;
            }

            await SafeReplyAsync(_helper.GivePublicHelp());
        }

        [Command("ktoto", RunMode = RunMode.Async)]
        [Alias("whois")]
        [Summary("wyświetla informacje o użytkowniku")]
        [Remarks("Dzida"), RequireCommandChannel]
        public async Task GiveUserInfoAsync([Summary("nazwa użytkownika")] SocketUser user = null)
        {
            var usr = (user ?? Context.User) as SocketGuildUser;
            if (usr == null)
            {
                await SafeReplyAsync("", embed: "Polecenie działa tylko z poziomu serwera.".ToEmbedMessage(EMType.Info).Build());
                return;
            }

            await SafeReplyAsync("", embed: _helper.GetInfoAboutUser(usr));
        }

        [Command("ping", RunMode = RunMode.Async)]
        [Summary("sprawdza opóźnienie między botem a serwerem")]
        [Remarks(""), RequireCommandChannel]
        public async Task GivePingAsync()
        {
            int latency = Context.Client.Latency;

            EMType type = EMType.Error;
            if (latency < 400) type = EMType.Warning;
            if (latency < 200) type = EMType.Success;

            await SafeReplyAsync("", embed: $"Pong! `{latency}ms`".ToEmbedMessage(type).Build());
        }

        [Command("serwerinfo", RunMode = RunMode.Async)]
        [Alias("serverinfo", "sinfo")]
        [Summary("wyświetla informacje o serwerze")]
        [Remarks(""), RequireCommandChannel]
        public async Task GiveServerInfoAsync()
        {
            if (Context.Guild == null)
            {
                await SafeReplyAsync("", embed: "Polecenie działa tylko z poziomu serwera.".ToEmbedMessage(EMType.Info).Build());
                return;
            }

            await SafeReplyAsync("", embed: _helper.GetInfoAboutServer(Context.Guild));
        }

        [Command("awatar", RunMode = RunMode.Async)]
        [Alias("avatar", "pfp")]
        [Summary("wyświetla awatar użytkownika")]
        [Remarks("Dzida"), RequireCommandChannel]
        public async Task ShowUserAvatarAsync([Summary("nazwa użytkownika")] SocketUser user = null, [Summary("awatar serwera?")] bool fromGuild = false)
        {
            var usr = (user ?? Context.User);
            var embed = new EmbedBuilder
            {
                ImageUrl = usr.GetUserOrDefaultAvatarUrl(fromGuild),
                Author = new EmbedAuthorBuilder().WithUser(usr),
                Color = EMType.Info.Color(),
            };

            await SafeReplyAsync("", embed: embed.Build());
        }

        [Command("info", RunMode = RunMode.Async)]
        [Summary("wyświetla informacje o bocie")]
        [Remarks("")]
        public async Task GiveBotInfoAsync()
        {
            using (var proc = System.Diagnostics.Process.GetCurrentProcess())
            {
                var info = new System.Text.StringBuilder()
                .AppendLine($"**Sanakan ({typeof(Sanakan).Assembly.GetName().Version})**:")
                .AppendLine($"**Czas działania**: `{_time.Now() - proc.StartTime:d'd 'hh\\:mm\\:ss}`")
                .AppendLine()
                .Append("[Strona](https://sanakan.pl/) | ")
                .Append("[GitHub](https://github.com/MZKNEK/sanakan.git) | ")
                .Append("[Wiki](https://wiki.sanakan.pl/) | ")
                .Append("[Karty](https://waifu.sanakan.pl/#/)");

                await SafeReplyAsync("", embed: info.ToString().ToEmbedMessage(EMType.Bot).Build());
            }
        }

        [Command("napraw ramke")]
        [Alias("fix border")]
        [Summary("naprawia wygasły obrazek ramki")]
        [Remarks("123123"), RequireWaifuCommandChannel]
        public async Task FixCardCustomBorderImageAsync([Summary("WID")] ulong wid, [Summary("bezpośredni adres do obrazka")] string url)
        {
            var imgCheck = await _img.CheckImageUrlAsync(url);
            if (imgCheck.IsError())
            {
                await SafeReplyAsync("", embed: ExecutionResult.From(imgCheck).ToEmbedMessage($"{Context.User.Mention} ").Build());
                return;
            }

            using (var db = new Database.DatabaseContext(Config))
            {
                var botuser = await db.GetUserOrCreateAsync(Context.User.Id);
                var thisCard = botuser.GetCard(wid);
                if (thisCard == null)
                {
                    await SafeReplyAsync("", embed: $"{Context.User.Mention} nie odnaleziono karty.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (thisCard.CustomBorder == null)
                {
                    await SafeReplyAsync("", embed: $"{Context.User.Mention} ta karta nie ma ustawionego niestandardowego obrazka ramki.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (Dir.IsLocal(thisCard.CustomBorder))
                {
                    await SafeReplyAsync("", embed: $"{Context.User.Mention} ten obrazek jest przechowywany lokalnie.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if ((await _helper.GetResponseFromUrl(thisCard.CustomBorder)) == System.Net.HttpStatusCode.OK)
                {
                    await SafeReplyAsync("", embed: $"{Context.User.Mention} obecnie ustawiony adres do niestandardowego obrazka ramki jest poprawny.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                _ = await _img.SaveCardBorderImageFromUrlAsync(imgCheck.Url, thisCard);

                await db.SaveChangesAsync();

                _waifu.DeleteCardImageIfExist(thisCard);

                await SafeReplyAsync("", embed: $"{Context.User.Mention} obrazek ramki został poprawiony!".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("napraw obrazek")]
        [Alias("fix image")]
        [Summary("naprawia wygasły obrazek karty")]
        [Remarks("123123"), RequireWaifuCommandChannel]
        public async Task FixCardCustomImageAsync([Summary("WID")] ulong wid, [Summary("bezpośredni adres do obrazka")] string url)
        {
            var imgCheck = await _img.CheckImageUrlAsync(url);
            if (imgCheck.IsError())
            {
                await SafeReplyAsync("", embed: ExecutionResult.From(imgCheck).ToEmbedMessage($"{Context.User.Mention} ").Build());
                return;
            }

            if (!_fixableHostings.Any(x => x.Enabled))
            {
                await SafeReplyAsync("", embed: $"{Context.User.Mention} obecnie funkcja naprawiania obrazków jest wyłączona.".ToEmbedMessage(EMType.Error).Build());
                return;
            }

            using (var db = new Database.DatabaseContext(Config))
            {
                var botuser = await db.GetUserOrCreateAsync(Context.User.Id);
                var thisCard = botuser.GetCard(wid);
                if (thisCard == null)
                {
                    await SafeReplyAsync("", embed: $"{Context.User.Mention} nie odnaleziono karty.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (thisCard.CustomImage == null)
                {
                    await SafeReplyAsync("", embed: $"{Context.User.Mention} ta karta nie ma ustawionego niestandardowego obrazka.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (Dir.IsLocal(thisCard.CustomImage))
                {
                    await SafeReplyAsync("", embed: $"{Context.User.Mention} ten obrazek jest przechowywany lokalnie.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var hostingData = _fixableHostings.FirstOrDefault(x => x.Enabled && _img.IsUrlFromHost(thisCard.CustomImage, x.Host));
                if (hostingData == null)
                {
                    var hosts = string.Join(' ', _fixableHostings.Where(x => x.Enabled).Select(x => x.Name));
                    await SafeReplyAsync("", embed: $"{Context.User.Mention} ta karta nie spełnia wymogów polecenia. Hosting obrazka jest niepoprawny. Dozwolone do naprawy to: {hosts}".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (thisCard.CustomImageDate >= hostingData.Threshold)
                {
                    await SafeReplyAsync("", embed: $"{Context.User.Mention} ta karta nie spełnia wymogów polecenia. Obrazek został ustawiony za późno.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (thisCard.FixedCustomImageCnt > 2)
                {
                    await SafeReplyAsync("", embed: $"{Context.User.Mention} na tej karcie już trzy razy został naprawiony niestandardowy obrazek.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if ((await _helper.GetResponseFromUrl(thisCard.CustomImage)) == System.Net.HttpStatusCode.OK)
                {
                    await SafeReplyAsync("", embed: $"{Context.User.Mention} obecnie ustawiony adres do niestandardowego obrazka jest poprawny.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                _ = await _img.SaveCardImageFromUrlAsync(imgCheck.Url, thisCard);

                thisCard.FixedCustomImageCnt++;
                thisCard.CustomImageDate = _time.Now();

                await db.SaveChangesAsync();

                _waifu.DeleteCardImageIfExist(thisCard);

                await SafeReplyAsync("", embed: $"{Context.User.Mention} obrazek został poprawiony!".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("zgłoś", RunMode = RunMode.Async)]
        [Alias("raport", "report", "zgłos", "zglos", "zgloś")]
        [Summary("zgłasza wiadomość użytkownika")]
        [Remarks("Tak nie wolno!"), RequireUserRole]
        public async Task ReportUserSimpleAsync([Summary("powód")][Remainder] string reason)
        {
            if (Context.Message.Reference != null && Context.Message.Reference.MessageId.IsSpecified)
            {
                await ReportUserAsync(Context.Message.Reference.MessageId.Value, reason);
            }
            else
            {
                await Context.Message.DeleteAsync();
                await SafeReplyAsync("", embed: "Należy podać id wiadomości.".ToEmbedMessage(EMType.Error).Build());
            }
        }

        [Command("zgłoś", RunMode = RunMode.Async)]
        [Alias("raport", "report", "zgłos", "zglos", "zgloś")]
        [Summary("zgłasza wiadomość użytkownika")]
        [Remarks("63312335634561 Tak nie wolno!"), RequireUserRole]
        public async Task ReportUserAsync([Summary("id wiadomości")] ulong messageId, [Summary("powód")][Remainder] string reason)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var config = await db.GetCachedGuildFullConfigAsync(Context.Guild.Id);
                if (config == null)
                {
                    await SafeReplyAsync("", embed: "Serwer nie jest jeszcze skonfigurowany.".ToEmbedMessage(EMType.Bot).Build());
                    return;
                }

                var raportCh = Context.Guild.GetTextChannel(config.RaportChannel);
                if (raportCh == null)
                {
                    await SafeReplyAsync("", embed: "Serwer nie ma skonfigurowanych raportów.".ToEmbedMessage(EMType.Bot).Build());
                    return;
                }

                await Context.Message.DeleteAsync();

                var repMsg = await Context.Channel.GetMessageAsync(messageId);
                if (repMsg == null)
                {
                    await SafeReplyAsync("", embed: "Nie odnaleziono wiadomości.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (repMsg.Author.IsBot || repMsg.Author.IsWebhook)
                {
                    await SafeReplyAsync("", embed: "Raportować bota? Bez sensu.".ToEmbedMessage(EMType.Bot).Build());
                    return;
                }

                if ((_time.Now() - repMsg.CreatedAt.DateTime.ToLocalTime()).TotalHours > 3)
                {
                    await SafeReplyAsync("", embed: "Można raportować tylko wiadomości, które nie są starsze od 3h.".ToEmbedMessage(EMType.Bot).Build());
                    return;
                }

                if (repMsg.Author.Id == Context.User.Id)
                {
                    var user = Context.User as SocketGuildUser;
                    if (user == null) return;

                    var notifChannel = Context.Guild.GetTextChannel(config.NotificationChannel);
                    var userRole = Context.Guild.GetRole(config.UserRole);
                    var muteRole = Context.Guild.GetRole(config.MuteRole);

                    if (muteRole == null)
                    {
                        await SafeReplyAsync("", embed: "Rola wyciszająca nie jest ustawiona.".ToEmbedMessage(EMType.Bot).Build());
                        return;
                    }

                    if (user.Roles.Contains(muteRole))
                    {
                        await SafeReplyAsync("", embed: $"{user.Mention} już jest wyciszony.".ToEmbedMessage(EMType.Error).Build());
                        return;
                    }

                    var session = new AcceptSession(user, null, Context.Client.CurrentUser);
                    await _session.KillSessionIfExistAsync(session);

                    var msg = await SafeReplyAsync("", embed: $"{user.Mention} raportujesz samego siebie? Może pomogę! Na pewno chcesz muta?".ToEmbedMessage(EMType.Error).Build());
                    await msg.AddReactionsAsync(session.StartReactions);
                    session.Actions = new AcceptMute(Config)
                    {
                        NotifChannel = notifChannel,
                        Moderation = _moderation,
                        MuteRole = muteRole,
                        UserRole = userRole,
                        Message = msg,
                        User = user,
                    };
                    session.Message = msg;

                    await _session.TryAddSession(session);
                    return;
                }

                await SafeReplyAsync("", embed: "Wysłano zgłoszenie.".ToEmbedMessage(EMType.Success).Build());

                string userName = $"{Context.User.Username}({Context.User.Id})";
                var sendMsg = await raportCh.SendMessageAsync($"{repMsg.GetJumpUrl()}", embed: "prep".ToEmbedMessage().Build());

                try
                {
                    await sendMsg.ModifyAsync(x => x.Embed = _helper.BuildRaportInfo(repMsg, userName, reason, sendMsg.Id));

                    var rConfig = await db.GetGuildConfigOrCreateAsync(Context.Guild.Id);
                    rConfig.Raports.Add(new Database.Models.Configuration.Raport { User = repMsg.Author.Id, Message = sendMsg.Id });
                    await db.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    _logger.Log($"in raport: {ex}");
                    await sendMsg.DeleteAsync();
                }
            }
        }
    }
}