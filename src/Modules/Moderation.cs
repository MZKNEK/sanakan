﻿#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Sanakan.Config;
using Sanakan.Database.Models;
using Sanakan.Database.Models.Management;
using Sanakan.Extensions;
using Sanakan.Preconditions;
using Sanakan.Services;
using Sanakan.Services.Commands;
using Sanakan.Services.Time;
using Shinden;
using Z.EntityFramework.Plus;

namespace Sanakan.Modules
{
    [Name("Moderacja"), Group("mod"), DontAutoLoad]
    public class Moderation : SanakanModuleBase<SocketCommandContext>
    {
        private IConfig _config;
        private ISystemTime _time;
        private Services.Helper _helper;
        private ShindenClient _shClient;
        private Services.Profile _profile;
        private Services.Moderator _moderation;

        public Moderation(Services.Helper helper, Services.Moderator moderation, Services.Profile prof,
            ShindenClient sh, IConfig config, ISystemTime time)
        {
            _time = time;
            _shClient = sh;
            _profile = prof;
            _config = config;
            _helper = helper;
            _moderation = moderation;
        }

        [Command("kasujs", RunMode = RunMode.Async)]
        [Alias("prunes")]
        [Summary("usuwa podaną wiadomość")]
        [Remarks("62342345245"), RequireAnyAdminRoleOrChannelPermission(ChannelPermission.ManageMessages)]
        public async Task DeleteMesegeAsync([Summary("id wiadomości")] ulong id = 0)
        {
            if (id == 0 && Context.Message.Reference != null && Context.Message.Reference.MessageId.IsSpecified)
                id = Context.Message.Reference.MessageId.Value;

            await Context.Message.DeleteAsync();

            if (id != 0)
            {
                var dmsg = await Context.Channel.GetMessageAsync(id);
                await dmsg.DeleteAsync();
            }
        }

        [Command("kasuj", RunMode = RunMode.Async)]
        [Alias("prune")]
        [Summary("usuwa x ostatnich wiadomości")]
        [Remarks("12"), RequireAnyAdminRoleOrChannelPermission(ChannelPermission.ManageMessages)]
        public async Task DeleteMesegesAsync([Summary("liczba wiadomości")] int count)
        {
            if (count < 1)
                return;

            await Context.Message.DeleteAsync();
            if (Context.Channel is ITextChannel channel)
            {
                var enumerable = await channel.GetMessagesAsync(count).FlattenAsync();
                try
                {
                    await channel.DeleteMessagesAsync(enumerable).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    await SafeReplyAsync("", embed: $"Wiadomości są zbyt stare.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                await SafeReplyAsync("", embed: $"Usunięto {count} ostatnich wiadomości.".ToEmbedMessage(EMType.Bot).Build());
            }
        }

        [Command("kasuju", RunMode = RunMode.Async)]
        [Alias("pruneu")]
        [Summary("usuwa wiadomości danego użytkownika")]
        [Remarks("Karna"), RequireAnyAdminRoleOrChannelPermission(ChannelPermission.ManageMessages)]
        public async Task DeleteUserMesegesAsync([Summary("nazwa użytkownika")] SocketGuildUser user)
        {
            await Context.Message.DeleteAsync();
            if (Context.Channel is ITextChannel channel)
            {
                var enumerable = await channel.GetMessagesAsync().FlattenAsync();
                var userMessages = enumerable.Where(x => x.Author == user);
                try
                {
                    await channel.DeleteMessagesAsync(userMessages).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    await SafeReplyAsync("", embed: $"Wiadomości są zbyt stare.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                await SafeReplyAsync("", embed: $"Usunięto wiadomości {user.Mention}.".ToEmbedMessage(EMType.Bot).Build());
            }
        }

        [Command("ban")]
        [Summary("banuje użytkownika")]
        [Remarks("Karna"), RequireAdminRole, Priority(1)]
        public async Task BanUserAsync([Summary("nazwa użytkownika")] SocketGuildUser user, [Summary("czas trwania w godzinach")] long duration, [Summary("powód")][Remainder] string reason = "nie podano")
        {
            if (duration < 1) return;

            using (var db = new Database.DatabaseContext(Config))
            {
                var config = await db.GetCachedGuildFullConfigAsync(Context.Guild.Id);
                if (config == null)
                {
                    await SafeReplyAsync("", embed: "Serwer nie jest poprawnie skonfigurowany.".ToEmbedMessage(EMType.Bot).Build());
                    return;
                }

                var notifChannel = Context.Guild.GetTextChannel(config.NotificationChannel);

                var usr = Context.User as SocketGuildUser;
                var info = _moderation.GetBanUserInfo(user, duration, reason);
                await _moderation.NotifyAboutPenaltyAsync(user, notifChannel, info, $"{usr.GetUserNickInGuild()}");
                await _moderation.BanUserAsync(user, info, db);
            }

            await SafeReplyAsync("", embed: $"{user.Mention} został zbanowany.".ToEmbedMessage(EMType.Success)
                .WithImageUrl(Services.Fun.GetRandomMuteReactionGif()).Build());
        }

        [Command("mute modifier")]
        [Summary("dodaje modyfikacje muta")]
        [Remarks("Karna"), RequireAnyAdminRoleOrChannelPermission(ChannelPermission.ManageRoles), Priority(1)]
        public async Task SetMuteModifierAsync([Summary("nazwa użytkownika")] SocketGuildUser user, [Summary("czas w godzinach (0 - kasacja)")] long duration, [Summary("typ (Constant/Growing)")] ModifierType type)
        {
            if (duration < 0) return;

            using (var db = new Database.DatabaseContext(Config))
            {
                var mod = await db.MuteModifiers.FirstOrDefaultAsync(x => x.User == user.Id && x.Guild == Context.Guild.Id);
                if (mod != null && duration == 0)
                {
                    db.MuteModifiers.Remove(mod);
                    await db.SaveChangesAsync();
                    await SafeReplyAsync("", embed: $"Wpis o użytkowniku {user.Mention} został skasowany.".ToEmbedMessage(EMType.Success).Build());
                    return;
                }

                if (mod == null)
                {
                    mod = new MuteModifier
                    {
                        Count = 0,
                        Type = type,
                        User = user.Id,
                        Value = duration,
                        Guild = Context.Guild.Id,
                    };
                    db.MuteModifiers.Add(mod);
                }
                else
                {
                    mod.Value = duration;
                    mod.Type = type;
                }

                await db.SaveChangesAsync();
            }

            await SafeReplyAsync("", embed: $"Wpis o użytkowniku {user.Mention} został dodany/zmieniony.".ToEmbedMessage(EMType.Success).Build());
        }

        [Command("mute")]
        [Summary("wycisza użytkownika")]
        [Remarks("Karna"), RequireAnyAdminRoleOrChannelPermission(ChannelPermission.ManageRoles), Priority(1)]
        public async Task MuteUserAsync([Summary("nazwa użytkownika")] SocketGuildUser user, [Summary("czas trwania w godzinach")] long duration, [Summary("powód")][Remainder] string reason = "nie podano")
        {
            if (duration < 1) return;

            using (var db = new Database.DatabaseContext(Config))
            {
                var config = await db.GetCachedGuildFullConfigAsync(Context.Guild.Id);
                if (config == null)
                {
                    await SafeReplyAsync("", embed: "Serwer nie jest poprawnie skonfigurowany.".ToEmbedMessage(EMType.Bot).Build());
                    return;
                }

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

                var usr = Context.User as SocketGuildUser;
                var info = await _moderation.MuteUserAsync(user, muteRole, null, userRole, db, duration, reason);
                await _moderation.NotifyAboutPenaltyAsync(user, notifChannel, info, $"{usr.GetUserNickInGuild()}");
            }

            await SafeReplyAsync("", embed: $"{user.Mention} został wyciszony.".ToEmbedMessage(EMType.Success)
                .WithImageUrl(Services.Fun.GetRandomMuteReactionGif()).Build());
        }

        [Command("mute mod")]
        [Summary("wycisza moderatora")]
        [Remarks("Karna"), RequireAdminRole, Priority(1)]
        public async Task MuteModUserAsync([Summary("nazwa użytkownika")] SocketGuildUser user, [Summary("czas trwania w godzinach")] long duration, [Summary("powód")][Remainder] string reason = "nie podano")
        {
            if (duration < 1) return;

            using (var db = new Database.DatabaseContext(Config))
            {
                var config = await db.GetCachedGuildFullConfigAsync(Context.Guild.Id);
                if (config == null)
                {
                    await SafeReplyAsync("", embed: "Serwer nie jest poprawnie skonfigurowany.".ToEmbedMessage(EMType.Bot).Build());
                    return;
                }

                var notifChannel = Context.Guild.GetTextChannel(config.NotificationChannel);
                var muteModRole = Context.Guild.GetRole(config.ModMuteRole);
                var userRole = Context.Guild.GetRole(config.UserRole);
                var muteRole = Context.Guild.GetRole(config.MuteRole);

                if (muteRole == null)
                {
                    await SafeReplyAsync("", embed: "Rola wyciszająca nie jest ustawiona.".ToEmbedMessage(EMType.Bot).Build());
                    return;
                }

                if (muteModRole == null)
                {
                    await SafeReplyAsync("", embed: "Rola wyciszająca moderatora nie jest ustawiona.".ToEmbedMessage(EMType.Bot).Build());
                    return;
                }

                if (user.Roles.Contains(muteRole))
                {
                    await SafeReplyAsync("", embed: $"{user.Mention} już jest wyciszony.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var usr = Context.User as SocketGuildUser;
                var info = await _moderation.MuteUserAsync(user, muteRole, muteModRole, userRole, db, duration, reason, config.ModeratorRoles);
                await _moderation.NotifyAboutPenaltyAsync(user, notifChannel, info, $"{usr.GetUserNickInGuild()}");
            }

            await SafeReplyAsync("", embed: $"{user.Mention} został wyciszony.".ToEmbedMessage(EMType.Success)
                .WithImageUrl(Services.Fun.GetRandomMuteReactionGif()).Build());
        }

        [Command("unmute")]
        [Summary("zdejmuje wyciszenie z użytkownika")]
        [Remarks("Karna"), RequireAnyAdminRoleOrChannelPermission(ChannelPermission.ManageRoles), Priority(1)]
        public async Task UnmuteUserAsync([Summary("nazwa użytkownika")] SocketGuildUser user)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var config = await db.GetCachedGuildFullConfigAsync(Context.Guild.Id);
                if (config == null)
                {
                    await SafeReplyAsync("", embed: "Serwer nie jest poprawnie skonfigurowany.".ToEmbedMessage(EMType.Bot).Build());
                    return;
                }

                var muteRole = Context.Guild.GetRole(config.MuteRole);
                var muteModRole = Context.Guild.GetRole(config.ModMuteRole);
                if (muteRole == null)
                {
                    await SafeReplyAsync("", embed: "Rola wyciszająca nie jest ustawiona.".ToEmbedMessage(EMType.Bot).Build());
                    return;
                }

                if (!user.Roles.Contains(muteRole))
                {
                    await SafeReplyAsync("", embed: $"{user.Mention} nie jest wyciszony.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                await _moderation.UnmuteUserAsync(user, muteRole, muteModRole, db);
            }

            await SafeReplyAsync("", embed: $"{user.Mention} już nie jest wyciszony.".ToEmbedMessage(EMType.Success).Build());
        }

        [Command("wyciszeni", RunMode = RunMode.Async)]
        [Alias("show muted")]
        [Summary("wyświetla listę wyciszonych użytkowników")]
        [Remarks(""), RequireAnyAdminRoleOrChannelPermission(ChannelPermission.ManageRoles)]
        public async Task ShowMutedUsersAsync()
        {
            using (var mdb = new Database.DatabaseContext(Config))
            {
                await SafeReplyAsync("", embed: await _moderation.GetMutedListAsync(mdb, Context));
            }
        }

        [Command("prefix")]
        [Summary("ustawia prefix serwera (nie podanie - reset)")]
        [Remarks("."), RequireAdminRole]
        public async Task SetPrefixPerServerAsync([Summary("nowy prefix")] string prefix = null)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var config = await db.GetGuildConfigOrCreateAsync(Context.Guild.Id);

                config.Prefix = prefix;
                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"config-{Context.Guild.Id}" });

                await SafeReplyAsync("", embed: $"Ustawiono `{prefix ?? "domyślny"}` prefix.".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("przywitanie")]
        [Alias("welcome")]
        [Summary("ustawia/wyświetla wiadomość przywitania")]
        [Remarks("No elo ^mention!"), RequireAdminRole]
        public async Task SetOrShowWelcomeMessageAsync([Summary("wiadomość (off - wyłączenie)")][Remainder] string messsage = null)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var config = await db.GetGuildConfigOrCreateAsync(Context.Guild.Id);
                if (messsage == null)
                {
                    await SafeReplyAsync("", embed: $"**Wiadomość powitalna:**\n\n{config?.WelcomeMessage ?? "off"}".ToEmbedMessage(EMType.Bot).Build());
                    return;
                }

                if (messsage.Length > 2000)
                {
                    await SafeReplyAsync("", embed: $"**Wiadomość jest za długa!".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                config.WelcomeMessage = messsage;
                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"config-{Context.Guild.Id}" });
            }

            await SafeReplyAsync("", embed: $"Ustawiono `{messsage}` jako wiadomość powitalną.".ToEmbedMessage(EMType.Success).Build());
        }

        [Command("przywitaniepw")]
        [Alias("welcomepw")]
        [Summary("ustawia/wyświetla wiadomośc przywitania wysyłanego na pw")]
        [Remarks("No elo ^mention!"), RequireAdminRole]
        public async Task SetOrShowWelcomeMessagePWAsync([Summary("wiadomość (off - wyłączenie)")][Remainder] string messsage = null)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var config = await db.GetGuildConfigOrCreateAsync(Context.Guild.Id);
                if (messsage == null)
                {
                    await SafeReplyAsync("", embed: $"**Wiadomość przywitalna pw:**\n\n{config?.WelcomeMessagePW ?? "off"}".ToEmbedMessage(EMType.Bot).Build());
                    return;
                }

                if (messsage.Length > 2000)
                {
                    await SafeReplyAsync("", embed: $"**Wiadomość jest za długa!".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                config.WelcomeMessagePW = messsage;
                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"config-{Context.Guild.Id}" });
            }

            await SafeReplyAsync("", embed: $"Ustawiono `{messsage}` jako wiadomość powitalną wysyłaną na pw.".ToEmbedMessage(EMType.Success).Build());
        }

        [Command("pożegnanie")]
        [Alias("pozegnanie", "goodbye")]
        [Summary("ustawia/wyświetla wiadomość pożegnalną")]
        [Remarks("Nara ^nick?"), RequireAdminRole]
        public async Task SetOrShowGoodbyeMessageAsync([Summary("wiadomość (off - wyłączenie)")][Remainder] string messsage = null)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var config = await db.GetGuildConfigOrCreateAsync(Context.Guild.Id);
                if (messsage == null)
                {
                    await SafeReplyAsync("", embed: $"**Wiadomość pożegnalna:**\n\n{config?.GoodbyeMessage ?? "off"}".ToEmbedMessage(EMType.Bot).Build());
                    return;
                }

                if (messsage.Length > 2000)
                {
                    await SafeReplyAsync("", embed: $"**Wiadomość jest za długa!".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                config.GoodbyeMessage = messsage;
                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"config-{Context.Guild.Id}" });
            }

            await SafeReplyAsync("", embed: $"Ustawiono `{messsage}` jako wiadomość pożegnalną.".ToEmbedMessage(EMType.Success).Build());
        }

        [Command("role", RunMode = RunMode.Async)]
        [Summary("wyświetla role serwera")]
        [Remarks(""), RequireAnyAdminRoleOrChannelPermission(ChannelPermission.ManageRoles)]
        public async Task ShowRolesAsync()
        {
            string tmg = "";
            var msg = new List<String>();
            foreach (var item in Context.Guild.Roles)
            {
                string mg = tmg + $"{item.Mention} `{item.Mention}`\n";
                if ((mg.Length) > 2000)
                {
                    msg.Add(tmg);
                    tmg = "";
                }
                tmg += $"{item.Mention} `{item.Mention}`\n";
            }
            msg.Add(tmg);

            foreach (var content in msg)
                await SafeReplyAsync("", embed: content.ToEmbedMessage(EMType.Bot).Build());
        }

        [Command("clean config")]
        [Summary("przeczyszcza konfiguracje serwera z usuniętych ról i kanałów")]
        [Remarks(""), RequireAdminRole]
        public async Task CleanConfigAsync([Summary("id serwera")] ulong guildId = 0)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var guild = Context.Guild;
                if (guildId != 0)
                {
                    guild = Context.Client.GetGuild(guildId);
                    if (guild == null)
                    {
                        await SafeReplyAsync("", embed: $"Nie znaleziono serwera o id `{guildId}`".ToEmbedMessage(EMType.Error).Build());
                        return;
                    }
                }

                var config = await db.GetGuildConfigOrCreateAsync(guild.Id, true);
                if (config == null)
                {
                    await SafeReplyAsync("", embed: $"Konfiguracja tego serwera nie istnieje w bazie!".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                int clearedRows = 0;
                foreach (var ch in config.ChannelsWithoutExp.ToList())
                {
                    var channel = guild.GetTextChannel(ch.Channel);
                    if (channel == null)
                    {
                        ++clearedRows;
                        config.ChannelsWithoutExp.Remove(ch);
                    }
                }

                foreach (var ch in config.ChannelsWithoutSupervision.ToList())
                {
                    var channel = guild.GetTextChannel(ch.Channel);
                    if (channel == null)
                    {
                        ++clearedRows;
                        config.ChannelsWithoutSupervision.Remove(ch);
                    }
                }

                foreach (var ch in config.CommandChannels.ToList())
                {
                    var channel = guild.GetTextChannel(ch.Channel);
                    if (channel == null)
                    {
                        ++clearedRows;
                        config.CommandChannels.Remove(ch);
                    }
                }

                foreach (var ch in config.IgnoredChannels.ToList())
                {
                    var channel = guild.GetTextChannel(ch.Channel);
                    if (channel == null)
                    {
                        ++clearedRows;
                        config.IgnoredChannels.Remove(ch);
                    }
                }

                foreach (var ch in config.WaifuConfig.CommandChannels.ToList())
                {
                    var channel = guild.GetTextChannel(ch.Channel);
                    if (channel == null)
                    {
                        ++clearedRows;
                        config.WaifuConfig.CommandChannels.Remove(ch);
                    }
                }

                foreach (var ch in config.WaifuConfig.FightChannels.ToList())
                {
                    var channel = guild.GetTextChannel(ch.Channel);
                    if (channel == null)
                    {
                        ++clearedRows;
                        config.WaifuConfig.FightChannels.Remove(ch);
                    }
                }

                foreach (var rl in config.RolesPerLevel.ToList())
                {
                    var role = guild.GetRole(rl.Role);
                    if (role == null)
                    {
                        ++clearedRows;
                        config.RolesPerLevel.Remove(rl);
                    }
                }

                foreach (var rl in config.SelfRoles.ToList())
                {
                    var role = guild.GetRole(rl.Role);
                    if (role == null)
                    {
                        ++clearedRows;
                        config.SelfRoles.Remove(rl);
                    }
                }

                if (clearedRows < 1)
                {
                    await SafeReplyAsync("", embed: $"Konfiguracja serwera nie wymaga czyszcenia!".ToEmbedMessage(EMType.Info).Build());
                    return;
                }

                await db.SaveChangesAsync();
                QueryCacheManager.ExpireTag(new string[] { $"config-{guild.Id}" });

                await SafeReplyAsync("", embed: $"Przeczyszczono konfigurację serwera: **{guild.Name}**.\n\nZostało skasowanych **{clearedRows}** wpisów z bazy.".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("config")]
        [Summary("wyświetla konfiguracje serwera")]
        [Remarks("mods"), RequireAnyAdminRole]
        public async Task ShowConfigAsync([Summary("typ")][Remainder] Services.ConfigType type = Services.ConfigType.Global)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var config = await db.GetCachedGuildFullConfigAsync(Context.Guild.Id);
                if (config == null)
                {
                    config = new Database.Models.Configuration.GuildOptions
                    {
                        SafariLimit = 50,
                        Id = Context.Guild.Id,
                        WaifuConfig = new Database.Models.Configuration.Waifu()
                    };
                    await db.Guilds.AddAsync(config);

                    await db.SaveChangesAsync();
                }

                await SafeReplyAsync("", embed: _moderation.GetConfiguration(config, Context, type).WithTitle($"Konfiguracja {Context.Guild.Name}:").Build());
            }
        }

        [Command("adminr")]
        [Summary("ustawia rolę administratora")]
        [Remarks("34125343243432"), RequireAdminRole]
        public async Task SetAdminRoleAsync([Summary("id roli")] SocketRole role)
        {
            if (role == null)
            {
                await SafeReplyAsync("", embed: "Nie odnaleziono roli na serwerze.".ToEmbedMessage(EMType.Error).Build());
                return;
            }

            using (var db = new Database.DatabaseContext(Config))
            {
                var config = await db.GetGuildConfigOrCreateAsync(Context.Guild.Id);
                if (config.AdminRole == role.Id)
                {
                    await SafeReplyAsync("", embed: $"Rola {role.Mention} już jest ustawiona jako rola administratora.".ToEmbedMessage(EMType.Bot).Build());
                    return;
                }

                config.AdminRole = role.Id;
                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"config-{Context.Guild.Id}" });

                await SafeReplyAsync("", embed: $"Ustawiono {role.Mention} jako rolę administratora.".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("sadminr")]
        [Summary("ustawia role semi administratora")]
        [Remarks("34125343243432"), RequireAdminRole]
        public async Task SetSemiAdminRoleAsync([Summary("id roli")] SocketRole role)
        {
            if (role == null)
            {
                await SafeReplyAsync("", embed: "Nie odnaleziono roli na serwerze.".ToEmbedMessage(EMType.Error).Build());
                return;
            }

            using (var db = new Database.DatabaseContext(Config))
            {
                var config = await db.GetGuildConfigOrCreateAsync(Context.Guild.Id);
                if (config.SemiAdminRole == role.Id)
                {
                    await SafeReplyAsync("", embed: $"Rola {role.Mention} już jest ustawiona jako rola semi administratora.".ToEmbedMessage(EMType.Bot).Build());
                    return;
                }

                config.SemiAdminRole = role.Id;
                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"config-{Context.Guild.Id}" });

                await SafeReplyAsync("", embed: $"Ustawiono {role.Mention} jako rolę semi administratora.".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("testr")]
        [Summary("ustawia role testera")]
        [Remarks("34125343243432"), RequireAdminRole]
        public async Task SetTesterRoleAsync([Summary("id roli")] SocketRole role)
        {
            if (role == null)
            {
                await SafeReplyAsync("", embed: "Nie odnaleziono roli na serwerze.".ToEmbedMessage(EMType.Error).Build());
                return;
            }

            using (var db = new Database.DatabaseContext(Config))
            {
                var config = await db.GetGuildConfigOrCreateAsync(Context.Guild.Id);
                if (config.TesterRole == role.Id)
                {
                    await SafeReplyAsync("", embed: $"Rola {role.Mention} już jest ustawiona jako rola testera.".ToEmbedMessage(EMType.Bot).Build());
                    return;
                }

                config.TesterRole = role.Id;
                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"config-{Context.Guild.Id}" });

                await SafeReplyAsync("", embed: $"Ustawiono {role.Mention} jako rolę testera.".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("userr")]
        [Summary("ustawia role użytkownika")]
        [Remarks("34125343243432"), RequireAdminRole]
        public async Task SetUserRoleAsync([Summary("id roli")] SocketRole role)
        {
            if (role == null)
            {
                await SafeReplyAsync("", embed: "Nie odnaleziono roli na serwerze.".ToEmbedMessage(EMType.Error).Build());
                return;
            }

            using (var db = new Database.DatabaseContext(Config))
            {
                var config = await db.GetGuildConfigOrCreateAsync(Context.Guild.Id);
                if (config.UserRole == role.Id)
                {
                    await SafeReplyAsync("", embed: $"Rola {role.Mention} już jest ustawiona jako rola użytkownika.".ToEmbedMessage(EMType.Bot).Build());
                    return;
                }

                config.UserRole = role.Id;
                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"config-{Context.Guild.Id}" });

                await SafeReplyAsync("", embed: $"Ustawiono {role.Mention} jako rolę użytkownika.".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("nitror")]
        [Summary("ustawia role nitro")]
        [Remarks("34125343243432"), RequireAdminRole]
        public async Task SetNitroRoleAsync([Summary("id roli")] SocketRole role)
        {
            if (role == null)
            {
                await SafeReplyAsync("", embed: "Nie odnaleziono roli na serwerze.".ToEmbedMessage(EMType.Error).Build());
                return;
            }

            using (var db = new Database.DatabaseContext(Config))
            {
                var config = await db.GetGuildConfigOrCreateAsync(Context.Guild.Id);
                if (config.NitroRole == role.Id)
                {
                    await SafeReplyAsync("", embed: $"Rola {role.Mention} już jest ustawiona jako rola nitro.".ToEmbedMessage(EMType.Bot).Build());
                    return;
                }

                config.NitroRole = role.Id;
                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"config-{Context.Guild.Id}" });

                await SafeReplyAsync("", embed: $"Ustawiono {role.Mention} jako rolę nitro.".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("muter")]
        [Summary("ustawia rolę wyciszającą użytkownika")]
        [Remarks("34125343243432"), RequireAdminRole]
        public async Task SetMuteRoleAsync([Summary("id roli")] SocketRole role)
        {
            if (role == null)
            {
                await SafeReplyAsync("", embed: "Nie odnaleziono roli na serwerze.".ToEmbedMessage(EMType.Error).Build());
                return;
            }

            using (var db = new Database.DatabaseContext(Config))
            {
                var config = await db.GetGuildConfigOrCreateAsync(Context.Guild.Id);
                if (config.MuteRole == role.Id)
                {
                    await SafeReplyAsync("", embed: $"Rola {role.Mention} już jest ustawiona jako rola wyciszająca użytkownika.".ToEmbedMessage(EMType.Bot).Build());
                    return;
                }

                config.MuteRole = role.Id;
                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"config-{Context.Guild.Id}" });

                await SafeReplyAsync("", embed: $"Ustawiono {role.Mention} jako rolę wyciszającą użytkownika.".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("mutemodr")]
        [Summary("ustawia rolę wyciszającą moderatora")]
        [Remarks("34125343243432"), RequireAdminRole]
        public async Task SetMuteModRoleAsync([Summary("id roli")] SocketRole role)
        {
            if (role == null)
            {
                await SafeReplyAsync("", embed: "Nie odnaleziono roli na serwerze.".ToEmbedMessage(EMType.Error).Build());
                return;
            }

            using (var db = new Database.DatabaseContext(Config))
            {
                var config = await db.GetGuildConfigOrCreateAsync(Context.Guild.Id);
                if (config.ModMuteRole == role.Id)
                {
                    await SafeReplyAsync("", embed: $"Rola {role.Mention} już jest ustawiona jako rola wyciszająca moderatora.".ToEmbedMessage(EMType.Bot).Build());
                    return;
                }

                config.ModMuteRole = role.Id;
                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"config-{Context.Guild.Id}" });

                await SafeReplyAsync("", embed: $"Ustawiono {role.Mention} jako rolę wyciszającą moderatora.".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("globalr")]
        [Summary("ustawia rolę globalnych emotek")]
        [Remarks("34125343243432"), RequireAdminRole]
        public async Task SetGlobalRoleAsync([Summary("id roli")] SocketRole role)
        {
            if (role == null)
            {
                await SafeReplyAsync("", embed: "Nie odnaleziono roli na serwerze.".ToEmbedMessage(EMType.Error).Build());
                return;
            }

            using (var db = new Database.DatabaseContext(Config))
            {
                var config = await db.GetGuildConfigOrCreateAsync(Context.Guild.Id);
                if (config.GlobalEmotesRole == role.Id)
                {
                    await SafeReplyAsync("", embed: $"Rola {role.Mention} już jest ustawiona jako rola globalnych emotek.".ToEmbedMessage(EMType.Bot).Build());
                    return;
                }

                config.GlobalEmotesRole = role.Id;
                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"config-{Context.Guild.Id}" });

                await SafeReplyAsync("", embed: $"Ustawiono {role.Mention} jako rolę globalnych emotek.".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("waifur")]
        [Summary("ustawia rolę waifu")]
        [Remarks("34125343243432"), RequireAdminRole]
        public async Task SetWaifuRoleAsync([Summary("id roli")] SocketRole role)
        {
            if (role == null)
            {
                await SafeReplyAsync("", embed: "Nie odnaleziono roli na serwerze.".ToEmbedMessage(EMType.Error).Build());
                return;
            }

            using (var db = new Database.DatabaseContext(Config))
            {
                var config = await db.GetGuildConfigOrCreateAsync(Context.Guild.Id);
                if (config.WaifuRole == role.Id)
                {
                    await SafeReplyAsync("", embed: $"Rola {role.Mention} już jest ustawiona jako rola waifu.".ToEmbedMessage(EMType.Bot).Build());
                    return;
                }

                config.WaifuRole = role.Id;
                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"config-{Context.Guild.Id}" });

                await SafeReplyAsync("", embed: $"Ustawiono {role.Mention} jako rolę waifu.".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("modr")]
        [Summary("ustawia rolę moderatora")]
        [Remarks("34125343243432"), RequireAdminRole]
        public async Task SetModRoleAsync([Summary("id roli")] SocketRole role)
        {
            if (role == null)
            {
                await SafeReplyAsync("", embed: "Nie odnaleziono roli na serwerze.".ToEmbedMessage(EMType.Error).Build());
                return;
            }

            using (var db = new Database.DatabaseContext(Config))
            {
                var config = await db.GetGuildConfigOrCreateAsync(Context.Guild.Id);

                var rol = config.ModeratorRoles.FirstOrDefault(x => x.Role == role.Id);
                if (rol != null)
                {
                    config.ModeratorRoles.Remove(rol);
                    await db.SaveChangesAsync();

                    QueryCacheManager.ExpireTag(new string[] { $"config-{Context.Guild.Id}" });

                    await SafeReplyAsync("", embed: $"Usunięto {role.Mention} z listy roli moderatorów.".ToEmbedMessage(EMType.Success).Build());
                    return;
                }

                rol = new Database.Models.Configuration.ModeratorRoles { Role = role.Id };
                config.ModeratorRoles.Add(rol);
                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"config-{Context.Guild.Id}" });

                await SafeReplyAsync("", embed: $"Ustawiono {role.Mention} jako rolę moderatora.".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("addur")]
        [Summary("dodaje nową rolę na poziom")]
        [Remarks("34125343243432 130"), RequireAdminRole]
        public async Task SetUselessRoleAsync([Summary("id roli")] SocketRole role, [Summary("poziom")] uint level)
        {
            if (role == null)
            {
                await SafeReplyAsync("", embed: "Nie odnaleziono roli na serwerze.".ToEmbedMessage(EMType.Error).Build());
                return;
            }

            using (var db = new Database.DatabaseContext(Config))
            {
                var config = await db.GetGuildConfigOrCreateAsync(Context.Guild.Id);

                var rol = config.RolesPerLevel.FirstOrDefault(x => x.Role == role.Id);
                if (rol != null)
                {
                    config.RolesPerLevel.Remove(rol);
                    await db.SaveChangesAsync();

                    QueryCacheManager.ExpireTag(new string[] { $"config-{Context.Guild.Id}" });

                    await SafeReplyAsync("", embed: $"Usunięto {role.Mention} z listy roli na poziom.".ToEmbedMessage(EMType.Success).Build());
                    return;
                }

                rol = new Database.Models.Configuration.LevelRole { Role = role.Id, Level = level };
                config.RolesPerLevel.Add(rol);
                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"config-{Context.Guild.Id}" });

                await SafeReplyAsync("", embed: $"Ustawiono {role.Mention} jako rolę na poziom `{level}`.".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("selfrole")]
        [Summary("dodaje/usuwa role do automatycznego zarządzania")]
        [Remarks("34125343243432 newsy"), RequireAnyAdminRole]
        public async Task SetSelfRoleAsync([Summary("id roli")] SocketRole role, [Summary("nazwa")][Remainder] string name = null)
        {
            if (role == null)
            {
                await SafeReplyAsync("", embed: "Nie odnaleziono roli na serwerze.".ToEmbedMessage(EMType.Error).Build());
                return;
            }

            using (var db = new Database.DatabaseContext(Config))
            {
                var config = await db.GetGuildConfigOrCreateAsync(Context.Guild.Id);

                var rol = config.SelfRoles.FirstOrDefault(x => x.Role == role.Id);
                if (rol != null)
                {
                    config.SelfRoles.Remove(rol);
                    await db.SaveChangesAsync();

                    QueryCacheManager.ExpireTag(new string[] { $"config-{Context.Guild.Id}" });

                    await SafeReplyAsync("", embed: $"Usunięto {role.Mention} z listy roli automatycznego zarządzania.".ToEmbedMessage(EMType.Success).Build());
                    return;
                }

                if (name == null)
                {
                    await SafeReplyAsync("", embed: "Nie podano nazwy roli.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                rol = new Database.Models.Configuration.SelfRole { Role = role.Id, Name = name };
                config.SelfRoles.Add(rol);
                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"config-{Context.Guild.Id}" });

                await SafeReplyAsync("", embed: $"Ustawiono {role.Mention} jako rolę automatycznego zarządzania: `{name}`.".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("myland"), RequireAdminRole]
        [Summary("dodaje nową krainę")]
        [Remarks("34125343243432 64325343243432 Kopacze")]
        public async Task AddMyLandRoleAsync([Summary("id roli")] SocketRole manager, [Summary("id roli")] SocketRole underling = null, [Summary("nazwa krainy")][Remainder] string name = null)
        {
            if (manager == null)
            {
                await SafeReplyAsync("", embed: "Nie odnaleziono roli na serwerze.".ToEmbedMessage(EMType.Error).Build());
                return;
            }

            using (var db = new Database.DatabaseContext(Config))
            {
                var config = await db.GetGuildConfigOrCreateAsync(Context.Guild.Id);

                var land = config.Lands.FirstOrDefault(x => x.Manager == manager.Id);
                if (land != null)
                {
                    await SafeReplyAsync("", embed: $"Usunięto {land.Name}.".ToEmbedMessage(EMType.Success).Build());

                    config.Lands.Remove(land);
                    await db.SaveChangesAsync();

                    QueryCacheManager.ExpireTag(new string[] { $"config-{Context.Guild.Id}" });
                    return;
                }

                if (underling == null)
                {
                    await SafeReplyAsync("", embed: "Nie odnaleziono roli na serwerze.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (string.IsNullOrEmpty(name))
                {
                    await SafeReplyAsync("", embed: "Nazwa nie może być pusta.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (manager.Id == underling.Id)
                {
                    await SafeReplyAsync("", embed: "Rola właściciela nie może być taka sama jak podwładnego.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                land = new Database.Models.Configuration.MyLand
                {
                    Manager = manager.Id,
                    Underling = underling.Id,
                    Name = name
                };

                config.Lands.Add(land);
                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"config-{Context.Guild.Id}" });

                await SafeReplyAsync("", embed: $"Dodano {land.Name} z właścicielem {manager.Mention} i podwładnym {underling.Mention}.".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("logch")]
        [Summary("ustawia kanał logowania usuniętych wiadomości")]
        [Remarks(""), RequireAdminRole]
        public async Task SetLogChannelAsync()
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var config = await db.GetGuildConfigOrCreateAsync(Context.Guild.Id);
                if (config.LogChannel == Context.Channel.Id)
                {
                    await SafeReplyAsync("", embed: $"Kanał `{Context.Channel.Name}` już jest ustawiony jako kanał logowania usuniętych wiadomości.".ToEmbedMessage(EMType.Bot).Build());
                    return;
                }

                config.LogChannel = Context.Channel.Id;
                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"config-{Context.Guild.Id}" });
            }

            await SafeReplyAsync("", embed: $"Ustawiono `{Context.Channel.Name}` jako kanał logowania usuniętych wiadomości.".ToEmbedMessage(EMType.Success).Build());
        }

        [Command("helloch")]
        [Summary("ustawia kanał witania nowych użytkowników")]
        [Remarks(""), RequireAdminRole]
        public async Task SetGreetingChannelAsync()
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var config = await db.GetGuildConfigOrCreateAsync(Context.Guild.Id);
                if (config.GreetingChannel == Context.Channel.Id)
                {
                    await SafeReplyAsync("", embed: $"Kanał `{Context.Channel.Name}` już jest ustawiony jako kanał witania nowych użytkowników.".ToEmbedMessage(EMType.Bot).Build());
                    return;
                }

                config.GreetingChannel = Context.Channel.Id;
                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"config-{Context.Guild.Id}" });
            }

            await SafeReplyAsync("", embed: $"Ustawiono `{Context.Channel.Name}` jako kanał witania nowych użytkowników.".ToEmbedMessage(EMType.Success).Build());
        }

        [Command("notifch")]
        [Summary("ustawia kanał powiadomień o karach")]
        [Remarks(""), RequireAdminRole]
        public async Task SetNotifChannelAsync()
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var config = await db.GetGuildConfigOrCreateAsync(Context.Guild.Id);
                if (config.NotificationChannel == Context.Channel.Id)
                {
                    await SafeReplyAsync("", embed: $"Kanał `{Context.Channel.Name}` już jest ustawiony jako kanał powiadomień o karach.".ToEmbedMessage(EMType.Bot).Build());
                    return;
                }

                config.NotificationChannel = Context.Channel.Id;
                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"config-{Context.Guild.Id}" });
            }

            await SafeReplyAsync("", embed: $"Ustawiono `{Context.Channel.Name}` jako kanał powiadomień o karach.".ToEmbedMessage(EMType.Success).Build());
        }

        [Command("raportch")]
        [Summary("ustawia kanał raportów")]
        [Remarks(""), RequireAdminRole]
        public async Task SetRaportChannelAsync()
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var config = await db.GetGuildConfigOrCreateAsync(Context.Guild.Id);
                if (config.RaportChannel == Context.Channel.Id)
                {
                    await SafeReplyAsync("", embed: $"Kanał `{Context.Channel.Name}` już jest ustawiony jako kanał raportów.".ToEmbedMessage(EMType.Bot).Build());
                    return;
                }

                config.RaportChannel = Context.Channel.Id;
                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"config-{Context.Guild.Id}" });
            }

            await SafeReplyAsync("", embed: $"Ustawiono `{Context.Channel.Name}` jako kanał raportów.".ToEmbedMessage(EMType.Success).Build());
        }

        [Command("quizch")]
        [Summary("ustawia kanał quizów")]
        [Remarks(""), RequireAdminRole]
        public async Task SetQuizChannelAsync()
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var config = await db.GetGuildConfigOrCreateAsync(Context.Guild.Id);
                if (config.QuizChannel == Context.Channel.Id)
                {
                    await SafeReplyAsync("", embed: $"Kanał `{Context.Channel.Name}` już jest ustawiony jako kanał quizów.".ToEmbedMessage(EMType.Bot).Build());
                    return;
                }

                config.QuizChannel = Context.Channel.Id;
                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"config-{Context.Guild.Id}" });
            }

            await SafeReplyAsync("", embed: $"Ustawiono `{Context.Channel.Name}` jako kanał quizów.".ToEmbedMessage(EMType.Success).Build());
        }

        [Command("todoch")]
        [Summary("ustawia kanał todo")]
        [Remarks(""), RequireAdminRole]
        public async Task SetTodoChannelAsync()
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var config = await db.GetGuildConfigOrCreateAsync(Context.Guild.Id);
                if (config.ToDoChannel == Context.Channel.Id)
                {
                    await SafeReplyAsync("", embed: $"Kanał `{Context.Channel.Name}` już jest ustawiony jako kanał todo.".ToEmbedMessage(EMType.Bot).Build());
                    return;
                }

                config.ToDoChannel = Context.Channel.Id;
                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"config-{Context.Guild.Id}" });
            }

            await SafeReplyAsync("", embed: $"Ustawiono `{Context.Channel.Name}` jako kanał todo.".ToEmbedMessage(EMType.Success).Build());
        }

        [Command("nsfwch")]
        [Summary("ustawia kanał nsfw")]
        [Remarks(""), RequireAdminRole]
        public async Task SetNsfwChannelAsync()
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var config = await db.GetGuildConfigOrCreateAsync(Context.Guild.Id);
                if (config.NsfwChannel == Context.Channel.Id)
                {
                    await SafeReplyAsync("", embed: $"Kanał `{Context.Channel.Name}` już jest ustawiony jako kanał nsfw.".ToEmbedMessage(EMType.Bot).Build());
                    return;
                }

                config.NsfwChannel = Context.Channel.Id;
                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"config-{Context.Guild.Id}" });
            }

            await SafeReplyAsync("", embed: $"Ustawiono `{Context.Channel.Name}` jako kanał nsfw.".ToEmbedMessage(EMType.Success).Build());
        }

        [Command("tfightch")]
        [Summary("ustawia śmieciowy kanał walk waifu")]
        [Remarks(""), RequireAdminRole]
        public async Task SetTrashFightWaifuChannelAsync()
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var config = await db.GetGuildConfigOrCreateAsync(Context.Guild.Id);
                if (config.WaifuConfig == null)
                    config.WaifuConfig = new Database.Models.Configuration.Waifu();

                if (config.WaifuConfig.TrashFightChannel == Context.Channel.Id)
                {
                    await SafeReplyAsync("", embed: $"Kanał `{Context.Channel.Name}` już jest ustawiony jako kanał śmieciowy walk waifu.".ToEmbedMessage(EMType.Bot).Build());
                    return;
                }

                config.WaifuConfig.TrashFightChannel = Context.Channel.Id;
                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"config-{Context.Guild.Id}" });
            }

            await SafeReplyAsync("", embed: $"Ustawiono `{Context.Channel.Name}` jako kanał śmieciowy walk waifu.".ToEmbedMessage(EMType.Success).Build());
        }

        [Command("tcmdch")]
        [Summary("ustawia śmieciowy kanał poleceń waifu")]
        [Remarks(""), RequireAdminRole]
        public async Task SetTrashCmdWaifuChannelAsync()
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var config = await db.GetGuildConfigOrCreateAsync(Context.Guild.Id);
                if (config.WaifuConfig == null)
                    config.WaifuConfig = new Database.Models.Configuration.Waifu();

                if (config.WaifuConfig.TrashCommandsChannel == Context.Channel.Id)
                {
                    await SafeReplyAsync("", embed: $"Kanał `{Context.Channel.Name}` już jest ustawiony jako kanał śmieciowy poleceń waifu.".ToEmbedMessage(EMType.Bot).Build());
                    return;
                }

                config.WaifuConfig.TrashCommandsChannel = Context.Channel.Id;
                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"config-{Context.Guild.Id}" });
            }

            await SafeReplyAsync("", embed: $"Ustawiono `{Context.Channel.Name}` jako kanał śmieciowy poleceń waifu.".ToEmbedMessage(EMType.Success).Build());
        }

        [Command("tsafarich")]
        [Summary("ustawia śmieciowy kanał polowań waifu")]
        [Remarks(""), RequireAdminRole]
        public async Task SetTrashSpawnWaifuChannelAsync()
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var config = await db.GetGuildConfigOrCreateAsync(Context.Guild.Id);
                if (config.WaifuConfig == null)
                    config.WaifuConfig = new Database.Models.Configuration.Waifu();

                if (config.WaifuConfig.TrashSpawnChannel == Context.Channel.Id)
                {
                    await SafeReplyAsync("", embed: $"Kanał `{Context.Channel.Name}` już jest ustawiony jako kanał śmieciowy polowań waifu.".ToEmbedMessage(EMType.Bot).Build());
                    return;
                }

                config.WaifuConfig.TrashSpawnChannel = Context.Channel.Id;
                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"config-{Context.Guild.Id}" });
            }

            await SafeReplyAsync("", embed: $"Ustawiono `{Context.Channel.Name}` jako kanał śmieciowy polowań waifu.".ToEmbedMessage(EMType.Success).Build());
        }

        [Command("marketch")]
        [Summary("ustawia kanał rynku waifu")]
        [Remarks(""), RequireAdminRole]
        public async Task SetMarketWaifuChannelAsync()
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var config = await db.GetGuildConfigOrCreateAsync(Context.Guild.Id);
                if (config.WaifuConfig == null)
                    config.WaifuConfig = new Database.Models.Configuration.Waifu();

                if (config.WaifuConfig.MarketChannel == Context.Channel.Id)
                {
                    await SafeReplyAsync("", embed: $"Kanał `{Context.Channel.Name}` już jest ustawiony jako kanał rynku waifu.".ToEmbedMessage(EMType.Bot).Build());
                    return;
                }

                config.WaifuConfig.MarketChannel = Context.Channel.Id;
                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"config-{Context.Guild.Id}" });
            }

            await SafeReplyAsync("", embed: $"Ustawiono `{Context.Channel.Name}` jako kanał rynku waifu.".ToEmbedMessage(EMType.Success).Build());
        }

        [Command("duelch")]
        [Summary("ustawia kanał pojedynków waifu")]
        [Remarks(""), RequireAdminRole]
        public async Task SetDuelWaifuChannelAsync()
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var config = await db.GetGuildConfigOrCreateAsync(Context.Guild.Id);
                if (config.WaifuConfig == null)
                    config.WaifuConfig = new Database.Models.Configuration.Waifu();

                if (config.WaifuConfig.DuelChannel == Context.Channel.Id)
                {
                    await SafeReplyAsync("", embed: $"Kanał `{Context.Channel.Name}` już jest ustawiony jako kanał pojedynków waifu.".ToEmbedMessage(EMType.Bot).Build());
                    return;
                }

                config.WaifuConfig.DuelChannel = Context.Channel.Id;
                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"config-{Context.Guild.Id}" });
            }

            await SafeReplyAsync("", embed: $"Ustawiono `{Context.Channel.Name}` jako kanał pojedynków waifu.".ToEmbedMessage(EMType.Success).Build());
        }

        [Command("spawnch")]
        [Summary("ustawia kanał safari waifu")]
        [Remarks(""), RequireAdminRole]
        public async Task SetSafariWaifuChannelAsync()
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var config = await db.GetGuildConfigOrCreateAsync(Context.Guild.Id);
                if (config.WaifuConfig == null)
                    config.WaifuConfig = new Database.Models.Configuration.Waifu();

                if (config.WaifuConfig.SpawnChannel == Context.Channel.Id)
                {
                    await SafeReplyAsync("", embed: $"Kanał `{Context.Channel.Name}` już jest ustawiony jako kanał safari waifu.".ToEmbedMessage(EMType.Bot).Build());
                    return;
                }

                config.WaifuConfig.SpawnChannel = Context.Channel.Id;
                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"config-{Context.Guild.Id}" });
            }

            await SafeReplyAsync("", embed: $"Ustawiono `{Context.Channel.Name}` jako kanał safari waifu.".ToEmbedMessage(EMType.Success).Build());
        }

        [Command("fightch")]
        [Summary("ustawia kanał walk waifu")]
        [Remarks(""), RequireAdminRole]
        public async Task SetFightWaifuChannelAsync()
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var config = await db.GetGuildConfigOrCreateAsync(Context.Guild.Id);
                if (config.WaifuConfig == null)
                    config.WaifuConfig = new Database.Models.Configuration.Waifu();

                var chan = config.WaifuConfig.FightChannels.FirstOrDefault(x => x.Channel == Context.Channel.Id);
                if (chan != null)
                {
                    config.WaifuConfig.FightChannels.Remove(chan);
                    await db.SaveChangesAsync();

                    QueryCacheManager.ExpireTag(new string[] { $"config-{Context.Guild.Id}" });

                    await SafeReplyAsync("", embed: $"Usunięto `{Context.Channel.Name}` z listy kanałów walk waifu.".ToEmbedMessage(EMType.Success).Build());
                    return;
                }

                chan = new Database.Models.Configuration.WaifuFightChannel { Channel = Context.Channel.Id };
                config.WaifuConfig.FightChannels.Add(chan);
                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"config-{Context.Guild.Id}" });
            }

            await SafeReplyAsync("", embed: $"Ustawiono `{Context.Channel.Name}` jako kanał walk waifu.".ToEmbedMessage(EMType.Success).Build());
        }

        [Command("wcmdch")]
        [Summary("ustawia kanał poleneń waifu")]
        [Remarks(""), RequireAdminRole]
        public async Task SetCmdWaifuChannelAsync()
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var config = await db.GetGuildConfigOrCreateAsync(Context.Guild.Id);
                if (config.WaifuConfig == null)
                    config.WaifuConfig = new Database.Models.Configuration.Waifu();

                var chan = config.WaifuConfig.CommandChannels.FirstOrDefault(x => x.Channel == Context.Channel.Id);
                if (chan != null)
                {
                    config.WaifuConfig.CommandChannels.Remove(chan);
                    await db.SaveChangesAsync();

                    QueryCacheManager.ExpireTag(new string[] { $"config-{Context.Guild.Id}" });

                    await SafeReplyAsync("", embed: $"Usunięto `{Context.Channel.Name}` z listy kanałów poleceń waifu.".ToEmbedMessage(EMType.Success).Build());
                    return;
                }

                chan = new Database.Models.Configuration.WaifuCommandChannel { Channel = Context.Channel.Id };
                config.WaifuConfig.CommandChannels.Add(chan);
                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"config-{Context.Guild.Id}" });
            }

            await SafeReplyAsync("", embed: $"Ustawiono `{Context.Channel.Name}` jako kanał poleceń waifu.".ToEmbedMessage(EMType.Success).Build());
        }

        [Command("cmdch")]
        [Summary("ustawia kanał poleneń")]
        [Remarks(""), RequireAdminRole]
        public async Task SetCmdChannelAsync()
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var config = await db.GetGuildConfigOrCreateAsync(Context.Guild.Id);

                var chan = config.CommandChannels.FirstOrDefault(x => x.Channel == Context.Channel.Id);
                if (chan != null)
                {
                    config.CommandChannels.Remove(chan);
                    await db.SaveChangesAsync();

                    QueryCacheManager.ExpireTag(new string[] { $"config-{Context.Guild.Id}" });

                    await SafeReplyAsync("", embed: $"Usunięto `{Context.Channel.Name}` z listy kanałów poleceń.".ToEmbedMessage(EMType.Success).Build());
                    return;
                }

                chan = new Database.Models.Configuration.CommandChannel { Channel = Context.Channel.Id };
                config.CommandChannels.Add(chan);
                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"config-{Context.Guild.Id}" });
            }

            await SafeReplyAsync("", embed: $"Ustawiono `{Context.Channel.Name}` jako kanał poleceń.".ToEmbedMessage(EMType.Success).Build());
        }

        [Command("ignch")]
        [Summary("ustawia kanał jako ignorowany")]
        [Remarks(""), RequireAdminRole]
        public async Task SetIgnoredChannelAsync()
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var config = await db.GetGuildConfigOrCreateAsync(Context.Guild.Id);

                var chan = config.IgnoredChannels.FirstOrDefault(x => x.Channel == Context.Channel.Id);
                if (chan != null)
                {
                    config.IgnoredChannels.Remove(chan);
                    await db.SaveChangesAsync();

                    QueryCacheManager.ExpireTag(new string[] { $"config-{Context.Guild.Id}" });

                    await SafeReplyAsync("", embed: $"Usunięto `{Context.Channel.Name}` z listy kanałów ignorowanych.".ToEmbedMessage(EMType.Success).Build());
                    return;
                }

                chan = new Database.Models.Configuration.WithoutMsgCntChannel { Channel = Context.Channel.Id };
                config.IgnoredChannels.Add(chan);
                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"config-{Context.Guild.Id}" });
            }

            await SafeReplyAsync("", embed: $"Ustawiono `{Context.Channel.Name}` jako kanał ignorowany.".ToEmbedMessage(EMType.Success).Build());
        }

        [Command("noexpch")]
        [Summary("ustawia kanał bez punktów doświadczenia")]
        [Remarks(""), RequireAdminRole]
        public async Task SetNonExpChannelAsync()
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var config = await db.GetGuildConfigOrCreateAsync(Context.Guild.Id);

                var chan = config.ChannelsWithoutExp.FirstOrDefault(x => x.Channel == Context.Channel.Id);
                if (chan != null)
                {
                    config.ChannelsWithoutExp.Remove(chan);
                    await db.SaveChangesAsync();

                    QueryCacheManager.ExpireTag(new string[] { $"config-{Context.Guild.Id}" });

                    await SafeReplyAsync("", embed: $"Usunięto `{Context.Channel.Name}` z listy kanałów bez doświadczenia.".ToEmbedMessage(EMType.Success).Build());
                    return;
                }

                chan = new Database.Models.Configuration.WithoutExpChannel { Channel = Context.Channel.Id };
                config.ChannelsWithoutExp.Add(chan);
                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"config-{Context.Guild.Id}" });
            }

            await SafeReplyAsync("", embed: $"Ustawiono `{Context.Channel.Name}` jako kanał bez doświadczenia.".ToEmbedMessage(EMType.Success).Build());
        }

        [Command("nosupch")]
        [Summary("ustawia kanał bez nadzoru")]
        [Remarks(""), RequireAdminRole]
        public async Task SetNonSupChannelAsync()
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var config = await db.GetGuildConfigOrCreateAsync(Context.Guild.Id);

                var chan = config.ChannelsWithoutSupervision.FirstOrDefault(x => x.Channel == Context.Channel.Id);
                if (chan != null)
                {
                    config.ChannelsWithoutSupervision.Remove(chan);
                    await db.SaveChangesAsync();

                    QueryCacheManager.ExpireTag(new string[] { $"config-{Context.Guild.Id}" });

                    await SafeReplyAsync("", embed: $"Usunięto `{Context.Channel.Name}` z listy kanałów bez nadzoru.".ToEmbedMessage(EMType.Success).Build());
                    return;
                }

                chan = new Database.Models.Configuration.WithoutSupervisionChannel { Channel = Context.Channel.Id };
                config.ChannelsWithoutSupervision.Add(chan);
                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"config-{Context.Guild.Id}" });
            }

            await SafeReplyAsync("", embed: $"Ustawiono `{Context.Channel.Name}` jako kanał bez nadzoru.".ToEmbedMessage(EMType.Success).Build());
        }

        [Command("todo", RunMode = RunMode.Async)]
        [Summary("dodaje wiadomość do todo")]
        [Remarks("2342123444212"), RequireAnyAdminOrModRole]
        public async Task MarkAsTodoAsync([Summary("id wiadomości")] ulong messageId = 0, [Summary("nazwa serwera")] string serverName = null)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var guild = Context.Guild;
                if (messageId == 0 && Context.Message.Reference != null && Context.Message.Reference.MessageId.IsSpecified)
                {
                    messageId = Context.Message.Reference.MessageId.Value;
                }

                if (messageId == 0)
                {
                    await SafeReplyAsync("", embed: "Należy podać id wiadomości.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (serverName != null)
                {
                    var customGuild = Context.Client.Guilds.FirstOrDefault(x => x.Name.Equals(serverName, StringComparison.CurrentCultureIgnoreCase));
                    if (customGuild == null)
                    {
                        await SafeReplyAsync("", embed: "Nie odnaleziono serwera.".ToEmbedMessage(EMType.Bot).Build());
                        return;
                    }

                    var thisUser = customGuild.Users.FirstOrDefault(x => x.Id == Context.User.Id);
                    if (thisUser == null)
                    {
                        await SafeReplyAsync("", embed: "Nie znajdujesz się na docelowym serwerze.".ToEmbedMessage(EMType.Bot).Build());
                        return;
                    }

                    if (!thisUser.GuildPermissions.Administrator)
                    {
                        await SafeReplyAsync("", embed: "Nie posiadasz wystarczających uprawnień na docelowym serwerze.".ToEmbedMessage(EMType.Bot).Build());
                        return;
                    }

                    guild = customGuild;
                }

                var config = await db.GetCachedGuildFullConfigAsync(guild.Id);
                if (config == null)
                {
                    await SafeReplyAsync("", embed: "Serwer nie jest poprawnie skonfigurowany.".ToEmbedMessage(EMType.Bot).Build());
                    return;
                }

                var todoChannel = guild.GetTextChannel(config.ToDoChannel);
                if (todoChannel == null)
                {
                    await SafeReplyAsync("", embed: "Kanał todo nie jest ustawiony.".ToEmbedMessage(EMType.Bot).Build());
                    return;
                }

                var message = await Context.Channel.GetMessageAsync(messageId);
                if (message == null)
                {
                    await SafeReplyAsync("", embed: "Wiadomość nie istnieje!\nPamiętaj, że polecenie musi zostać użyte w tym samym kanale, gdzie znajduje się wiadomość!".ToEmbedMessage(EMType.Bot).Build());
                    return;
                }

                await Context.Message.AddReactionAsync(new Emoji("👌"));
                await todoChannel.SendMessageAsync(message.GetJumpUrl(), embed: _moderation.BuildTodo(message, Context.User as SocketGuildUser));
            }
        }

        [Command("quote", RunMode = RunMode.Async)]
        [Summary("cytuje wiadomość i wysyła na podany kanał")]
        [Remarks("2342123444212 2342123444212"), RequireAnyAdminOrModRole]
        public async Task QuoteAndSendAsync([Summary("id wiadomości")] ulong messageId, [Summary("id kanału na serwerze")] ulong channelId)
        {
            var channel2Send = Context.Guild.GetTextChannel(channelId);
            if (channel2Send == null)
            {
                await SafeReplyAsync("", embed: "Nie odnaleziono kanału.\nPamiętaj, że kanał musi znajdować się na tym samym serwerze.".ToEmbedMessage(EMType.Bot).Build());
                return;
            }

            var message = await Context.Channel.GetMessageAsync(messageId);
            if (message == null)
            {
                await SafeReplyAsync("", embed: "Wiadomość nie istnieje!\nPamiętaj, że polecenie musi zostać użyte w tym samym kanale, gdzie znajduje się wiadomość!".ToEmbedMessage(EMType.Bot).Build());
                return;
            }

            await Context.Message.AddReactionAsync(new Emoji("👌"));
            await channel2Send.SendMessageAsync(message.GetJumpUrl(), embed: _moderation.BuildTodo(message, Context.User as SocketGuildUser));
        }

        [Command("tchaos")]
        [Summary("włącz/wyłącz tryb siania chaosu")]
        [Remarks(""), RequireAdminRole]
        public async Task SetToggleChaosModeAsync()
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var config = await db.GetGuildConfigOrCreateAsync(Context.Guild.Id);

                config.ChaosMode = !config.ChaosMode;
                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"config-{Context.Guild.Id}" });

                await SafeReplyAsync("", embed: $"Tryb siania chaosu - włączony? `{config.ChaosMode.GetYesNo()}`.".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("tsup")]
        [Summary("włącza lub wyłącza tryb nadzoru")]
        [Remarks(""), RequireAdminRole]
        public async Task SetToggleSupervisionModeAsync()
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var config = await db.GetGuildConfigOrCreateAsync(Context.Guild.Id);

                config.Supervision = !config.Supervision;
                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"config-{Context.Guild.Id}" });

                await SafeReplyAsync("", embed: $"Tryb nadzoru - włączony?`{config.ChaosMode.GetYesNo()}`.".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("check")]
        [Summary("sprawdza użytkownika")]
        [Remarks("Karna"), RequireAnyAdminOrModRole]
        public async Task CheckUserAsync([Summary("użytkownik")] SocketGuildUser user)
        {
            string report = "**Globalki:** ✅\n\n";
            using (var db = new Database.DatabaseContext(Config))
            {
                var guildConfig = await db.GetCachedGuildFullConfigAsync(user.Guild.Id);

                var duser = await db.GetUserOrCreateSimpleAsync(user.Id);
                var globalRole = user.Guild.GetRole(guildConfig.GlobalEmotesRole);
                if (globalRole != null)
                {
                    if (user.Roles.Contains(globalRole))
                    {
                        var sub = duser.TimeStatuses.FirstOrDefault(x => x.Type == StatusType.Globals && x.Guild == user.Guild.Id);
                        if (sub == null)
                        {
                            report = $"**Globalki:** ❗\n\n";
                            await user.RemoveRoleAsync(globalRole);
                        }
                        else if (!sub.IsActive(_time.Now()))
                        {
                            report = $"**Globalki:** ⚠\n\n";
                            await user.RemoveRoleAsync(globalRole);
                        }
                    }
                }

                string kolorRep = $"**Kolor:** ✅\n\n";
                var colorRoles = (IEnumerable<uint>)Enum.GetValues(typeof(FColor));
                if (user.Roles.Any(x => colorRoles.Any(c => c.ToString() == x.Name)))
                {
                    var sub = duser.TimeStatuses.FirstOrDefault(x => x.Type == StatusType.Color && x.Guild == user.Guild.Id);
                    if (sub == null)
                    {
                        kolorRep = $"**Kolor:** ❗\n\n";
                        await _profile.RomoveUserColorAsync(user);
                    }
                    else if (!sub.IsActive(_time.Now()))
                    {
                        kolorRep = $"**Kolor:** ⚠\n\n";
                        await _profile.RomoveUserColorAsync(user);
                    }
                }
                report += kolorRep;

                string nickRep = $"**Nick:** ✅";
                if (guildConfig.UserRole != 0)
                {
                    var userRole = user.Guild.GetRole(guildConfig.UserRole);
                    if (userRole != null)
                    {
                        if (user.Roles.Contains(userRole))
                        {
                            var realNick = (user.Nickname ?? user.GlobalName) ?? user.Username;
                            if (duser.Shinden != 0)
                            {
                                var res = await _shClient.User.GetAsync(duser.Shinden);
                                if (res.IsSuccessStatusCode())
                                {
                                    if (res.Body.Name != realNick)
                                        nickRep = $"**Nick:** ❗ {res.Body.Name}";
                                }
                                else nickRep = $"**Nick:** ❗ D: {duser.Shinden}";
                            }
                            else
                            {
                                var res = await _shClient.Search.UserAsync(realNick);
                                if (res.IsSuccessStatusCode())
                                {
                                    if (!res.Body.Any(x => x.Name.Equals(realNick, StringComparison.Ordinal)))
                                        nickRep = $"**Nick:** ⚠";
                                }
                                else nickRep = $"**Nick:** ⚠";
                            }
                        }
                    }
                }
                report += nickRep;
            }

            await SafeReplyAsync("", embed: report.ToEmbedMessage(EMType.Bot).WithAuthor(new EmbedAuthorBuilder().WithUser(user)).Build());
        }

        [Command("loteria", RunMode = RunMode.Async)]
        [Summary("bot losuje osobę spośród tych, co dodali reakcję")]
        [Remarks("5"), RequireAnyAdminOrModRole]
        public async Task GetRandomUserAsync([Summary("długość w minutach")] uint duration)
        {
            var emote = new Emoji("🎰");
            var time = _time.Now().AddMinutes(duration);
            var msg = await SafeReplyAsync("", embed: $"Loteria! zareaguj {emote}, aby wziąć udział.\n\n Koniec `{time.ToShortTimeString()}:{time.Second:00}`".ToEmbedMessage(EMType.Bot).Build());

            await msg.AddReactionAsync(emote);
            await Task.Delay(TimeSpan.FromMinutes(duration));
            await msg.RemoveReactionAsync(emote, Context.Client.CurrentUser);

            var reactions = await msg.GetReactionUsersAsync(emote, 300).FlattenAsync();
            var winner = Services.Fun.GetOneRandomFrom(reactions);
            await msg.DeleteAsync();

            await SafeReplyAsync("", embed: $"Zwycięzca loterii: {winner.Mention}".ToEmbedMessage(EMType.Success).Build());
        }

        [Command("pary", RunMode = RunMode.Async)]
        [Summary("bot losuje pary liczb")]
        [Remarks("5"), RequireAnyAdminOrModRole]
        public async Task GetRandomPairsAsync([Summary("liczba par")] uint count)
        {
            var pairs = new List<Tuple<int, int>>();
            var total = Enumerable.Range(1, (int)count * 2).ToList();

            while (total.Count > 0)
            {
                var first = Services.Fun.GetOneRandomFrom(total);
                total.Remove(first);

                var second = Services.Fun.GetOneRandomFrom(total);
                total.Remove(second);

                pairs.Add(new Tuple<int, int>(first, second));
            }

            await SafeReplyAsync("", embed: $"**Pary**:\n\n{string.Join("\n", pairs.Select(x => $"{x.Item1} - {x.Item2}"))}".TrimToLength().ToEmbedMessage(EMType.Success).Build());
        }

        [Command("pozycja gracza", RunMode = RunMode.Async)]
        [Summary("bot losuje liczbę dla gracza")]
        [Remarks("kokosek dzida"), RequireAnyAdminOrModRole]
        public async Task AssingNumberToUsersAsync([Summary("nazwy graczy")] params string[] players)
        {
            var numbers = Enumerable.Range(1, players.Count()).ToList();
            var pairs = new List<Tuple<string, int>>();
            var playerList = players.ToList();

            while (playerList.Count > 0)
            {
                var player = Services.Fun.GetOneRandomFrom(playerList);
                playerList.Remove(player);

                var number = Services.Fun.GetOneRandomFrom(numbers);
                numbers.Remove(number);

                pairs.Add(new Tuple<string, int>(player, number));
            }

            await SafeReplyAsync("", embed: $"**Numerki**:\n\n{string.Join("\n", pairs.Select(x => $"{x.Item1} - {x.Item2}"))}".TrimToLength().ToEmbedMessage(EMType.Success).Build());
        }

        [Command("raport")]
        [Alias("report")]
        [Summary("rozwiązuje raport, nie podanie czasu odrzuca go, podanie czasu 0 ostrzega użytkownika")]
        [Remarks("2342123444212 4 kara dla Ciebie"), RequireAnyAdminRole, Priority(1)]
        public async Task ResolveReportAsync([Summary("id raportu")] ulong rId, [Summary("długość wyciszenia w h")] long duration = -1, [Summary("powód")][Remainder] string reason = "z raportu")
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var config = await db.GetGuildConfigOrCreateAsync(Context.Guild.Id);

                var raport = config.Raports.FirstOrDefault(x => x.Message == rId);
                if (raport == null)
                {
                    await SafeReplyAsync("", embed: $"Taki raport nie istnieje.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var notifChannel = Context.Guild.GetTextChannel(config.NotificationChannel);
                var reportChannel = Context.Guild.GetTextChannel(config.RaportChannel);
                var userRole = Context.Guild.GetRole(config.UserRole);
                var muteRole = Context.Guild.GetRole(config.MuteRole);

                if (muteRole == null)
                {
                    await SafeReplyAsync("", embed: "Rola wyciszająca nie jest ustawiona.".ToEmbedMessage(EMType.Bot).Build());
                    return;
                }

                if (reportChannel == null)
                {
                    await SafeReplyAsync("", embed: "Kanał raportów nie jest ustawiony.".ToEmbedMessage(EMType.Bot).Build());
                    return;
                }

                var reportMsg = await reportChannel.GetMessageAsync(raport.Message);
                if (duration == -1)
                {
                    if (reportMsg != null)
                    {
                        try
                        {
                            var rEmbedBuilder = reportMsg?.Embeds?.FirstOrDefault().ToEmbedBuilder();

                            rEmbedBuilder.Color = EMType.Info.Color();
                            rEmbedBuilder.Fields.FirstOrDefault(x => x.Name == "Id zgloszenia:").Value = "Odrzucone!";
                            await SafeReplyAsync("", embed: rEmbedBuilder.Build());
                        }
                        catch (Exception) { }
                        await reportMsg.DeleteAsync();
                    }

                    config.Raports.Remove(raport);
                    await db.SaveChangesAsync();
                    return;
                }
                if (duration < 0) return;

                bool warning = duration == 0;
                if (reportMsg != null)
                {
                    try
                    {
                        var rEmbedBuilder = reportMsg?.Embeds?.FirstOrDefault().ToEmbedBuilder();
                        if (reason == "z raportu")
                            reason = rEmbedBuilder?.Fields.FirstOrDefault(x => x.Name == "Powód:").Value.ToString() ?? reason;

                        rEmbedBuilder.Color = warning ? EMType.Success.Color() : EMType.Bot.Color();
                        rEmbedBuilder.Fields.FirstOrDefault(x => x.Name == "Id zgloszenia:").Value = "Rozpatrzone!";
                        await SafeReplyAsync("", embed: rEmbedBuilder.Build());
                    }
                    catch (Exception) { }

                    await reportMsg.DeleteAsync();
                }

                config.Raports.Remove(raport);
                await db.SaveChangesAsync();

                var user = Context.Guild.GetUser(raport.User);
                if (user == null)
                {
                    await SafeReplyAsync("", embed: $"Użytkownika nie ma serwerze.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (user.Roles.Contains(muteRole) && !warning)
                {
                    await SafeReplyAsync("", embed: $"{user.Mention} już jest wyciszony.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var usr = Context.User as SocketGuildUser;
                string byWho = usr.GetUserNickInGuild();

                if (warning)
                {
                    var dbUser = await db.GetUserOrCreateSimpleAsync(user.Id);

                    ++dbUser.Warnings;

                    if (dbUser.Warnings < 3)
                    {
                        await db.SaveChangesAsync();
                        await _moderation.NotifyUserAsync(user, reason);
                        return;
                    }

                    var multiplier = 1;
                    if (dbUser.Warnings > 15)
                    {
                        multiplier = 30;
                    }
                    else if (dbUser.Warnings > 10)
                    {
                        multiplier = 10;
                    }
                    else if (dbUser.Warnings > 5)
                    {
                        multiplier = 2;
                    }

                    byWho = "automat";
                    duration = 24 * multiplier;
                    reason = $"przekroczono maksymalną liczbę ostrzeżeń ({dbUser.Warnings})";
                }

                var info = await _moderation.MuteUserAsync(user, muteRole, null, userRole, db, duration, reason);
                await _moderation.NotifyAboutPenaltyAsync(user, notifChannel, info, byWho);

                await SafeReplyAsync("", embed: $"{user.Mention} został wyciszony.".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("pomoc", RunMode = RunMode.Async)]
        [Alias("help", "h")]
        [Summary("wypisuje polecenia")]
        [Remarks("kasuj"), RequireAnyAdminOrModRole]
        public async Task SendHelpAsync([Summary("nazwa polecenia")][Remainder] string command = null)
        {
            if (command != null)
            {
                try
                {
                    string prefix = _config.Get().Prefix;
                    if (Context.Guild != null)
                    {
                        using (var db = new Database.DatabaseContext(_config))
                        {
                            var gConfig = await db.GetCachedGuildFullConfigAsync(Context.Guild.Id);
                            if (gConfig?.Prefix != null) prefix = gConfig.Prefix;
                        }
                    }

                    await SafeReplyAsync(_helper.GiveHelpAboutPrivateCmd("Moderacja", command, prefix));
                }
                catch (Exception ex)
                {
                    await SafeReplyAsync("", embed: ex.Message.ToEmbedMessage(EMType.Error).Build());
                }

                return;
            }

            await SafeReplyAsync(_helper.GivePrivateHelp("Moderacja"));
        }
    }
}
