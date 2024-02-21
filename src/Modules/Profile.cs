#pragma warning disable 1591

using System;
using System.Collections.Generic;
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
                    await ReplyAsync("", embed: "Ta osoba nie ma profilu bota.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                await ReplyAsync("", embed: ($"**Portfel** {usr.Mention}:\n\n"
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
                var rsubs = botuser.TimeStatuses.Where(x => x.Type.IsSubType());

                string subs = "brak";
                if (rsubs.Count() > 0)
                {
                    subs = "";
                    foreach (var sub in rsubs)
                        subs += $"{sub.ToView(_time.Now())}\n";
                }

                await ReplyAsync("", embed: $"**Subskrypcje** {Context.User.Mention}:\n\n{subs.TrimToLength()}".ToEmbedMessage(EMType.Info).Build());
            }
        }

        [Command("przyznaj role", RunMode = RunMode.Async)]
        [Alias("add role")]
        [Summary("dodaje samo zarządzaną role")]
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
                    await ReplyAsync("", embed: $"Nie odnaleziono roli `{name}`".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (!user.Roles.Contains(gRole))
                    await user.AddRoleAsync(gRole);

                await ReplyAsync("", embed: $"{user.Mention} przyznano rolę: `{name}`".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("zdejmij role", RunMode = RunMode.Async)]
        [Alias("remove role")]
        [Summary("zdejmuje samo zarządzaną role")]
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
                    await ReplyAsync("", embed: $"Nie odnaleziono roli `{name}`".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (user.Roles.Contains(gRole))
                    await user.RemoveRoleAsync(gRole);

                await ReplyAsync("", embed: $"{user.Mention} zdjęto rolę: `{name}`".ToEmbedMessage(EMType.Success).Build());
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
                    await ReplyAsync("", embed: "Nie odnaleziono roli.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                string stringRole = "";
                foreach (var selfRole in config.SelfRoles)
                {
                    var gRole = Context.Guild.GetRole(selfRole?.Role ?? 0);
                    stringRole += $" `{selfRole.Name}` ";
                }

                await ReplyAsync($"**Dostępne role:**\n{stringRole}\n\nUżyj `s.przyznaj role [nazwa]` aby dodać lub `s.zdejmij role [nazwa]` odebrać sobie role.");
            }
        }

        [Command("statystyki", RunMode = RunMode.Async)]
        [Alias("stats")]
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
                    await ReplyAsync("", embed: "Ta osoba nie ma profilu bota.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                await ReplyAsync("", embed: botuser.GetStatsView(usr).Build());
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
                    await ReplyAsync("", embed: "Ta osoba nie ma profilu bota.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                await ReplyAsync("", embed: $"{usr.Mention} potrzebuje **{botuser.GetRemainingExp()}** punktów doświadczenia do następnego poziomu."
                    .ToEmbedMessage(EMType.Info).Build());
            }
        }

        [Command("topka", RunMode = RunMode.Async)]
        [Alias("top")]
        [Summary("wyświetla topke użytkowników")]
        [Remarks(""), RequireAnyCommandChannel]
        public async Task ShowTopAsync([Summary("rodzaj topki (poziom/sc/tc/pc/ac/posty(m/ms)/kart(a/y/ym)/karma(-))/pvp(s)")]TopType type = TopType.Level)
        {
            var session = new ListSession<string>(Context.User, Context.Client.CurrentUser);
            await _session.KillSessionIfExistAsync(session);

            var building = await ReplyAsync("", embed: $"🔨 Trwa budowanie topki...".ToEmbedMessage(EMType.Bot).Build());
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
            var msg = await ReplyAsync("", embed: session.BuildPage(0));
            await msg.AddReactionsAsync(new[] { new Emoji("⬅"), new Emoji("➡") });

            session.Message = msg;
            await _session.TryAddSession(session);
        }

        [Command("profil", RunMode = RunMode.Async)]
        [Alias("profile")]
        [Summary("wyświetla profil użytkownika")]
        [Remarks("karna"), DelayNextUseBy(15)]
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
                    await ReplyAsync("", embed: "Ta osoba nie ma profilu bota.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                long rankingPosition = 0;
                if (!isBot)
                {
                    var allUsers = await db.GetCachedAllUsersLiteAsync(1);
                    var rankingUser = allUsers.FirstOrDefault(x => x.Id == searchId);
                    rankingPosition = allUsers.OrderByDescending(x => x.ExpCnt).ToList().IndexOf(rankingUser) + 1;
                }

                dataUser.GameDeck.Cards = (await db.GetCachedUserGameDeckAsync(searchId)).Cards;
                using (var stream = await _profile.GetProfileImageAsync(usr, dataUser, rankingPosition))
                {
                    await Context.Channel.SendFileAsync(stream, $"{usr.Id}.png");
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
                        QueryCacheManager.ExpireTag(new string[] { $"user-{botuser.Id}", "users" });

                        await ReplyAsync("", embed: $"**Odebrane nagrody:**\n\n{string.Join("\n", rewards)}".ToEmbedMessage(EMType.Success).WithUser(Context.User).Build());
                        await db.SaveChangesAsync();
                        return;
                    }

                    await ReplyAsync("", embed: "Nie masz nic do odebrania.".ToEmbedMessage(EMType.Error).WithUser(Context.User).Build());
                    return;
                }

                string dailyTip = "Za wykonanie wszystkich dziennych misji można otrzymać 10 AC.";
                string totalTip = "Dzienne misje odświeżają się o północy, a tygodniowe co niedzielę.";
                string daily = $"**Dzienne misje:**\n\n{string.Join("\n", dailyQuests.Select(x => x.ToView(_time.Now())))}";
                string weekly = $"**Tygodniowe misje:**\n\n{string.Join("\n", weeklyQuests.Select(x => x.ToView(_time.Now())))}";

                await ReplyAsync("", embed: $"{daily}\n\n{dailyTip}\n\n\n{weekly}\n\n{totalTip}".ToEmbedMessage(EMType.Bot).WithUser(Context.User).Build());
            }
        }

        [Command("konfiguracja profilu")]
        [Alias("configure profile", "konprof", "confprof")]
        [Summary("pozwala ustawić konfigurowac profil użytkownika")]
        [Remarks("konfiguracja profilu info"), RequireCommandChannel]
        public async Task ConfigureProfileAsync([Summary("konfiguracja (podanie info wyświetla dodatkowe informacje)")][Remainder]ProfileConfig config)
        {
            if (config.Type == ProfileConfigType.ShowInfo)
            {
                await ReplyAsync("", embed: config.GetHelp().ToEmbedMessage(EMType.Info).Build());
                return;
            }

            using (var db = new Database.DatabaseContext(Config))
            {
                var botuser = await db.GetUserOrCreateSimpleAsync(Context.User.Id);

                var toPay = config.ToPay();
                if (config.NeedPay() && !botuser.Pay(toPay))
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie masz wystarczającej liczby **{toPay.Type}**, potrzebujesz {toPay.Cost}.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                switch (config.Type)
                {
                    case ProfileConfigType.BackgroundAndStyle:
                    {
                        if (!config.StyleNeedUrl())
                        {
                            await ReplyAsync("", embed: $"{Context.User.Mention} tym poleceniem możesz ustawić tylko style wymagające obarazka!".ToEmbedMessage(EMType.Error).Build());
                            return;
                        }

                        var res = await _profile.SaveProfileAndStyleImageAsync(config.Url, $"{Dir.SavedData}/BG{botuser.Id}.png", 750, 160, $"{Dir.SavedData}/SR{botuser.Id}.png", 750, 340);
                        if (res != SaveResult.Success)
                        {
                            await ReplyAsync("", embed: $"{Context.User.Mention} nie wykryto obrazka! Upewnij się, że podałeś poprawny adres!".ToEmbedMessage(EMType.Error).Build());
                            return;
                        }

                        botuser.StatsReplacementProfileUri = $"{Dir.SavedData}/SR{botuser.Id}.png";
                        botuser.BackgroundProfileUri = $"{Dir.SavedData}/BG{botuser.Id}.png";
                        botuser.ProfileType = config.Style;
                    }
                    break;

                    case ProfileConfigType.Background:
                    {
                        var res = await _profile.SaveProfileImageAsync(config.Url, $"{Dir.SavedData}/BG{botuser.Id}.png", 750, 160, true);
                        if (res != SaveResult.Success)
                        {
                            await ReplyAsync("", embed: $"{Context.User.Mention} nie wykryto obrazka! Upewnij się, że podałeś poprawny adres!".ToEmbedMessage(EMType.Error).Build());
                            return;
                        }

                        botuser.BackgroundProfileUri = $"{Dir.SavedData}/BG{botuser.Id}.png";
                    }
                    break;

                    case ProfileConfigType.Style:
                    {
                        if (config.StyleNeedUrl())
                        {
                            var res = await _profile.SaveProfileImageAsync(config.Url, $"{Dir.SavedData}/SR{botuser.Id}.png", 750, 340);
                            if (res != SaveResult.Success)
                            {
                                await ReplyAsync("", embed: $"{Context.User.Mention} nie wykryto obrazka! Upewnij się, że podałeś poprawny adres!".ToEmbedMessage(EMType.Error).Build());
                                return;
                            }

                            botuser.StatsReplacementProfileUri = $"{Dir.SavedData}/SR{botuser.Id}.png";
                        }

                        botuser.ProfileType = config.Style;
                    }
                    break;

                    case ProfileConfigType.Overlay:
                    {
                        if (_img.CheckImageUrl(ref config.Url) != ImageUrlCheckResult.Ok)
                        {
                            await ReplyAsync("", embed: $"{Context.User.Mention} nie wykryto obrazka! Upewnij się, że podałeś poprawny adres!".ToEmbedMessage(EMType.Error).Build());
                            return;
                        }

                        botuser.CustomProfileOverlayUrl = config.Url;
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
                            await ReplyAsync("", embed: $"{Context.User.Mention} nie możesz zmienić tej opcji na obecnie ustawionym stylu!".ToEmbedMessage(EMType.Error).Build());
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
                    {
                        if (!config.CanUseSettingOnStyle(botuser.ProfileType))
                        {
                            await ReplyAsync("", embed: $"{Context.User.Mention} nie możesz zmienić tej opcji na obecnie ustawionym stylu!".ToEmbedMessage(EMType.Error).Build());
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

                QueryCacheManager.ExpireTag(new string[] { $"user-{botuser.Id}", "users" });

                await ReplyAsync("", embed: $"{Context.User.Mention} {config.What()}".ToEmbedMessage(EMType.Success).Build());
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
                    await ReplyAsync("", embed: $"{user.Mention} nie posiadasz wystarczającej liczby TC!".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var gConfig = await db.GetCachedGuildFullConfigAsync(Context.Guild.Id);
                var gRole = Context.Guild.GetRole(gConfig.GlobalEmotesRole);
                if (gRole == null)
                {
                    await ReplyAsync("", embed: "Serwer nie ma ustawionej roli globalnych emotek.".ToEmbedMessage(EMType.Bot).Build());
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

                global.BValue = true;
                global.EndsAt = global.EndsAt.AddMonths(1);
                botuser.TcCnt -= cost;

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"user-{botuser.Id}", "users" });

                await ReplyAsync("", embed: $"{user.Mention} wykupił miesiąc globalnych emotek!".ToEmbedMessage(EMType.Success).Build());
            }
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
                    await ReplyAsync("", embed: $"{user.Mention} nie posiadasz wystarczającej liczby {currency.ToString().ToUpper()}!".ToEmbedMessage(EMType.Error).Build());
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
                        await ReplyAsync("", embed: $"Coś poszło nie tak!".ToEmbedMessage(EMType.Error).Build());
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

                QueryCacheManager.ExpireTag(new string[] { $"user-{botuser.Id}", "users" });

                await ReplyAsync("", embed: $"{user.Mention} wykupił kolor!".ToEmbedMessage(EMType.Success).Build());
            }
        }
    }
}
