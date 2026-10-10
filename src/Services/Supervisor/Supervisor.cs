#pragma warning disable 1591

using System;
using System.Globalization;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using AsyncKeyedLock;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Sanakan.Config;
using Sanakan.Extensions;
using Sanakan.Services.ScamImages;
using Sanakan.Services.Time;
using Shinden.Logger;

namespace Sanakan.Services.Supervisor
{
    public class Supervisor
    {
        private enum Action { None, Ban, Mute, Warn }

        private const int MAX_TOTAL = 12;
        private const int MAX_SPECIFIED = 6;

        private const int COMMAND_MOD = 2;
        private const int UNCONNECTED_MOD = -2;
        private const string IMAGE_SPAM_EXEMPTION = "Szanowny sanakanie, pozwól mi wysłać więcej niż trzy obrazki, błagam!";

    #if DEBUG
        private const bool isDebug = true;
    #else
        private const bool isDebug = false;
    #endif

        private static readonly string[] _imageExtensions =
        {
            ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp"
        };

        private AsyncNonKeyedLocker _semaphore = new AsyncNonKeyedLocker(1);
        private AsyncNonKeyedLocker _semaphoreJoin = new AsyncNonKeyedLocker(1);
        private AsyncKeyedLocker<ulong> _semaphoreUser = new AsyncKeyedLocker<ulong>(x =>
        {
            x.PoolSize = 200;
            x.PoolInitialFill = 10;
            x.MaxCount = 1;
        });
        private Dictionary<ulong, Dictionary<ulong, SupervisorEntity>> _guilds;
        private Dictionary<ulong, Dictionary<string, SupervisorJoinEntity>> _guildsJoin;
        private Dictionary<ulong, HashSet<ulong>> _temporarySupervisionChannels;

        private DiscordSocketClient _client;
        private Moderator _moderator;
        private ScamImageScanner _scamImages;
        private ISystemTime _time;
        private ILogger _logger;
        private IConfig _config;
        private Timer _timer;

        public Supervisor(DiscordSocketClient client, IConfig config, ILogger logger, Moderator moderator, ISystemTime time,
            ScamImageScanner scamImages)
        {
            _moderator = moderator;
            _client = client;
            _config = config;
            _logger = logger;
            _time = time;
            _scamImages = scamImages;

            _guilds = new Dictionary<ulong, Dictionary<ulong, SupervisorEntity>>();
            _guildsJoin = new Dictionary<ulong, Dictionary<string, SupervisorJoinEntity>>();
            _temporarySupervisionChannels = new Dictionary<ulong, HashSet<ulong>>();

            _timer = new Timer(async _ =>
            {
                try
                {
                    using (await _semaphore.LockAsync().ConfigureAwait(false))
                    using (await _semaphoreJoin.LockAsync().ConfigureAwait(false))
                    {
                        AutoValidate();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Supervisor: autovalidate: {ex}");
                }
            },
            null,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(5));
            _client.MessageReceived += HandleMessageAsync;
            _client.UserJoined += UserJoinedAsync;
        }

        private async Task HandleMessageAsync(SocketMessage message)
        {
            var msg = message as SocketUserMessage;
            if (msg == null) return;

            if (msg.Author.IsBot || msg.Author.IsWebhook) return;

            var user = msg.Author as SocketGuildUser;
            if (user == null) return;

            if (await HandleSupervisionCommandAsync(user, msg))
                return;

            if (!_config.Get().Supervision) return;

            if (_config.Get().BlacklistedGuilds.Any(x => x == user.Guild.Id))
                return;

            _ = Task.Run(async () =>
            {
                try
                {
                    using (await _semaphoreUser.LockAsync(user.Id).ConfigureAwait(false))
                    {
                        await Analize(user, msg);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Supervisor: analize: {ex}");
                }
            });

            await Task.CompletedTask;
        }

        public enum SupervisionCommand { None, Status, Activate }

        public static SupervisionCommand ParseSupervisionCommand(string content, bool debug)
        {
            var command = content?.Trim();
            if (string.Equals(command, "isSuper", StringComparison.OrdinalIgnoreCase))
                return SupervisionCommand.Status;

            if (debug && string.Equals(command, "activesuper", StringComparison.OrdinalIgnoreCase))
                return SupervisionCommand.Activate;

            return SupervisionCommand.None;
        }

        private async Task<bool> HandleSupervisionCommandAsync(SocketGuildUser user, SocketUserMessage message)
        {
            var command = ParseSupervisionCommand(message.Content, isDebug);
            if (command == SupervisionCommand.None)
                return false;

            var isActivateCommand = command == SupervisionCommand.Activate;
            using (var db = new Database.DatabaseContext(_config))
            {
                var gConfig = await db.GetCachedGuildFullConfigAsync(user.Guild.Id);
                if (gConfig == null)
                    return false;

                var isAdmin = gConfig.AdminRole != 0 && user.Roles.Any(x => x.Id == gConfig.AdminRole);
                if (!isAdmin)
                    return false;

                var isBlacklisted = _config.Get().BlacklistedGuilds.Any(x => x == user.Guild.Id);
                if (isActivateCommand && !isBlacklisted)
                {
                    using (await _semaphore.LockAsync().ConfigureAwait(false))
                    {
                        if (!_temporarySupervisionChannels.TryGetValue(user.Guild.Id, out var channels))
                        {
                            channels = new HashSet<ulong>();
                            _temporarySupervisionChannels.Add(user.Guild.Id, channels);
                        }

                        channels.Add(message.Channel.Id);
                    }

                    await message.Channel.SendMessageAsync("Nadzór został tymczasowo aktywowany na tym kanale (do restartu bota).");
                    return true;
                }

                var isTemporary = await IsTemporarySupervisionActiveAsync(user.Guild.Id, message.Channel.Id);
                var isActive = !isBlacklisted && (isTemporary ||
                    (gConfig.Supervision && !gConfig.ChannelsWithoutSupervision.Any(x => x.Channel == message.Channel.Id)));
                var status = isActive ? "aktywny" : "nieaktywny";

                await message.Channel.SendMessageAsync($"Nadzór na tym kanale jest {status}.");
                return true;
            }
        }

        private async Task<bool> IsTemporarySupervisionActiveAsync(ulong guildId, ulong channelId)
        {
            using (await _semaphore.LockAsync().ConfigureAwait(false))
            {
                return _temporarySupervisionChannels.TryGetValue(guildId, out var channels) &&
                    channels.Contains(channelId);
            }
        }

        private async Task Analize(SocketGuildUser user, SocketUserMessage message)
        {
            Action action;
            SocketRole muteRole;
            SocketRole userRole;
            ITextChannel notifChannel;
            bool deleteMessage = false;
            bool sendImageScamWarning = false;
            string penaltyReason = null;
            var hasTooManyImages = HasMoreThanThreeImages(message) && !IsImageSpamExempt(message);

            using (var db = new Database.DatabaseContext(_config))
            {
                var gConfig = await db.GetCachedGuildFullConfigAsync(user.Guild.Id);
                if (gConfig == null) return;

                if (gConfig.AlwaysBanChannel != 0 && message.Channel.Id == gConfig.AlwaysBanChannel)
                {
                    var isStaff = IsAlwaysBanProtected(user.Id == user.Guild.OwnerId, user.GuildPermissions.Administrator,
                        gConfig.AdminRole, gConfig.SemiAdminRole,
                        user.Roles.Select(x => x.Id),
                        gConfig.ModeratorRoles?.Select(x => x.Role) ?? Enumerable.Empty<ulong>());

                    muteRole = user.Guild.GetRole(gConfig.MuteRole);
                    userRole = user.Guild.GetRole(gConfig.UserRole);
                    notifChannel = user.Guild.GetTextChannel(gConfig.NotificationChannel);

                    try
                    {
                        await message.DeleteAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Supervisor: unable to delete always-ban message {message.Id}: {ex}");
                    }

                    var alwaysBanReason = isStaff
                        ? "Automatyczny mute: wiadomość na kanale objętym always-ban (rola uprzywilejowana)."
                        : "Automatyczny ban: wiadomość na kanale objętym always-ban.";

                    try
                    {
                        await MakeActionAsync(isStaff ? Action.Mute : Action.Ban, user, message, userRole, muteRole, notifChannel, false, alwaysBanReason);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Supervisor: always-ban action failed for {user.Id} on channel {message.Channel.Id}: {ex}");
                    }

                    try
                    {
                        var farewell = isStaff
                            ? $"{user.Mention} opuszcza nas na chwilę."
                            : $"{user.Mention} opuszcza nas na zawsze.";

                        await message.Channel.SendMessageAsync("", embed: farewell.ToEmbedMessage(EMType.Bot)
                            .WithImageUrl(Fun.GetRandomMuteReactionGif()).Build());
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Supervisor: unable to send always-ban farewell on {message.Channel.Id}: {ex}");
                    }

                    return;
                }

                var isTemporary = await IsTemporarySupervisionActiveAsync(user.Guild.Id, message.Channel.Id);
                if (!gConfig.Supervision && !isTemporary) return;

                if (gConfig.AdminRole != 0 && user.Roles.Any(x => x.Id == gConfig.AdminRole))
                    return;

                if (!isTemporary && gConfig.ChannelsWithoutSupervision.Any(x => x.Channel == message.Channel.Id))
                    return;

                var scan = hasTooManyImages ? new ScamImageScanResult() : await ScanImagesAsync(message);
                var hasScamImage = scan.Matches.Count > 0;
                foreach (var scamMatch in scan.Matches)
                {
                    _logger.Log($"ScamImage: hit msg={message.GetJumpUrl()} author={user.Id} url={scamMatch.Url} " +
                        $"hash={ScamImageStore.FormatHash(scamMatch.Hash)} known={ScamImageStore.FormatHash(scamMatch.KnownHash)} distance={scamMatch.Distance}");
                }

                var messageContent = GetMessageContent(message);
                muteRole = user.Guild.GetRole(gConfig.MuteRole);
                userRole = user.Guild.GetRole(gConfig.UserRole);
                notifChannel = user.Guild.GetTextChannel(gConfig.NotificationChannel);

                using (await _semaphore.LockAsync().ConfigureAwait(false))
                {
                    if (!_guilds.Any(x => x.Key == user.Guild.Id))
                        _guilds.Add(user.Guild.Id, new Dictionary<ulong, SupervisorEntity>());

                    var guild = _guilds[user.Guild.Id];
                    if (!guild.Any(x => x.Key == user.Id))
                        guild.Add(user.Id, new SupervisorEntity(_time));

                    var susspect = guild[user.Id];
                    if (!susspect.IsValid())
                    {
                        susspect = new SupervisorEntity(_time);
                        guild[user.Id] = susspect;
                    }

                    var trackedContent = string.IsNullOrEmpty(message.Content)
                        ? $"attachment:{message.Id}"
                        : messageContent;
                    var thisMessage = susspect.Get(trackedContent);

                    bool hasRole = user.Roles.Any(x => x.Id == gConfig.UserRole || x.Id == gConfig.MuteRole) || gConfig.UserRole == 0;
                    bool hasSuspiciousUrl = thisMessage.IsBannable();
                    bool hasNonWhitelistedUrl = false;
                    if (_config.Get().GiveBanForUrlSpam)
                    {
                        hasNonWhitelistedUrl = thisMessage.AnyUrl();
                    }

                    bool isBannable = hasSuspiciousUrl || hasNonWhitelistedUrl;
                    action = MakeDecision(messageContent, susspect.Inc(), thisMessage.Inc(), hasRole && !isBannable);
                    var imageSpamCount = 0;
                    if (hasTooManyImages || hasScamImage)
                    {
                        deleteMessage = true;
                        var hits = hasScamImage ? scan.Matches.Count : 1;
                        imageSpamCount = susspect.IncImageSpam(hits);
                        if (ShouldPunishImageSpam(imageSpamCount, hasScamImage))
                            action = hasRole ? Action.Mute : Action.Ban;
                        else
                            sendImageScamWarning = imageSpamCount == hits;
                    }

                    if (action == Action.Mute || action == Action.Ban)
                    {
                        penaltyReason = GetPenaltyReason(action, messageContent, hasSuspiciousUrl,
                            hasNonWhitelistedUrl, imageSpamCount, susspect.TotalMessages, thisMessage.Count,
                            hasScamImage && !hasTooManyImages);
                    }
                }

                if (action == Action.Mute || action == Action.Ban)
                {
                    foreach (var image in scan.Unmatched)
                        _logger.Log($"ScamImage: unmatched image in penalized message msg={message.GetJumpUrl()} author={user.Id} " +
                            $"action={action} url={image.Url} hash={ScamImageStore.FormatHash(image.Hash)}");
                }
            }

            if (deleteMessage)
            {
                try
                {
                    await message.DeleteAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Supervisor: unable to delete image spam message {message.Id}: {ex}");
                }
            }

            await MakeActionAsync(action, user, message, userRole, muteRole, notifChannel, sendImageScamWarning, penaltyReason);
        }

        private async Task MakeActionAsync(Action action, SocketGuildUser user, SocketUserMessage message, SocketRole userRole, SocketRole muteRole, ITextChannel notifChannel, bool sendImageScamWarning, string penaltyReason)
        {
            if (sendImageScamWarning)
                await message.Channel.SendMessageAsync("",
                    embed: $"{user.Mention} wysyłanie samych obrazków traktujemy jako scam. Zachowaj ostrożność.".ToEmbedMessage(EMType.Bot).Build());

            switch (action)
            {
                case Action.Warn:
                    await message.Channel.SendMessageAsync("",
                        embed: $"{user.Mention} zaraz przekroczysz granicę!".ToEmbedMessage(EMType.Bot).Build());
                    break;

                case Action.Mute:
                    if (IsDebugBuild())
                    {
                        await message.Channel.SendMessageAsync($"{user.Mention} No i właśnie dostałeś muta.");
                    }
                    else if (muteRole != null)
                    {
                        if (user.Roles.Contains(muteRole))
                            return;

                        using (var db = new Database.DatabaseContext(_config))
                        {
                            var info = await _moderator.MuteUserAsync(user, muteRole, null, userRole, db, 24, penaltyReason);
                            await _moderator.NotifyAboutPenaltyAsync(user, notifChannel, info);
                        }
                    }
                    break;

                case Action.Ban:
                    if (IsDebugBuild())
                        await message.Channel.SendMessageAsync($"{user.Mention} No i właśnie dostałeś bana.");
                    else
                        await user.Guild.AddBanAsync(user, 1, penaltyReason);
                    break;

                default:
                case Action.None:
                    break;
            }
        }

        private string GetPenaltyReason(Action action, string messageContent, bool hasSuspiciousUrl,
            bool hasNonWhitelistedUrl, int imageSpamCount, int totalMessages, int specifiedMessages, bool scamImage)
        {
            var penalty = action == Action.Ban ? "ban" : "mute";

            if (imageSpamCount >= 2 && scamImage)
                return $"Automatyczny {penalty}: scamowe obrazki - rozpoznano {imageSpamCount} scamowych obrazków w ciągu 2 minut.";

            if (imageSpamCount >= 3)
                return $"Automatyczny {penalty}: spam obrazkami - wysłano {imageSpamCount} wiadomości zawierających więcej niż trzy obrazki w ciągu 2 minut.";

            if (hasSuspiciousUrl)
                return $"Automatyczny ban: wykryto podejrzany link lub wzorzec phishingowy. Adresy: {GetUrlsDescription(messageContent)}";

            if (hasNonWhitelistedUrl)
                return $"Automatyczny ban: spam linkami - wykryto adres spoza listy zaufanych domen. Adresy: {GetUrlsDescription(messageContent)}";

            return $"Automatyczny {penalty}: spam/flood - {specifiedMessages} powtórzeń tej samej treści, łącznie {totalMessages} wiadomości w krótkim czasie.";
        }

        private string GetUrlsDescription(string content)
        {
            var urls = content.GetURLs().ToList();
            return urls.Count == 0 ? "nie odczytano adresu" : string.Join(" ", urls).TrimToLength(450);
        }

        private bool IsDebugBuild()
        {
            return isDebug;
        }

        private Action MakeDecision(string content, int total, int specified, bool hasRole)
        {
            int mSpecified = MAX_SPECIFIED;
            int mTotal = MAX_TOTAL;

            if (content.IsCommand(_config.Get().Prefix))
            {
                mTotal += COMMAND_MOD;
                mSpecified += COMMAND_MOD;
            }

            if (!hasRole)
            {
                mTotal += UNCONNECTED_MOD;
                mSpecified += UNCONNECTED_MOD;
            }

            int mWSpec = mSpecified - 1;
            int mWTot = mTotal - 1;

            if (total >= mTotal || specified >= mSpecified)
            {
                if (!hasRole) return Action.Ban;
                return Action.Mute;
            }

            if ((total >= mWTot || specified >= mWSpec) && hasRole)
                return Action.Warn;

            return Action.None;
        }

        private string GetMessageContent(SocketUserMessage message)
        {
            return string.IsNullOrEmpty(message.Content) ? "embed" : message.Content;
        }

        private bool HasMoreThanThreeImages(SocketUserMessage message)
        {
            return message.Attachments.Count(IsImageAttachment) > 3;
        }

        private async Task<ScamImageScanResult> ScanImagesAsync(SocketUserMessage message)
        {
            var images = message.Attachments.Where(IsImageAttachment).ToList();
            if (_scamImages.SignatureCount == 0 || images.Count == 0)
                return new ScamImageScanResult();

            return await _scamImages.ScanAsync(images);
        }

        public static bool IsAlwaysBanProtected(bool isOwner, bool isAdministrator, ulong adminRole, ulong semiAdminRole,
            IEnumerable<ulong> userRoles, IEnumerable<ulong> moderatorRoles)
        {
            return isOwner || isAdministrator ||
                IsAlwaysBanStaff(adminRole, semiAdminRole, userRoles, moderatorRoles);
        }

        public static bool ShouldPunishImageSpam(int imageSpamCount, bool scamImage)
            => imageSpamCount >= (scamImage ? 2 : 3);

        public static bool IsAlwaysBanStaff(ulong adminRole, ulong semiAdminRole, IEnumerable<ulong> userRoles,
            IEnumerable<ulong> moderatorRoles)
        {
            if (userRoles == null)
                return false;

            if (adminRole != 0 && userRoles.Contains(adminRole))
                return true;

            if (semiAdminRole != 0 && userRoles.Contains(semiAdminRole))
                return true;

            return moderatorRoles != null && userRoles.Any(x => moderatorRoles.Contains(x));
        }

        private bool IsImageSpamExempt(SocketUserMessage message)
        {
            return !string.IsNullOrEmpty(message.Content) &&
                CultureInfo.CurrentCulture.CompareInfo.IndexOf(
                    message.Content,
                    IMAGE_SPAM_EXEMPTION,
                    CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
        }

        private bool IsImageAttachment(IAttachment attachment)
        {
            var extension = Path.GetExtension(attachment.Filename);
            return _imageExtensions.Any(x => extension.Equals(x, StringComparison.OrdinalIgnoreCase));
        }

        private void AutoValidate()
        {
            try
            {
                // usuń wpisy dla gildii, na których bota już nie ma (słownik rósłby w nieskończoność)
                foreach (var guildId in _temporarySupervisionChannels.Keys.ToList())
                    if (_client.GetGuild(guildId) == null)
                        _temporarySupervisionChannels.Remove(guildId);

                var toClean = new Dictionary<ulong, List<ulong>>();
                foreach (var guild in _guilds)
                {
                    var usrs = new List<ulong>();
                    foreach (var susspect in guild.Value)
                    {
                        if (!susspect.Value.IsValid())
                            usrs.Add(susspect.Key);
                    }
                    toClean.Add(guild.Key, usrs);
                }

                foreach (var guild in toClean)
                {
                    foreach (var uId in guild.Value)
                        _guilds[guild.Key].Remove(uId);

                    if (_guilds[guild.Key].Count == 0)
                        _guilds.Remove(guild.Key);
                }

                var toClean2 = new Dictionary<ulong, List<string>>();
                foreach (var guild in _guildsJoin)
                {
                    var usrs = new List<string>();
                    foreach (var susspect in guild.Value)
                    {
                        if (!susspect.Value.IsValid())
                            usrs.Add(susspect.Key);
                    }
                    toClean2.Add(guild.Key, usrs);
                }

                foreach (var guild in toClean2)
                {
                    foreach (var nick in guild.Value)
                        _guildsJoin[guild.Key].Remove(nick);

                    if (_guildsJoin[guild.Key].Count == 0)
                        _guildsJoin.Remove(guild.Key);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Supervisor: autovalidate error {ex}");
            }
        }

        private async Task UserJoinedAsync(SocketGuildUser user)
        {
            if (!_config.Get().Supervision) return;

            var usr = user as SocketGuildUser;
            if (usr == null) return;

            if (usr.IsBot || usr.IsWebhook) return;

            if (_config.Get().BlacklistedGuilds.Any(x => x == user.Guild.Id))
                return;

            _ = Task.Run(async () =>
            {
                try
                {
                    using (await _semaphoreUser.LockAsync(usr.Id).ConfigureAwait(false))
                    {
                        await AnalizeJoin(usr);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Supervisor: analize join: {ex}");
                }
            });

            await Task.CompletedTask;
        }

        private async Task AnalizeJoin(SocketGuildUser user)
        {
            List<ulong> usersToBan = null;

            using (var db = new Database.DatabaseContext(_config))
            {
                var gConfig = await db.GetCachedGuildFullConfigAsync(user.Guild.Id);
                if (gConfig == null) return;

                if (!gConfig.Supervision) return;

                using (await _semaphoreJoin.LockAsync().ConfigureAwait(false))
                {
                    if (!_guildsJoin.Any(x => x.Key == user.Guild.Id))
                        _guildsJoin.Add(user.Guild.Id, new Dictionary<string, SupervisorJoinEntity>());

                    var guild = _guildsJoin[user.Guild.Id];
                    if (!guild.Any(x => x.Key == user.Username))
                        guild.Add(user.Username, new SupervisorJoinEntity(user.Id, _time));

                    var susspect = guild[user.Username];
                    if (!susspect.IsValid())
                    {
                        susspect = new SupervisorJoinEntity(user.Id, _time);
                        guild[user.Username] = susspect;
                    }

                    susspect.Add(user.Id);
                    if (susspect.IsBannable())
                        usersToBan = susspect.GetUsersToBan();
                }
            }

            if (usersToBan == null)
                return;

            foreach (var toBan in usersToBan)
            {
                try
                {
                    await user.Guild.AddBanAsync(toBan, 1,
                        $"Automatyczny ban: raid - wykryto {usersToBan.Count} kont, które dołączyły w ciągu 2 minut z tą samą nazwą użytkownika ({user.Username}).");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Supervisor: raid ban {toBan}: {ex}");
                }
            }
        }
    }
}
