#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Preconditions;
using Sanakan.Services;
using Sanakan.Services.Commands;
using Sanakan.Services.PocketWaifu;
using Sanakan.Services.Session;
using Sanakan.Services.Session.Models;
using Sanakan.Services.Time;
using Z.EntityFramework.Plus;

namespace Sanakan.Modules
{
    [Name("Profil"), RequireUserRole]
    public class Profile : SanakanModuleBase<SocketCommandContext>
    {
        private Services.Profile _profile;
        private SessionManager _session;
        private ImageProcessing _img;
        private ISystemTime _time;

        public Profile(Services.Profile prof, SessionManager session, ISystemTime time, ImageProcessing img)
        {
            _img = img;
            _time = time;
            _profile = prof;
            _session = session;
        }

        [Command("portfel", RunMode = RunMode.Async)]
        [Alias("wallet")]
        [Summary("wyświetla portfel użytkownika")]
        [Remarks("Karna")]
        public async Task ShowWalletAsync([Summary("nazwa użytkownika")]SocketUser user = null)
        {
            var usr = user ?? Context.User;
            if (usr == null) return;

            using (var db = new Database.DatabaseContext(Config))
            {
                var botuser = await db.GetCachedFullUserAsync(usr.Id);
                if (botuser == null)
                {
                    await SafeReplyAsync("", embed: "Ta osoba nie ma profilu bota.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                await SafeReplyAsync("", embed: ($"**Portfel** {usr.Mention}:\n\n"
                    + $"<:msc:1209243856759947296> {botuser?.ScCnt}\n"
                    + $"<:mtc:1209243855572967464> {botuser?.TcCnt}\n"
                    + $"<:mac:1209243865836683284> {botuser?.AcCnt}\n\n"
                    + $"**PW**:\n"
                    + $"<:mct:1209243877685338162> {botuser?.GameDeck?.CTCnt}\n"
                    + $"<:mpc:1209243854436569089> {botuser?.GameDeck?.PVPCoins}").ToEmbedMessage(EMType.Info).Build());
            }
        }

        [Command("subskrypcje", RunMode = RunMode.Async)]
        [Alias("sub")]
        [Summary("wyświetla daty zakończenia subskrypcji")]
        [Remarks(""), RequireCommandChannel]
        public async Task ShowSubsAsync()
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var botuser = await db.GetCachedFullUserAsync(Context.User.Id);
                if (botuser == null)
                {
                    await SafeReplyAsync("", embed: $"{Context.User.Mention} nie odnaleziono profilu.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var rsubs = botuser.TimeStatuses.Where(x => x.Type.IsSubType());

                string subs = "brak";
                if (rsubs.Count() > 0)
                {
                    subs = "";
                    foreach (var sub in rsubs)
                        subs += $"{sub.ToView(_time.Now())}\n";
                }

                await SafeReplyAsync("", embed: $"**Subskrypcje** {Context.User.Mention}:\n\n{subs.TrimToLength()}".ToEmbedMessage(EMType.Info).Build());
            }
        }

        [Command("przyznaj role", RunMode = RunMode.Async)]
        [Alias("add role")]
        [Summary("dodaje samozarządzaną rolę")]
        [Remarks("newsy"), RequireCommandChannel]
        public async Task AddRoleAsync([Summary("nazwa roli z wypisz role")]string name)
        {
            var user = Context.User as SocketGuildUser;
            if (user == null) return;

            using (var db = new Database.DatabaseContext(Config))
            {
                var config = await db.GetCachedGuildFullConfigAsync(Context.Guild.Id);
                var selfRole = config.SelfRoles.FirstOrDefault(x => x.Name == name);
                var gRole = Context.Guild.GetRole(selfRole?.Role ?? 0);

                if (gRole == null)
                {
                    await SafeReplyAsync("", embed: $"Nie odnaleziono roli `{name}`".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (!user.Roles.Contains(gRole))
                    await user.AddRoleAsync(gRole);

                await SafeReplyAsync("", embed: $"{user.Mention} przyznano rolę: `{name}`".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("zdejmij role", RunMode = RunMode.Async)]
        [Alias("remove role")]
        [Summary("zdejmuje samozarządzaną rolę")]
        [Remarks("newsy"), RequireCommandChannel]
        public async Task RemoveRoleAsync([Summary("nazwa roli z wypisz role")]string name)
        {
            var user = Context.User as SocketGuildUser;
            if (user == null) return;

            using (var db = new Database.DatabaseContext(Config))
            {
                var config = await db.GetCachedGuildFullConfigAsync(Context.Guild.Id);
                var selfRole = config.SelfRoles.FirstOrDefault(x => x.Name == name);
                var gRole = Context.Guild.GetRole(selfRole?.Role ?? 0);

                if (gRole == null)
                {
                    await SafeReplyAsync("", embed: $"Nie odnaleziono roli `{name}`".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (user.Roles.Contains(gRole))
                    await user.RemoveRoleAsync(gRole);

                await SafeReplyAsync("", embed: $"{user.Mention} zdjęto rolę: `{name}`".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("wypisz role", RunMode = RunMode.Async)]
        [Summary("wypisuje samozarządzane role")]
        [Remarks(""), RequireCommandChannel]
        public async Task ShowRolesAsync()
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var config = await db.GetCachedGuildFullConfigAsync(Context.Guild.Id);
                if (config.SelfRoles.Count < 1)
                {
                    await SafeReplyAsync("", embed: "Nie odnaleziono roli.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                string stringRole = "";
                foreach (var selfRole in config.SelfRoles)
                {
                    var gRole = Context.Guild.GetRole(selfRole?.Role ?? 0);
                    stringRole += $" `{selfRole.Name}` ";
                }

                await SafeReplyAsync($"**Dostępne role:**\n{stringRole}\n\nUżyj `s.przyznaj role [nazwa]` aby dodać lub `s.zdejmij role [nazwa]` odebrać sobie rolę.");
            }
        }

        [Command("statystyki", RunMode = RunMode.Async)]
        [Alias("stats"), RequireAnyCommandChannel]
        [Summary("wyświetla statystyki użytkownika")]
        [Remarks("karna")]
        public async Task ShowStatsAsync([Summary("nazwa użytkownika")]SocketUser user = null)
        {
            var usr = user ?? Context.User;
            if (usr == null) return;

            using (var db = new Database.DatabaseContext(Config))
            {
                var botuser = await db.GetBaseUserAndDontTrackAsync(usr.Id);
                if (botuser == null)
                {
                    await SafeReplyAsync("", embed: "Ta osoba nie ma profilu bota.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                await SafeReplyAsync("", embed: botuser.GetStatsView(usr).Build());
            }
        }

        [Command("idp", RunMode = RunMode.Async)]
        [Alias("iledopoziomu", "howmuchtolevelup", "hmtlup")]
        [Summary("wyświetla ile pozostało punktów doświadczenia do następnego poziomu")]
        [Remarks("karna")]
        public async Task ShowHowMuchToLevelUpAsync([Summary("nazwa użytkownika")]SocketUser user = null)
        {
            var usr = user ?? Context.User;
            if (usr == null) return;

            using (var db = new Database.DatabaseContext(Config))
            {
                var botuser = await db.Users.AsQueryable().AsSplitQuery().Where(x => x.Id == usr.Id).AsNoTracking().FirstOrDefaultAsync();
                if (botuser == null)
                {
                    await SafeReplyAsync("", embed: "Ta osoba nie ma profilu bota.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                await SafeReplyAsync("", embed: $"{usr.Mention} potrzebuje **{botuser.GetRemainingExp()}** punktów doświadczenia do następnego poziomu."
                    .ToEmbedMessage(EMType.Info).Build());
            }
        }

        [Command("topka", RunMode = RunMode.Async)]
        [Alias("top")]
        [Summary("wyświetla topkę użytkowników")]
        [Remarks(""), RequireAnyCommandChannel]
        public async Task ShowTopAsync([Summary("rodzaj topki (poziom/sc/tc/pc/ac/posty(m/ms)/kart(a/y/ym)/karma(-))/pvp(s)")]TopType type = TopType.Level)
        {
            var session = new ListSession<string>(Context.User, Context.Client.CurrentUser);
            await _session.KillSessionIfExistAsync(session);

            var building = await SafeReplyAsync("", embed: $"🔨 Trwa budowanie topki...".ToEmbedMessage(EMType.Bot).Build());
            using (var db = new Database.DatabaseContext(Config))
            {
                session.ListItems = _profile.BuildListView(await _profile.GetTopUsers(db.GetQueryableAllUsers(), type), type, Context.Guild);
            }

            session.Event = ExecuteOn.ReactionAdded;
            session.Embed = new EmbedBuilder
            {
                Color = EMType.Info.Color(),
                Title = $"Topka {type.Name()}"
            };

            await building.DeleteAsync();
            var msg = await SafeReplyAsync("", embed: session.BuildPage(0));
            await msg.AddReactionsAsync(new[] { new Emoji("⬅"), new Emoji("➡") });

            session.Message = msg;
            await _session.TryAddSession(session);
        }

        [Command("profil", RunMode = RunMode.Async)]
        [Alias("profile")]
        [Summary("wyświetla profil użytkownika")]
        [Remarks("karna"), DelayNextUseBy(5)]
        public async Task ShowUserProfileAsync([Summary("nazwa użytkownika")]SocketGuildUser user = null)
        {
            var usr = user ?? Context.User as SocketGuildUser;
            if (usr == null) return;

            var isBot =  usr.Id == Context.Client.CurrentUser.Id;
            var searchId = isBot ? 1 : usr.Id;

            using (var db = new Database.DatabaseContext(Config))
            {
                var dataUser = db.Users.AsQueryable().Include(x => x.GameDeck).AsNoTracking().FirstOrDefault(x => x.Id == searchId);
                if (dataUser is null)
                {
                    await SafeReplyAsync("", embed: "Ta osoba nie ma profilu bota.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                long rankingPosition = 0;
                if (!isBot)
                {
                    var allUsers = await db.GetCachedAllUsersLiteAsync(1);
                    var rankingUser = allUsers.FirstOrDefault(x => x.Id == searchId);
                    rankingPosition = allUsers.OrderByDescending(x => x.ExpCnt).ToList().IndexOf(rankingUser) + 1;
                }

                var url = Api.Models.CardFinalView.GetProfileUrl(dataUser.Id);
                var profileUri = $"{Dir.FakeCardsAsProfiles}/{dataUser.Id}.webp";
                dataUser.GameDeck.Cards = (await db.GetCachedUserGameDeckAsync(searchId)).Cards;
                using (var image = await _profile.GetProfileImageAsync(usr, dataUser, rankingPosition))
                {
                    image.SaveToPath(profileUri);
                    try
                    {
                        await Context.Channel.SendFileAsync(profileUri);
                    }
                    catch(Exception)
                    {
                        await Context.Channel.SendMessageAsync(url);
                    }
                }
            }
        }

        [Command("misje")]
        [Alias("quest")]
        [Summary("wyświetla postęp misji użytkownika")]
        [Remarks("tak"), RequireAnyCommandChannel]
        public async Task ShowUserQuestsProgressAsync([Summary("czy odebrać nagrody?")]bool claim = false)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var botuser = await db.GetUserOrCreateSimpleAsync(Context.User.Id);
                var weeklyQuests = botuser.CreateOrGetAllWeeklyQuests();
                var dailyQuests = botuser.CreateOrGetAllDailyQuests();

                if (claim)
                {
                    var rewards = new List<string>();
                    var allClaimedBefore = dailyQuests.Count(x => x.IsClaimed(_time.Now())) == dailyQuests.Count;
                    foreach(var d in dailyQuests)
                    {
                        if (d.CanClaim(_time.Now()))
                        {
                            d.Claim(botuser);
                            rewards.Add(d.Type.GetRewardString());
                        }
                    }

                    if (!allClaimedBefore && dailyQuests.Count(x => x.IsClaimed(_time.Now())) == dailyQuests.Count)
                    {
                        botuser.AcCnt += 10;
                        rewards.Add("10 AC");
                    }

                    foreach(var w in weeklyQuests)
                    {
                        if (w.CanClaim(_time.Now()))
                        {
                            w.Claim(botuser);
                            rewards.Add(w.Type.GetRewardString());
                        }
                    }

                    if (rewards.Count > 0)
                    {
                        await db.SaveChangesAsync();

                        QueryCacheManager.ExpireTag(new string[] { CacheTags.User(botuser.Id) });

                        await SafeReplyAsync("", embed: $"**Odebrane nagrody:**\n\n{string.Join("\n", rewards)}".ToEmbedMessage(EMType.Success).WithUser(Context.User).Build());
                        return;
                    }

                    await SafeReplyAsync("", embed: "Nie masz nic do odebrania.".ToEmbedMessage(EMType.Error).WithUser(Context.User).Build());
                    return;
                }

                string dailyTip = "Za wykonanie wszystkich dziennych misji można otrzymać 10 AC.";
                string totalTip = "Dzienne misje odświeżają się o północy, a tygodniowe co niedzielę.";
                string daily = $"**Dzienne misje:**\n\n{string.Join("\n", dailyQuests.Select(x => x.ToView(_time.Now())))}";
                string weekly = $"**Tygodniowe misje:**\n\n{string.Join("\n", weeklyQuests.Select(x => x.ToView(_time.Now())))}";

                await SafeReplyAsync("", embed: $"{daily}\n\n{dailyTip}\n\n\n{weekly}\n\n{totalTip}".ToEmbedMessage(EMType.Bot).WithUser(Context.User).Build());
            }
        }

        [Command("konfiguracja profilu")]
        [Alias("configure profile", "konprof", "conprof", "conp", "konp")]
        [Summary("pozwala konfigurować profil użytkownika")]
        [Remarks("konfiguracja profilu info"), RequireCommandChannel]
        public async Task ConfigureProfileAsync([Summary("konfiguracja (podanie info wyświetla dodatkowe informacje)")][Remainder]ProfileConfig config)
        {
            if (config.Type == ProfileConfigType.ShowInfo)
            {
                await SafeReplyAsync("", embed: config.GetHelp().ToEmbedMessage(EMType.Info).Build());
                return;
            }

            if (config.Type == ProfileConfigType.Code)
            {
                await ConfigureProfileFromCodeAsync(config);
                return;
            }

            using (var db = new Database.DatabaseContext(Config))
            {
                var botuser = await db.GetUserOrCreateSimpleAsync(Context.User.Id);

                if (config.ChangesNothing(botuser))
                {
                    await SafeReplyAsync("", embed: $"{Context.User.Mention} masz już to ustawione.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var toPay = config.ToPay();
                if (config.NeedPay(botuser) && !botuser.Pay(toPay))
                {
                    await SafeReplyAsync("", embed: $"{Context.User.Mention} nie masz wystarczającej liczby **{toPay.Type}**, potrzebujesz {toPay.Cost}.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                switch (config.Type)
                {
                    case ProfileConfigType.BackgroundAndStyle:
                    {
                        if (!config.StyleNeedUrl())
                        {
                            await SafeReplyAsync("", embed: $"{Context.User.Mention} tym poleceniem możesz ustawić tylko style wymagające obrazka!".ToEmbedMessage(EMType.Error).Build());
                            return;
                        }

                        var bgUri = $"{Dir.SavedData}/BG{botuser.Id}.webp";
                        var styleUri = $"{Dir.SavedData}/SR{botuser.Id}.webp";
                        var res = await _profile.SaveProfileAndStyleImageAsync(config.Url, bgUri, 750, 160, styleUri, 750, 340);
                        if (res != SaveResult.Success)
                        {
                            await SafeReplyAsync("", embed: $"{Context.User.Mention} nie wykryto obrazka! Upewnij się, że podałeś poprawny adres!".ToEmbedMessage(EMType.Error).Build());
                            return;
                        }

                        botuser.StatsReplacementProfileUri = styleUri;
                        botuser.BackgroundProfileUri = bgUri;
                        botuser.ProfileType = config.Style;
                    }
                    break;

                    case ProfileConfigType.Background:
                    {
                        var bgUri = $"{Dir.SavedData}/BG{botuser.Id}.webp";
                        var res = await _profile.SaveProfileImageAsync(config.Url, bgUri, 750, 160, true);
                        if (res != SaveResult.Success)
                        {
                            await SafeReplyAsync("", embed: $"{Context.User.Mention} nie wykryto obrazka! Upewnij się, że podałeś poprawny adres!".ToEmbedMessage(EMType.Error).Build());
                            return;
                        }

                        botuser.BackgroundProfileUri = bgUri;
                    }
                    break;

                    case ProfileConfigType.Style:
                    {
                        if (config.StyleNeedUrl())
                        {
                            var styleUri = $"{Dir.SavedData}/SR{botuser.Id}.webp";
                            var res = await _profile.SaveProfileImageAsync(config.Url, styleUri, 750, 340);
                            if (res != SaveResult.Success)
                            {
                                await SafeReplyAsync("", embed: $"{Context.User.Mention} nie wykryto obrazka! Upewnij się, że podałeś poprawny adres!".ToEmbedMessage(EMType.Error).Build());
                                return;
                            }

                            botuser.StatsReplacementProfileUri = styleUri;
                        }

                        botuser.ProfileType = config.Style;
                    }
                    break;

                    case ProfileConfigType.Overlay:
                    {
                        var ovUri = $"{Dir.SavedData}/OV{botuser.Id}.webp";
                        var res = await _profile.SaveProfileImageAsync(config.Url, ovUri, 750, 402);
                        if (res != SaveResult.Success)
                        {
                            await SafeReplyAsync("", embed: $"{Context.User.Mention} nie wykryto obrazka! Upewnij się, że podałeś poprawny adres!".ToEmbedMessage(EMType.Error).Build());
                            return;
                        }

                        botuser.CustomProfileOverlayUrl = ovUri;
                    }
                    break;

                    case ProfileConfigType.PremiumOverlay:
                    {
                        var uovUri = $"{Dir.SavedData}/UOV{botuser.Id}.webp";
                        var res = await _profile.SaveProfileImageAsync(config.Url, uovUri, 750, 500);
                        if (res != SaveResult.Success)
                        {
                            await SafeReplyAsync("", embed: $"{Context.User.Mention} nie wykryto obrazka! Upewnij się, że podałeś poprawny adres!".ToEmbedMessage(EMType.Error).Build());
                            return;
                        }

                        botuser.PremiumCustomProfileOverlayUrl = uovUri;
                    }
                    break;

                    case ProfileConfigType.AvatarBorder:
                    {
                        botuser.AvatarBorder = config.Border;
                    }
                    break;

                    case ProfileConfigType.ShadowsOpacity:
                    {
                        if (!config.IsConfigurableStyle(botuser.ProfileType))
                        {
                            await SafeReplyAsync("", embed: $"{Context.User.Mention} nie możesz zmienić tej opcji na obecnie ustawionym stylu!".ToEmbedMessage(EMType.Error).Build());
                            return;
                        }

                        botuser.ProfileShadowsOpacity = config.PercentToOpacity();
                    }
                    break;

                    case ProfileConfigType.Bar:
                    case ProfileConfigType.MiniFavCard:
                    case ProfileConfigType.AnimeStats:
                    case ProfileConfigType.MangaStats:
                    case ProfileConfigType.CardsStats:
                    case ProfileConfigType.MiniGallery:
                    case ProfileConfigType.CardCntInMiniGallery:
                    case ProfileConfigType.FlipPanels:
                    case ProfileConfigType.LevelAvatarBorder:
                    case ProfileConfigType.RoundAvatarWithoutBorder:
                    case ProfileConfigType.CustomBarOpacity:
                    case ProfileConfigType.OverlayVisibility:
                    case ProfileConfigType.PremiumOverlayVisibility:
                    {
                        if (!config.CanUseSettingOnStyle(botuser.ProfileType))
                        {
                            await SafeReplyAsync("", embed: $"{Context.User.Mention} nie możesz zmienić tej opcji na obecnie ustawionym stylu!".ToEmbedMessage(EMType.Error).Build());
                            return;
                        }

                        if (config.ToggleCurentValue)
                        {
                            botuser.StatsStyleSettings ^= config.Settings;
                        }
                    }
                    break;

                    default:
                    break;
                }

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { CacheTags.User(botuser.Id) });

                await SafeReplyAsync("", embed: $"{Context.User.Mention} {config.What()}".ToEmbedMessage(EMType.Success).Build());
            }
        }

        private async Task ConfigureProfileFromCodeAsync(ProfileConfig config)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var botuser = await db.GetUserOrCreateSimpleAsync(Context.User.Id);
                if (config.Code == null)
                {
                    await ShowProfileCodeAsync(db, botuser, config.WithImages);
                    return;
                }

                var code = ProfileCode.Decode(config.Code);
                if (code == null)
                {
                    await SafeReplyAsync("", embed: $"{Context.User.Mention} podany kod jest niepoprawny.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var styleAttachment = config.Url != null;
                var error = code.Validate(botuser, styleAttachment, TypeReaders.ProfileConfigTypeReader.IsSelectableBorder);
                if (error != null)
                {
                    await SafeReplyAsync("", embed: $"{Context.User.Mention} {error}".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (!code.ChangesAnything(botuser, styleAttachment))
                {
                    await SafeReplyAsync("", embed: $"{Context.User.Mention} twój profil ma już te ustawienia.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var costs = code.GetCosts(botuser, styleAttachment, config.Currency);
                var total = costs.Sum(x => x.Cost.Cost);

                // the context is never saved here, Pay only checks the balance
                if (total > 0 && !botuser.Pay(new CurrencyCost((int)total, config.Currency)))
                {
                    await SafeReplyAsync("", embed: $"{Context.User.Mention} nie masz wystarczającej liczby **{config.Currency}**, potrzebujesz {total}.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var session = new AcceptSession(Context.User, null, Context.Client.CurrentUser);
                await _session.KillSessionIfExistAsync(session);

                var actions = new AcceptProfileCode(Config)
                {
                    User = Context.User,
                    Code = code,
                    Currency = config.Currency,
                    Total = total,
                    StyleAttachment = styleAttachment,
                };

                var failed = await DownloadCodeImagesAsync(actions, code, config.Url, botuser.Id);
                if (failed != null)
                {
                    actions.DeletePending();
                    var why = failed == AttachmentName ? "nie wykryto obrazka w załączniku! Upewnij się, że podałeś poprawny adres!"
                        : $"nie udało się pobrać obrazka z kodu ({failed}), link mógł wygasnąć.";
                    await SafeReplyAsync("", embed: $"{Context.User.Mention} {why}".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var withImages = code.Images.Keys.OrderBy(x => x).Select(x => ImageNames[x]).ToList();
                if (styleAttachment)
                    withImages.Add("obrazek stylu z załącznika");

                var price = costs.Count == 0 ? "bez opłat"
                    : string.Join("\n", costs.Select(x => $"{x.What}: {x.Cost}")) + $"\n**Razem:** {total} {config.Currency}";

                var msg = await SafeReplyAsync("", embed: $"{Context.User.Mention} ustawić profil z kodu{(withImages.Count > 0 ? " z obrazkami: " + string.Join(", ", withImages) : "")}?\n\n**Koszt:**\n{price}".ToEmbedMessage(EMType.Bot).Build());
                await msg.AddReactionsAsync(session.StartReactions);
                actions.Message = msg;
                session.Actions = actions;
                session.Message = msg;

                await _session.TryAddSession(session);
            }
        }

        private static readonly Dictionary<ProfileImage, string> ImageNames = new Dictionary<ProfileImage, string>
        {
            { ProfileImage.Background, "tło" },
            { ProfileImage.Style, "obrazek stylu" },
            { ProfileImage.Overlay, "nakładka" },
            { ProfileImage.Premium, "ultra nakładka" },
            { ProfileImage.BackgroundAndStyle, "tło i styl" },
        };

        private const string AttachmentName = "załącznik";

        // every image of the code downloaded before the price is shown, as the commands for each image do;
        // null when all came, otherwise the name of the one that did not
        private async Task<string> DownloadCodeImagesAsync(AcceptProfileCode actions, ProfileCode code, string attachment, ulong userId)
        {
            async Task<bool> Save(ProfileImage image, string url, int width, int height, bool stretch = false)
            {
                var path = AcceptProfileCode.PendingPath(image, userId);
                actions.PendingImages[image] = path;
                return await _profile.SaveProfileImageAsync(url, path, width, height, stretch) == SaveResult.Success;
            }

            if (attachment != null && !await Save(ProfileImage.Style, attachment, 750, 340))
                return AttachmentName;

            foreach (var (image, url) in code.Images.OrderBy(x => x.Key))
            {
                var ok = image switch
                {
                    ProfileImage.Background => await Save(ProfileImage.Background, url, 750, 160, true),
                    ProfileImage.Style => await Save(ProfileImage.Style, url, 750, 340),
                    ProfileImage.Overlay => await Save(ProfileImage.Overlay, url, 750, 402),
                    ProfileImage.Premium => await Save(ProfileImage.Premium, url, 750, 500),
                    _ => await SaveBackgroundAndStyleAsync(actions, url, userId),
                };
                if (!ok)
                    return ImageNames[image];
            }
            return null;
        }

        private async Task<bool> SaveBackgroundAndStyleAsync(AcceptProfileCode actions, string url, ulong userId)
        {
            var background = AcceptProfileCode.PendingPath(ProfileImage.Background, userId);
            var style = AcceptProfileCode.PendingPath(ProfileImage.Style, userId);
            actions.PendingImages[ProfileImage.Background] = background;
            actions.PendingImages[ProfileImage.Style] = style;
            return await _profile.SaveProfileAndStyleImageAsync(url, background, 750, 160, style, 750, 340) == SaveResult.Success;
        }

        // the code of the profile, with images links to the account's pictures sent to the trash channel
        private async Task ShowProfileCodeAsync(Database.DatabaseContext db, User botuser, bool withImages)
        {
            var code = ProfileCode.FromUser(botuser);
            var note = "";
            if (withImages)
            {
                var gConfig = await db.GetCachedGuildFullConfigAsync(Context.Guild.Id);
                var trash = Context.Guild.GetTextChannel(gConfig?.WaifuConfig?.TrashCommandsChannel ?? 0);
                if (trash == null)
                {
                    note = "\n\nNa tym serwerze nie ma kanału na obrazki, więc kod jest bez obrazków.";
                }
                else
                {
                    var current = new List<(ProfileImage Image, string Uri)>
                    {
                        (ProfileImage.Background, botuser.BackgroundProfileUri),
                        (ProfileImage.Overlay, botuser.CustomProfileOverlayUrl),
                        (ProfileImage.Premium, botuser.PremiumCustomProfileOverlayUrl),
                    };
                    if (ProfileConfig.StyleNeedsImage(botuser.ProfileType))
                        current.Add((ProfileImage.Style, botuser.StatsReplacementProfileUri));

                    foreach (var (image, uri) in current)
                    {
                        var link = await ImageLinkAsync(trash, uri);
                        if (link != null)
                            code.Images[image] = link;
                    }

                    note = code.Images.Count > 0 ? "\n\nKod z obrazkami działa przez 24 godziny, potem linki do obrazków wygasną. Każdy obrazek z kodu jest płatny przy ustawianiu."
                        : "\n\nNie masz własnych obrazków, więc kod jest bez nich.";
                }
            }

            var text = code.Encode();
            var how = code.Images.Count > 0 ? $"```{text}```\n*Ustawisz go poleceniem* `konfiguracja profilu kod <kod>`"
                : $"`{text}`\n\n*Ustawisz go poleceniem* `konfiguracja profilu kod {text}`";
            await SafeReplyAsync("", embed: $"{Context.User.Mention} kod twojego profilu: {how}{note}".ToEmbedMessage(EMType.Info).Build());
        }

        // a link to an image of the profile: a picture saved by the bot goes to the trash channel and its link is taken from there,
        // a link stays as it is, the bot's own pictures (the default background) are no image of the account
        private static async Task<string> ImageLinkAsync(ITextChannel trash, string uri)
        {
            if (string.IsNullOrEmpty(uri) || uri.StartsWith(Dir.GetResource(""), StringComparison.Ordinal))
                return null;

            if (!Dir.IsLocal(uri))
                return Uri.TryCreate(uri, UriKind.Absolute, out _) ? uri : null;

            if (!File.Exists(uri))
                return null;

            try
            {
                var message = await trash.SendFileAsync(uri);
                return message.Attachments.FirstOrDefault()?.Url;
            }
            catch (Exception)
            {
                return null;
            }
        }

        [Command("globalki")]
        [Alias("global")]
        [Summary("nadaje na miesiąc rangę od globalnych emotek (1000 TC)")]
        [Remarks(""), RequireCommandChannel]
        public async Task AddGlobalEmotesAsync()
        {
            var cost = 1000;
            var user = Context.User as SocketGuildUser;
            if (user == null) return;

            using (var db = new Database.DatabaseContext(Config))
            {
                var botuser = await db.GetUserOrCreateSimpleAsync(user.Id);
                if (botuser.TcCnt < cost)
                {
                    await SafeReplyAsync("", embed: $"{user.Mention} nie posiadasz wystarczającej liczby TC!".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var gConfig = await db.GetCachedGuildFullConfigAsync(Context.Guild.Id);
                var gRole = Context.Guild.GetRole(gConfig.GlobalEmotesRole);
                if (gRole == null)
                {
                    await SafeReplyAsync("", embed: "Serwer nie ma ustawionej roli globalnych emotek.".ToEmbedMessage(EMType.Bot).Build());
                    return;
                }

                var global = botuser.TimeStatuses.FirstOrDefault(x => x.Type == Database.Models.StatusType.Globals && x.Guild == Context.Guild.Id);
                if (global == null)
                {
                    global = StatusType.Globals.NewTimeStatus(Context.Guild.Id);
                    botuser.TimeStatuses.Add(global);
                }

                if (!user.Roles.Contains(gRole))
                    await user.AddRoleAsync(gRole);

                global.ExtendSubscription(_time.Now());
                botuser.TcCnt -= cost;

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { CacheTags.User(botuser.Id) });

                await SafeReplyAsync("", embed: $"{user.Mention} wykupił miesiąc globalnych emotek!".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("rkolor")]
        [Alias("rcolor", "rcolour")]
        [Summary("zmienia kolor użytkownika")]
        [Remarks("1"), RequireCommandChannel]
        public async Task ToggleRainbowColorRoleAsync([Summary("kolor z listy")] int idRoli = -1)
        {
            var user = Context.User as SocketGuildUser;
            if (user == null) return;

            string priceSeparator = ":";
            string colorName = _profile.GetRainbowColorNameStart();
            var allRoles = Context.Guild.Roles.Where(x => x.Name.StartsWith(colorName)).ToArray();
            if (allRoles.IsNullOrEmpty())
            {
                await SafeReplyAsync("", embed: $"{user.Mention} nie znaleziono żadnego koloru.".ToEmbedMessage(EMType.Error).Build());
                return;
            }

            if (idRoli < 0)
            {
                var rolesText = allRoles.Select((x, i) => $"{i + 1}: {x.Mention} | {x.Name.Split(priceSeparator).Last()} TC");
                await SafeReplyAsync("", embed: $"Kolory:\n\n 0: Kasowanie roli\n{string.Join("\n", rolesText)}".ToEmbedMessage(EMType.Info).Build());
                return;
            }

            using (var db = new Database.DatabaseContext(Config))
            {
                var botuser = await db.GetUserOrCreateSimpleAsync(user.Id);
                var colort = botuser.TimeStatuses.FirstOrDefault(x => x.Type == StatusType.RainbowColor && x.Guild == Context.Guild.Id);
                if (colort == null)
                {
                    colort = StatusType.RainbowColor.NewTimeStatus(Context.Guild.Id);
                    botuser.TimeStatuses.Add(colort);
                }

                if (idRoli == 0)
                {
                    await _profile.RemoveUserRainbowColorAsync(user, colorName);
                    await SafeReplyAsync("", embed: $"{user.Mention} zdjęto rolę koloru.".ToEmbedMessage(EMType.Success).Build());
                    return;
                }

                var roleIndex = idRoli - 1;
                if (roleIndex >= allRoles.Length)
                {
                    await SafeReplyAsync("", embed: $"{user.Mention} nie znaleziono takiego koloru.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var selectedRole = allRoles[roleIndex];
                var rolePriceText = selectedRole.Name.Split(priceSeparator).Last();
                if (!int.TryParse(rolePriceText, out var rolePrice) || rolePrice < 0)
                {
                    await SafeReplyAsync("", embed: $"{user.Mention} ta rola jest niepoprawnie skonfigurowana.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (botuser.TcCnt < rolePrice)
                {
                    await SafeReplyAsync("", embed: $"{user.Mention} nie posiadasz wystarczającej liczby TC!".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (user.Roles.Any(x => x.Id == selectedRole.Id) && colort.IsActive(_time.Now()))
                {
                    colort.ExtendSubscription(_time.Now());
                }
                else
                {
                    await _profile.RemoveUserRainbowColorAsync(user, colorName);
                    colort.StartSubscription(_time.Now());
                }

                botuser.TcCnt -= rolePrice;

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { CacheTags.User(botuser.Id) });

                await user.AddRoleAsync(selectedRole);
            }

            await SafeReplyAsync("", embed: $"{user.Mention} wykupił kolor!".ToEmbedMessage(EMType.Success).Build());
        }

        [Command("kolor")]
        [Alias("color", "colour")]
        [Summary("zmienia kolor użytkownika (koszt TC/SC na liście)")]
        [Remarks("pink sc"), RequireCommandChannel]
        public async Task ToggleColorRoleAsync([Summary("kolor z listy (none - lista)")]FColor color = FColor.None, [Summary("waluta (SC/TC)")]CurrencyType currency = CurrencyType.TC)
        {
            var user = Context.User as SocketGuildUser;
            if (user == null) return;

            if (color == FColor.None)
            {
                using (var img = _profile.GetColorList(currency))
                {
                    await Context.Channel.SendFileAsync(img, "list.png");
                    return;
                }
            }

            using (var db = new Database.DatabaseContext(Config))
            {
                var botuser = await db.GetUserOrCreateSimpleAsync(user.Id);
                var points = currency == CurrencyType.TC ? botuser.TcCnt : botuser.ScCnt;
                var gConfig = await db.GetCachedGuildFullConfigAsync(Context.Guild.Id);
                var hasNitro = gConfig.NitroRole != 0 && ((Context.User as SocketGuildUser)?.Roles?.Any(x => x.Id == gConfig.NitroRole) ?? false);
                if (!hasNitro && points < color.Price(currency))
                {
                    await SafeReplyAsync("", embed: $"{user.Mention} nie posiadasz wystarczającej liczby {currency.ToString().ToUpper()}!".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var colort = botuser.TimeStatuses.FirstOrDefault(x => x.Type == Database.Models.StatusType.Color && x.Guild == Context.Guild.Id);
                if (colort == null)
                {
                    colort = StatusType.Color.NewTimeStatus(Context.Guild.Id);
                    botuser.TimeStatuses.Add(colort);
                }

                if (color == FColor.CleanColor)
                {
                    colort.BValue = false;
                    colort.EndsAt = _time.Now();
                    await _profile.RomoveUserColorAsync(user);
                }
                else
                {
                    if (!hasNitro && _profile.HasSameColor(user, color) && colort.IsActive(_time.Now()))
                    {
                        colort.EndsAt = colort.EndsAt.AddMonths(1);
                    }
                    else
                    {
                        await _profile.RomoveUserColorAsync(user, color);
                        colort.EndsAt = _time.Now().AddMonths(1);
                    }

                    colort.BValue = true;
                    if (!await _profile.SetUserColorAsync(user, gConfig.MuteRole, color))
                    {
                        await SafeReplyAsync("", embed: $"Coś poszło nie tak!".ToEmbedMessage(EMType.Error).Build());
                        return;
                    }

                    if (!hasNitro)
                    {
                        if (currency == CurrencyType.TC)
                        {
                            botuser.TcCnt -= color.Price(currency);
                        }
                        else
                        {
                            botuser.ScCnt -= color.Price(currency);
                        }
                    }
                }

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { CacheTags.User(botuser.Id) });

                await SafeReplyAsync("", embed: $"{user.Mention} wykupił kolor!".ToEmbedMessage(EMType.Success).Build());
            }
        }
    }
}
