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
using Sanakan.Database.Models.Management;
using Sanakan.Extensions;
using Sanakan.Services.Executor;
using Sanakan.Services.Time;

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
                    _logger.LogError($"Greeting: fetching ban reason for u{user.Id}: {ex}");
                }

                if (Moderator.IsAutomaticReason(banReason))
                {
                    var embed = new EmbedBuilder
                    {
                        Title = PenaltyFormat.Title(PenaltyType.Ban, true),
                        Color = PenaltyFormat.GetColor(PenaltyType.Ban),
                        Author = new EmbedAuthorBuilder().WithUser(user),
                        Description = $"**Powód:** {PenaltyFormat.Reason(banReason)}".TrimToLength(2500),
                        Footer = new EmbedFooterBuilder().WithText(PenaltyFormat.Footer(Moderator.AutoAuthor, true)),
                        Timestamp = new DateTimeOffset(_time.Now()),
                    }
                    .AddField("Użytkownik", PenaltyFormat.User(user), true)
                    .AddField("Czas", "na stałe", true);

                    await notifCh.SendMessageAsync("", embed: embed.Build());
                    return;
                }

                // the ban command has already announced this ban
                var since = _time.Now().AddMinutes(-5);
                if (await db.Penalties.AsQueryable().AnyAsync(x => x.User == user.Id && x.Guild == guild.Id
                    && x.Type == PenaltyType.Ban && x.StartDate >= since))
                    return;

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
                _logger.LogError($"Greeting: user join u{user.Id}: {ex}");
            }

            if (_config.Get().BlacklistedGuilds.Any(x => x == user.Guild.Id))
                return;

            using (var db = new Database.DatabaseContext(_config))
            {
                var config = await db.GetCachedGuildFullConfigAsync(user.Guild.Id);
                if (config == null) return;

                if (config.WelcomeMessage != null && config.WelcomeMessage != "off")
                    await SendMessageAsync(ReplaceTags(user, config.WelcomeMessage), user.Guild.GetTextChannel(config.GreetingChannel));

                if (config.WelcomeMessagePW == null) return;
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
            if (user.IsBot || user.IsWebhook) return;

            if (!_config.Get().BlacklistedGuilds.Any(x => x == guild.Id))
            {
                using (var db = new Database.DatabaseContext(_config))
                {
                    var config = await db.GetCachedGuildFullConfigAsync(guild.Id);

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
                _logger.LogError($"Greeting: user leave u{user.Id}: {ex}");
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
                            _logger.LogError($"Greeting: left user removal u{userId}: {ex}");
                        }
                    });

                    await _executor.TryAdd(new Executable("delete user", removeTask, userId, Priority.High), TimeSpan.FromSeconds(1));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Greeting: left users cleanup: {ex}");
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

        private Task<bool> IsStillOnAnyGuildAsync(ulong leftGuildId, ulong userId)
            => IsOnAnyGuildAsync(_client, _logger, userId, leftGuildId);

        // asks Discord only when the guild's member list is incomplete, an error counts as present so nobody is removed by mistake
        public static async Task<bool> IsOnAnyGuildAsync(DiscordSocketClient client, ILogger logger, ulong userId, ulong skipGuildId = 0)
        {
            foreach (var guild in client.Guilds.Where(x => x.Id != skipGuildId))
            {
                if (guild.GetUser(userId) != null)
                    return true;

                if (guild.HasAllMembers)
                    continue;

                try
                {
                    if (await client.Rest.GetGuildUserAsync(guild.Id, userId) != null)
                        return true;
                }
                catch (Exception ex)
                {
                    logger?.LogError($"Greeting: user leave check g{guild.Id} u{userId}: {ex}");
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