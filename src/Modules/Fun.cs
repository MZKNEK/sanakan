﻿#pragma warning disable 1591

using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Preconditions;
using Sanakan.Services;
using Sanakan.Services.Commands;
using Sanakan.Services.Session.Models;
using Sanakan.Services.Session;
using Sanakan.Services.SlotMachine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Z.EntityFramework.Plus;
using Sanakan.Services.Time;

namespace Sanakan.Modules
{
    [Name("Zabawy"), RequireUserRole]
    public class Fun : SanakanModuleBase<SocketCommandContext>
    {
        private Services.Fun _fun;
        private ISystemTime _time;
        private Moderator _moderation;
        private SessionManager _session;
        private Services.PocketWaifu.Spawn _spawn;

        public Fun(Services.Fun fun, Moderator moderation, SessionManager session,
            Services.PocketWaifu.Spawn spawn, ISystemTime time)
        {
            _fun = fun;
            _time = time;
            _spawn = spawn;
            _session = session;
            _moderation = moderation;
        }

        [Command("drobne")]
        [Alias("daily")]
        [Summary("dodaje dzienną dawkę drobniaków do twojego portfela")]
        [Remarks(""), RequireCommandChannel]
        public async Task GiveDailyScAsync()
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var botuser = await db.GetUserOrCreateSimpleAsync(Context.User.Id);
                var daily = botuser.TimeStatuses.FirstOrDefault(x => x.Type == Database.Models.StatusType.Daily);
                if (daily == null)
                {
                    daily = Database.Models.StatusType.Daily.NewTimeStatus();
                    botuser.TimeStatuses.Add(daily);
                }

                if (daily.IsActive(_time.Now()))
                {
                    await SafeReplyAsync("", embed: $"{Context.User.Mention} następne drobne możesz otrzymać dopiero {daily.EndsAt.ToRemTime()}!".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var mission = botuser.TimeStatuses.FirstOrDefault(x => x.Type == Database.Models.StatusType.WDaily);
                if (mission == null)
                {
                    mission = Database.Models.StatusType.WDaily.NewTimeStatus();
                    botuser.TimeStatuses.Add(mission);
                }
                mission.Count(_time.Now());

                daily.EndsAt = _time.Now().AddHours(20);
                botuser.ScCnt += 100;

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"user-{botuser.Id}" });

                await SafeReplyAsync("", embed: $"{Context.User.Mention} łap drobne na waciki!".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("daleko jeszcze?", RunMode = RunMode.Async)]
        [Alias("ilejeszczemuszespamicbydostactenzasranypakiet", "ijmsbdtzp", "how much to next packet")]
        [Summary("wyświetla ile pozostało znaków do otrzymania pakietu")]
        [Remarks("Karna"), RequireAnyCommandChannelOrLevel(60), DelayNextUseBy(120, DelayNextUseBy.ResType.Nothing)]
        public async Task ShowHowMuchToPacketAsync([Summary("nazwa użytkownika")]SocketUser user = null)
        {
            var usr = user ?? Context.User;
            if (usr == null) return;

            using (var db = new Database.DatabaseContext(Config))
            {
                long howMuch = Services.Fun.TakeATry(50d) ? Services.Fun.GetRandomValue(1, 5000) : _spawn.HowMuchToPacket(usr.Id);
                await SafeReplyAsync("", embed: $"{usr.Mention} potrzebuje **{howMuch}** znaków do następnego pakietu*(teoretycznie)*."
                    .ToEmbedMessage(EMType.Info).Build());
            }
        }

        [Command("chce muta", RunMode = RunMode.Async)]
        [Alias("mute me", "chce mute")]
        [Summary("odbierasz darmowego muta od bota - na serio i nawet nie proś o odmutowanie")]
        [Remarks("")]
        public async Task GiveMuteAsync()
        {
            var user = Context.User as SocketGuildUser;
            if (user == null) return;

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

                var session = new AcceptSession(user, null, Context.Client.CurrentUser);
                await _session.KillSessionIfExistAsync(session);

                var msg = await SafeReplyAsync("", embed: $"{user.Mention} na pewno chcesz muta?".ToEmbedMessage(EMType.Error).Build());
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
            }
        }

        [Command("zaskórniaki")]
        [Alias("hourly", "zaskorniaki")]
        [Summary("upadłeś tak nisko, że prosisz o SC pod marketem")]
        [Remarks(""), RequireCommandChannel]
        public async Task GiveHourlyScAsync()
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var botuser = await db.GetUserOrCreateSimpleAsync(Context.User.Id);
                var hourly = botuser.TimeStatuses.FirstOrDefault(x => x.Type == Database.Models.StatusType.Hourly);
                if (hourly == null)
                {
                    hourly = Database.Models.StatusType.Hourly.NewTimeStatus();
                    botuser.TimeStatuses.Add(hourly);
                }

                if (hourly.IsActive(_time.Now()))
                {
                    await SafeReplyAsync("", embed: $"{Context.User.Mention} następne zaskórniaki możesz otrzymać dopiero {hourly.EndsAt.ToRemTime()}!".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                hourly.EndsAt = _time.Now().AddHours(1);
                botuser.ScCnt += 5;

                var mission = botuser.TimeStatuses.FirstOrDefault(x => x.Type == Database.Models.StatusType.DHourly);
                if (mission == null)
                {
                    mission = Database.Models.StatusType.DHourly.NewTimeStatus();
                    botuser.TimeStatuses.Add(mission);
                }
                mission.Count(_time.Now());

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"user-{botuser.Id}", "users" });

                await SafeReplyAsync("", embed: $"{Context.User.Mention} łap piątaka!".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("wylosuj", RunMode = RunMode.Async)]
        [Alias("ofm", "one from many")]
        [Summary("bot losuje jedną rzecz z podanych opcji")]
        [Remarks("kokosek dzida wojtek"), RequireCommandChannel]
        public async Task GetOneFromManyAsync([Summary("opcje z których bot losuje")]params string[] options)
        {
            if (options.Count() < 2)
            {
                await SafeReplyAsync("", embed: "Podano zbyt mało opcji do wylosowania.".ToEmbedMessage(EMType.Error).Build());
                return;
            }

            var emote = Emote.Parse("<a:pinkarrow:826132578016559144>");
            var allOptions = options.Shuffle().ToList();

            await Task.Delay(Services.Fun.GetRandomValue(100, 500));

            await SafeReplyAsync("", embed: $"{emote} {Services.Fun.GetOneRandomFrom(allOptions)}".ToEmbedMessage(EMType.Success).WithAuthor(new EmbedAuthorBuilder().WithUser(Context.User)).Build());
        }

        [Command("rzut")]
        [Alias("beat", "toss")]
        [Summary("bot wykonuje rzut monetą, wygrywasz kwotę, o którą się założysz")]
        [Remarks("reszka 10"), RequireCommandChannel]
        public async Task TossCoinAsync([Summary("strona monety (orzeł/reszka)")]Services.CoinSide side, [Summary("ilość SC")]int amount)
        {
            if (amount <= 0)
            {
                await SafeReplyAsync("", embed: $"{Context.User.Mention} na minusie?!".ToEmbedMessage(EMType.Error).Build());
                return;
            }

            using (var db = new Database.DatabaseContext(Config))
            {
                var botuser = await db.GetUserOrCreateSimpleAsync(Context.User.Id);
                if (botuser.ScCnt < amount)
                {
                    await SafeReplyAsync("", embed: $"{Context.User.Mention} nie posiadasz wystarczającej liczby SC!".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                botuser.ScCnt -= amount;
                var thrown = _fun.RandomizeSide();
                var embed = $"{Context.User.Mention} pudło!\n\nObecnie posiadasz {botuser.ScCnt} SC.".ToEmbedMessage(EMType.Error);

                botuser.Stats.Tail += (thrown == CoinSide.Tail) ? 1 : 0;
                botuser.Stats.Head += (thrown == CoinSide.Head) ? 1 : 0;

                if (thrown == side)
                {
                    ++botuser.Stats.Hit;
                    botuser.ScCnt += amount * 2;
                    botuser.Stats.IncomeInSc += amount;
                    embed = $"{Context.User.Mention} trafiony zatopiony!\n\nObecnie posiadasz {botuser.ScCnt} SC.".ToEmbedMessage(EMType.Success);
                }
                else
                {
                    ++botuser.Stats.Misd;
                    botuser.Stats.ScLost += amount;
                    botuser.Stats.IncomeInSc -= amount;
                }

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"user-{botuser.Id}", "users" });

                embed.ImageUrl = $"https://sanakan.pl/i/coin{(int)thrown}.png";
                await SafeReplyAsync("", embed: embed.Build());
            }
        }

        [Command("ustaw automat")]
        [Alias("set slot")]
        [Summary("ustawia automat")]
        [Remarks("info"), RequireCommandChannel]
        public async Task SlotMachineSettingsAsync([Summary("typ nastaw (info - wyświetla informacje)")]SlotMachineSetting setting = SlotMachineSetting.Info, [Summary("wartość nastawy")]string value = "info")
        {
            if (setting == SlotMachineSetting.Info)
            {
                await SafeReplyAsync("", false, $"{_fun.GetSlotMachineInfo()}".ToEmbedMessage(EMType.Info).Build());
                return;
            }

            using (var db = new Database.DatabaseContext(Config))
            {
                var botuser = await db.GetUserOrCreateSimpleAsync(Context.User.Id);
                if (!botuser.ApplySlotMachineSetting(setting, value))
                {
                    await SafeReplyAsync("", embed: $"Podano niewłaściwą wartość parametru!".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"user-{botuser.Id}", "users" });
            }

            await SafeReplyAsync("", embed: $"{Context.User.Mention} zmienił nastawy automatu.".ToEmbedMessage(EMType.Success).Build());
        }

        [Command("automat")]
        [Alias("slot", "slot machine")]
        [Summary("grasz na jednorękim bandycie")]
        [Remarks("info"), RequireCommandChannel]
        public async Task PlayOnSlotMachineAsync([Summary("typ (info - wyświetla informacje)")]string type = "game")
        {
            if (type != "game")
            {
                await SafeReplyAsync("", false, $"{_fun.GetSlotMachineGameInfo()}".ToEmbedMessage(EMType.Info).Build());
                return;
            }

            using (var db = new Database.DatabaseContext(Config))
            {
                var botuser = await db.GetUserOrCreateSimpleAsync(Context.User.Id);
                var machine = new SlotMachine(botuser);

                var toPay = machine.ToPay();
                if (botuser.ScCnt < toPay)
                {
                    await SafeReplyAsync("", embed: $"{Context.User.Mention} brakuje Ci SC, aby za tyle zagrać.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }
                var win = machine.Play(new SlotWickedRandom());
                botuser.ScCnt += win - toPay;

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"user-{botuser.Id}", "users" });

                await SafeReplyAsync("", embed: $"{_fun.GetSlotMachineResult(machine.Draw(), Context.User, botuser, win)}".ToEmbedMessage(EMType.Bot).Build());
            }
        }

        [Command("podarujsc")]
        [Alias("donatesc")]
        [Summary("dajesz datek innemu graczowi w postaci SC obarczony 40% podatkiem")]
        [Remarks("Karna 2000"), RequireCommandChannel]
        public async Task GiveUserScAsync([Summary("nazwa użytkownika")]SocketGuildUser user, [Summary("liczba SC (min. 1000)")]uint value)
        {
            if (value < 1000)
            {
                await SafeReplyAsync("", embed: "Nie można podarować mniej niż 1000 SC.".ToEmbedMessage(EMType.Error).Build());
                return;
            }

            if (user.Id == Context.User.Id)
            {
                await SafeReplyAsync("", embed: "Coś tutaj nie gra.".ToEmbedMessage(EMType.Error).Build());
                return;
            }

            using (var db = new Database.DatabaseContext(Config))
            {
                if (!db.Users.Any(x => x.Id == user.Id))
                {
                    await SafeReplyAsync("", embed: "Ta osoba nie ma profilu bota.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var targetUser = await db.GetUserOrCreateSimpleAsync(user.Id);
                var thisUser = await db.GetUserOrCreateSimpleAsync(Context.User.Id);

                if (thisUser.ScCnt < value)
                {
                    await SafeReplyAsync("", embed: $"{Context.User.Mention} nie masz wystarczającej ilości SC.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                thisUser.ScCnt -= value;

                var newScCnt = (value * 60) / 100;
                targetUser.ScCnt += newScCnt;

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"user-{thisUser.Id}", "users", $"user-{targetUser.Id}" });

                await SafeReplyAsync("", embed: $"{Context.User.Mention} podarował {user.Mention} {newScCnt} SC".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("zagadka", RunMode = RunMode.Async)]
        [Alias("riddle")]
        [Summary("wypisuje losową zagadkę i podaje odpowiedź po 15 sekundach")]
        [Remarks(""), RequireCommandChannel]
        public async Task ShowRiddleAsync()
        {
            var riddles = new List<Question>();
            using (var db = new Database.DatabaseContext(Config))
            {
                riddles = await db.GetCachedAllQuestionsAsync();
            }

            riddles = riddles.Shuffle().ToList();
            var riddle = riddles.FirstOrDefault();

            riddle.RandomizeAnswers();
            var msg = await SafeReplyAsync(riddle.Get());
            await msg.AddReactionsAsync(riddle.GetEmotes());

            await Task.Delay(15000);

            int answers = 0;
            var react = await msg.GetReactionUsersAsync(riddle.GetRightEmote(), 100).FlattenAsync();
            foreach (var addR in riddle.GetEmotes())
            {
                var re = await msg.GetReactionUsersAsync(addR, 100).FlattenAsync();
                if (re.Any(x => x.Id == Context.User.Id)) answers++;
            }

            await msg.RemoveAllReactionsAsync();

            if (react.Any(x => x.Id == Context.User.Id) && answers < 2)
            {
                await SafeReplyAsync("", false, $"{Context.User.Mention} zgadłeś!".ToEmbedMessage(EMType.Success).Build());
            }
            else if (answers > 1)
            {
                await SafeReplyAsync("", false, $"{Context.User.Mention} wybrałeś więcej jak jedną odpowiedź!".ToEmbedMessage(EMType.Error).Build());
            }
            else await SafeReplyAsync("", false, $"{Context.User.Mention} pudło!".ToEmbedMessage(EMType.Error).Build());
        }
    }
}
