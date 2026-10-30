#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Sanakan.Config;
using Sanakan.Extensions;
using Sanakan.Services.Executor;
using Sanakan.Services.Time;
using Shinden.Logger;
using Z.EntityFramework.Plus;

namespace Sanakan.Services
{
    public class Greeting
    {
        private DiscordSocketClient _client { get; set; }
        private IExecutor _executor { get; set; }
        private ISystemTime _time { get; set; }
        private ILogger _logger { get; set; }
        private IConfig _config { get; set; }
        private Timer _cleanupTimer;

        public static readonly TimeSpan LeftUserGracePeriod = TimeSpan.FromDays(30);
        private static readonly TimeSpan CleanupAt = TimeSpan.FromHours(5);

        public Greeting(DiscordSocketClient client, ILogger logger, IConfig config, IExecutor exe, ISystemTime time)
        {
            _client = client;
            _logger = logger;
            _config = config;
            _executor = exe;
            _time = time;

#if !DEBUG
            _client.LeftGuild += BotLeftGuildAsync;
            _client.UserJoined += UserJoinedAsync;
            _client.UserBanned += UserBannedAsync;
            _client.UserLeft += UserLeftAsync;
            _cleanupTimer = new Timer(_ => _ = RemoveLeftUsersAsync(), null, UntilNextCleanup(time.Now()), Timeout.InfiniteTimeSpan);
#endif
        }

        private async Task UserBannedAsync(SocketUser user, SocketGuild guild)
        {
            using (var db = new Database.DatabaseContext(_config))
            {
                var botUser = await db.GetCachedNoGameDeckUserAsync(user.Id);
                await db.UserActivities.AddAsync(new UserActivityBuilder(_time)
                    .WithUser(botUser, user).WithType(Database.Models.ActivityType.Banned).Build());
                await db.SaveChangesAsync();

                var config = await db.GetCachedGuildFullConfigAsync(guild.Id);
                if (config == null) return;

                var notifCh = guild.GetTextChannel(config.NotificationChannel);
                if (notifCh == null) return;

                var banReason = string.Empty;
                try
                {
                    var ban = await guild.GetBanAsync(user.Id);
                    if (!string.IsNullOrWhiteSpace(ban?.Reason))
                        banReason = ban.Reason;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Greeting: nie udało się pobrać powodu bana użytkownika {user.Id}: {ex}");
                }

                var banMessage = string.IsNullOrWhiteSpace(banReason)
                    ? "Poleciał, szczegółów raczej brak."
                    : $"Poleciał. Powód: {banReason}";

                await notifCh.SendMessageAsync("", embed: banMessage.ToEmbedMessage(EMType.Error).WithUser(user, true).Build());
            }
        }

        private async Task BotLeftGuildAsync(SocketGuild guild)
        {
            using (var db = new Database.DatabaseContext(_config))
            {
                var gConfig = await db.GetGuildConfigOrCreateAsync(guild.Id);
                db.Guilds.Remove(gConfig);

                var stats = db.TimeStatuses.AsQueryable().AsSplitQuery().Where(x => x.Guild == guild.Id).ToList();
                db.TimeStatuses.RemoveRange(stats);

                await db.SaveChangesAsync();
            }

            using (var db = new Database.DatabaseContext(_config))
            {
                var mute = db.Penalties.AsQueryable().AsSplitQuery().Where(x => x.Guild == guild.Id).ToList();
                db.Penalties.RemoveRange(mute);

                await db.SaveChangesAsync();
            }
        }

        private async Task UserJoinedAsync(SocketGuildUser user)
        {
            if (user.IsBot || user.IsWebhook) return;

            try
            {
                using (var db = new Database.DatabaseContext(_config))
                {
                    await db.ClearUserLeftAsync(user.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"In user join u{user.Id}: {ex}");
            }

            if (_config.Get().BlacklistedGuilds.Any(x => x == user.Guild.Id))
                return;

            using (var db = new Database.DatabaseContext(_config))
            {
                var config = await db.GetCachedGuildFullConfigAsync(user.Guild.Id);
                if (config?.WelcomeMessage == null) return;
                if (config.WelcomeMessage == "off") return;

                await SendMessageAsync(ReplaceTags(user, config.WelcomeMessage), user.Guild.GetTextChannel(config.GreetingChannel));

                if (config?.WelcomeMessagePW == null) return;
                if (config.WelcomeMessagePW == "off") return;

                try
                {
                    var pw = await user.CreateDMChannelAsync();
                    await pw.SendMessageAsync(ReplaceTags(user, config.WelcomeMessagePW));
                    await pw.CloseAsync();
                }
                catch (Discord.Net.HttpException ex) when ((int?)ex.DiscordCode is 50007 or 50278)
                {
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Greeting: {ex}");
                }
            }
        }

        private async Task UserLeftAsync(SocketGuild guild, SocketUser user)
        {
            ulong channelId = 0;
            ulong adminRoleId = 0;

            if (user.IsBot || user.IsWebhook) return;

            if (!_config.Get().BlacklistedGuilds.Any(x => x == guild.Id))
            {
                using (var db = new Database.DatabaseContext(_config))
                {
                    var config = await db.GetCachedGuildFullConfigAsync(guild.Id);
                    channelId = config?.GreetingChannel ?? 0;
                    adminRoleId = config?.AdminRole ?? 0;

                    if (config?.GoodbyeMessage != null && config.GoodbyeMessage != "off")
                        await SendMessageAsync(ReplaceTags(user, config.GoodbyeMessage), guild.GetTextChannel(config.GreetingChannel));
                }
            }

            if (await IsStillOnAnyGuildAsync(guild.Id, user.Id))
                return;

            try
            {
                using (var db = new Database.DatabaseContext(_config))
                {
                    await db.MarkUserLeftAsync(user.Id, _time.Now());
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("In user leave:" + ex.ToString());
                await SendMessageAsync($"**ERR:** `{user.Id}` {guild.GetRole(adminRoleId)?.Mention}", guild.GetTextChannel(channelId));
            }
        }

        private async Task RemoveLeftUsersAsync()
        {
            try
            {
                // without a full guild list everyone would look absent
                if (_client.ConnectionState != ConnectionState.Connected || _client.Guilds.Count == 0)
                    return;

                var cutoff = _time.Now() - LeftUserGracePeriod;
                List<ulong> users;
                using (var db = new Database.DatabaseContext(_config))
                {
                    users = await db.GetUsersLeftBeforeAsync(cutoff);
                }

                foreach (var userId in users)
                {
                    if (await IsStillOnAnyGuildAsync(0, userId))
                    {
                        using (var db = new Database.DatabaseContext(_config))
                        {
                            await db.ClearUserLeftAsync(userId);
                        }
                        continue;
                    }

                    var removeTask = new Func<Task>(async () =>
                    {
                        try
                        {
                            using (var db = new Database.DatabaseContext(_config))
                            {
                                await db.RemoveUserLeftBeforeAsync(userId, cutoff);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"In left user removal u{userId}: {ex}");
                        }
                    });

                    await _executor.TryAdd(new Executable("delete user", removeTask, userId, Priority.High), TimeSpan.FromSeconds(1));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"In left users cleanup: {ex}");
            }
            finally
            {
                _cleanupTimer.Change(UntilNextCleanup(_time.Now()), Timeout.InfiniteTimeSpan);
            }
        }

        public static TimeSpan UntilNextCleanup(DateTime now)
        {
            var next = now.Date + CleanupAt;
            if (next <= now) next = next.AddDays(1);
            return next - now;
        }

        private async Task<bool> IsStillOnAnyGuildAsync(ulong leftGuildId, ulong userId)
        {
            foreach (var guild in _client.Guilds.Where(x => x.Id != leftGuildId))
            {
                if (guild.GetUser(userId) != null)
                    return true;

                try
                {
                    if (await _client.Rest.GetGuildUserAsync(guild.Id, userId) != null)
                        return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"In user leave check g{guild.Id} u{userId}: {ex}");
                    return true;
                }
            }
            return false;
        }

        private async Task SendMessageAsync(string message, ITextChannel channel)
        {
            if (channel != null) await channel.SendMessageAsync(message);
        }

        private string ReplaceTags(SocketUser user, string message)
            => message.Replace("^nick", user.Username).Replace("^mention", user.Mention);
    }
}