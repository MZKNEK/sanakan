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
                var botuser = await db.GetUserWithDeckAsync(usr.Id);
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
                var botuser = await db.GetCachedNoGameDeckUserAsync(Context.User.Id);
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
                    await SafeReplyAsync("", embed: "Na tym serwerze nie ma ról do samodzielnego przyznania.".ToEmbedMessage(EMType.Neutral).Build());
                    return;
                }

                var roles = config.SelfRoles.Select(x => $"`{x.Name}` → {Context.Guild.GetRole(x.Role)?.Mention ?? "rola usunięta z serwera"}");
                var prefix = config.Prefix ?? Config.Get().Prefix;

                await SafeReplyAsync("", embed: $"{string.Join("\n", roles)}\n\nUżyj `{prefix}przyznaj role [nazwa]`, aby dodać rolę, albo `{prefix}zdejmij role [nazwa]`, aby ją zdjąć."
                    .TrimToLength(4000).ToEmbedMessage(EMType.Info).WithTitle("Role do wzięcia").Build());
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

                var remaining = botuser.GetRemainingExp();
                await SafeReplyAsync("", embed: $"{usr.Mention} potrzebuje **{remaining}** {Polish.Plural(remaining, "punkt", "punkty", "punktów")} doświadczenia do następnego poziomu."
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
                session.ListItems = _profile.BuildListView(await _profile.GetTopUsers(db, type), Context.Guild);
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

        public const string ProfileCommand = "profil";
        public static readonly TimeSpan ProfileCooldown = TimeSpan.FromMinutes(5);

        public static string SavedProfileNote(DateTime savedAt, DateTime availableAt)
            => $"Zapisany profil z {savedAt.ToRemTime()} · nowy wygenerujesz {availableAt.ToRemTime()}";

        [Command(ProfileCommand, RunMode = RunMode.Async)]
        [Alias("profile")]
        [Summary("wyświetla profil użytkownika")]
        [Remarks("karna")]
        public async Task ShowUserProfileAsync([Summary("nazwa użytkownika")]SocketGuildUser user = null)
        {
            var usr = user ?? Context.User as SocketGuildUser;
            if (usr == null || Context.User is not SocketGuildUser caller) return;

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

                var url = Api.Models.CardFinalView.GetProfileUrl(dataUser.Id);
                var profileUri = $"{Dir.FakeCardsAsProfiles}/{dataUser.Id}.webp";

                var availableAt = await DelayNextUseBy.TryUseAsync(caller, ProfileCommand, ProfileCooldown, Config, _time);
                if (availableAt != null)
                {
                    if (!await TrySendSavedImageAsync(profileUri, savedAt => SavedProfileNote(savedAt, availableAt.Value)))
                        await SafeReplyAsync("", embed: DelayNextUseBy.CooldownMessage(caller, availableAt.Value).ToEmbedMessage(EMType.Error).Build());
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
                using (var image = await _profile.GetLayeredProfileImageAsync(db, dataUser, usr, rankingPosition))
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
                var botuser = await db.GetUserOrCreateLightAsync(Context.User.Id, _time);
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

                        QueryCache.ExpireTag(new string[] { CacheTags.User(botuser.Id) });

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

        private const string ProfilatorUrl = "https://sanakan.pl/profilator/";
        private const string ProfilatorLabel = "Kliknij tutaj, by przejść do Profilatora";

        // the old settings of the profile with any arguments end here too, pointed to Profilator
        [Command("konfiguracja profilu")]
        [Alias("configure profile", "konprof", "conprof", "conp", "konp")]
        [Summary("wskazuje Profilator, w którym ustawia się wygląd profilu")]
        [Remarks(""), RequireCommandChannel]
        public async Task ConfigureProfileAsync([Summary("dawne ustawienia (nieużywane)")][Remainder]string old = null)
        {
            var button = new ComponentBuilder().WithButton(ProfilatorLabel, style: ButtonStyle.Link, url: ProfilatorUrl).Build();
            await SafeReplyAsync("", embed: ($"{Context.User.Mention} profil ustawiasz w Profilatorze, po zalogowaniu kontem Discord. "
                + "Powrót do domyślnego wyglądu: `konfiguracja profilu domyślny tak`.").ToEmbedMessage(EMType.Info).Build(), components: button);
        }

        [Command("konfiguracja profilu domyślny"), Priority(1)]
        [Alias("konfiguracja profilu domyslny", "konprof domyślny", "konprof domyslny", "konp domyślny", "konp domyslny", "conp default", "configure profile default")]
        [Summary("przywraca domyślny wygląd profilu; odblokowania zostają")]
        [Remarks("tak"), RequireCommandChannel]
        public async Task ResetLayeredProfileAsync([Summary("potwierdzenie (tak)")]string confirm = null)
        {
            if (!string.Equals(confirm, "tak", StringComparison.OrdinalIgnoreCase))
            {
                await SafeReplyAsync("", embed: ($"{Context.User.Mention} to ustawi domyślny wygląd profilu zamiast twojego układu. Odblokowania zostają, "
                    + "a twoje obrazki zostają w Profilatorze do najbliższego zapisu. Potwierdź: `konfiguracja profilu domyślny tak`.").ToEmbedMessage(EMType.Warning).Build());
                return;
            }

            using (var db = new Database.DatabaseContext(Config))
                await _profile.Store.ResetAsync(db, await db.GetUserOrCreateSimpleAsync(Context.User.Id, _time), removePictures: false);

            await SafeReplyAsync("", embed: $"{Context.User.Mention} przywrócono domyślny wygląd profilu.".ToEmbedMessage(EMType.Success).Build());
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
                var botuser = await db.GetUserOrCreateSimpleAsync(user.Id, _time);
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

                QueryCache.ExpireTag(new string[] { CacheTags.User(botuser.Id) });

                await SafeReplyAsync("", embed: $"{user.Mention} wykupił miesiąc globalnych emotek, ważne do {global.EndsAt.ToShortDateTime()}.".ToEmbedMessage(EMType.Success).Build());
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
                var botuser = await db.GetUserOrCreateSimpleAsync(user.Id, _time);
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

                QueryCache.ExpireTag(new string[] { CacheTags.User(botuser.Id) });

                await user.AddRoleAsync(selectedRole);

                await SafeReplyAsync("", embed: $"{user.Mention} wykupił kolor, ważny do {colort.EndsAt.ToShortDateTime()}.".ToEmbedMessage(EMType.Success).Build());
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
                var botuser = await db.GetUserOrCreateSimpleAsync(user.Id, _time);
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
                        await SafeReplyAsync("", embed: $"{user.Mention} nie udało się nadać koloru, opłata nie została pobrana. Zgłoś to administracji.".ToEmbedMessage(EMType.Error).Build());
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

                QueryCache.ExpireTag(new string[] { CacheTags.User(botuser.Id) });

                var reply = color == FColor.CleanColor
                    ? $"{user.Mention} zdjęto kolor."
                    : $"{user.Mention} wykupił kolor, ważny do {colort.EndsAt.ToShortDateTime()}.";
                await SafeReplyAsync("", embed: reply.ToEmbedMessage(EMType.Success).Build());
            }
        }
    }
}
