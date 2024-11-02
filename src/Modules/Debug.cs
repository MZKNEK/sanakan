#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Sanakan.Config;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Preconditions;
using Sanakan.Services;
using Sanakan.Services.Commands;
using Sanakan.Services.Executor;
using Sanakan.Services.PocketWaifu;
using Sanakan.Services.Time;
using Shinden;
using Z.EntityFramework.Plus;

namespace Sanakan.Modules
{
    [Name("Debug"), Group("dev"), DontAutoLoad]
    public class Debug : SanakanModuleBase<SocketCommandContext>
    {
        private static Dictionary<string, CancellationTokenSource>  _lotteries = new Dictionary<string, CancellationTokenSource>();

        private Waifu _waifu;
        private Spawn _spawn;
        private IConfig _config;
        private ISystemTime _time;
        private IExecutor _executor;
        private Services.Helper _helper;
        private ShindenClient _shClient;
        private Services.ImageProcessing _img;

        public Debug(Waifu waifu, ShindenClient shClient, Services.Helper helper, Services.ImageProcessing img,
            IConfig config, IExecutor executor, Spawn spawn, ISystemTime time)
        {
            _shClient = shClient;
            _executor = executor;
            _helper = helper;
            _config = config;
            _waifu = waifu;
            _spawn = spawn;
            _time = time;
            _img = img;
        }

        [Command("poke", RunMode = RunMode.Async), RequireDevOrTester]
        [Summary("generuje obrazek safari")]
        [Remarks("1")]
        public async Task GeneratePokeImageAsync([Summary("nr grafiki")]int index)
        {
            try
            {
                var reader = new JsonFileReader(Services.Dir.GetResource("Poke/List.json"));
                var images = reader.Load<List<SafariImage>>();

                var character = (await _shClient.GetCharacterInfoAsync(2)).Body;
                var channel = Context.Channel as ITextChannel;

                _ = await _waifu.GetSafariViewAsync(images[index], _waifu.GenerateNewCard(null, character), channel);
            }
            catch (Exception ex)
            {
                await ReplyAsync("", embed: $"Coś poszło nie tak: {ex.Message}".ToEmbedMessage(EMType.Error).Build());
            }
        }

        [Command("echances", RunMode = RunMode.Async), RequireDevOrTester]
        [Summary("wypisuje szanse na itemy z danej wyprawy")]
        [Remarks("h")]
        public async Task ShowItemChancesFromExpeditionAsync([Summary("typ wyprawy")]CardExpedition e)
        {
            var msg = new EmbedBuilder
            {
                Color = EMType.Bot.Color(),
                Title = $"Szanse z {e.GetName("ej")} wyprawy",
                Description = string.Join("\n", _waifu.GetItemChancesFromExpedition(e))
            };

            await ReplyAsync("", embed: msg.Build());
        }

        [Command("eechances", RunMode = RunMode.Async), RequireDevOrTester]
        [Summary("wypisuje szanse na wydarzenia")]
        [Remarks("h")]
        public async Task ShowEventChancesFromExpeditionAsync([Summary("typ wyprawy")]CardExpedition e)
        {
            var msg = new EmbedBuilder
            {
                Color = EMType.Bot.Color(),
                Title = $"Szanse z {e.GetName("ej")} wyprawy",
                Description = _waifu.GetEventChancesFromExpedition(e)
            };

            await ReplyAsync("", embed: msg.Build());
        }

        [Command("lchances", RunMode = RunMode.Async), RequireDevOrTester]
        [Summary("wypisuje szanse z loterii")]
        [Remarks("scalpel")]
        public async Task ShowItemChancesFromExpeditionAsync([Summary("kategoria nagród")]LotteryReward r = LotteryReward.None)
        {
            IEnumerable<(string, float)> chances = null;
            switch (r)
            {
                case LotteryReward.Quality:
                {
                    chances = Lottery.GetPartQualityChances().Select(x => (x.Item1.ToName(), x.Item2)).OrderByDescending(x => x.Item2);
                }
                break;

                case LotteryReward.FigurePart:
                {
                    chances = Lottery.GetPartChances().Select(x => (x.Item1.Name(), x.Item2)).OrderByDescending(x => x.Item2);
                }
                break;

                case LotteryReward.FigurePartNS:
                {
                    chances = Lottery.GetPartNSChances().Select(x => (x.Item1.Name(), x.Item2)).OrderByDescending(x => x.Item2);
                }
                break;

                case LotteryReward.WaifuFood:
                {
                    chances = Lottery.GetFoodChances().Select(x => (x.Item1.Name(), x.Item2)).OrderByDescending(x => x.Item2);
                }
                break;

                case LotteryReward.RandomPill:
                {
                    chances = Lottery.GetPillsChances().Select(x => (x.Item1.Name(), x.Item2)).OrderByDescending(x => x.Item2);
                }
                break;

                case LotteryReward.TC:
                case LotteryReward.CT:
                {
                    chances = Lottery.GetMoneyRewardChances().Select(x => (x.Item1.ToString(), x.Item2)).OrderByDescending(x => x.Item2);
                }
                break;

                default:
                {
                    chances = Lottery.GetRewardChances().Select(x => (x.Item1.ToString(), x.Item2)).OrderByDescending(x => x.Item2);
                }
                break;
            }

            var msg = new EmbedBuilder
            {
                Color = EMType.Bot.Color(),
                Title = $"Szanse z {r}",
            };

            foreach (var item in chances)
                msg.Description += $"**{item.Item2:F}%** - {item.Item1}\n";

            await ReplyAsync("", embed: msg.Build());
        }

        [Command("missingu", RunMode = RunMode.Async), RequireDev]
        [Summary("generuje listę id użytkowników, których nie widzi bot na serwerach")]
        [Remarks("")]
        public async Task GenerateMissingUsersListAsync()
        {
            var allUsers = Context.Client.Guilds.SelectMany(x => x.Users).Distinct();
            using (var db = new Database.DatabaseContext(Config))
            {
                var nonExistingIds = db.Users.AsQueryable().AsSplitQuery().Where(x => !allUsers.Any(u => u.Id == x.Id)).Select(x => x.Id).ToList();
                await ReplyAsync("", embed: string.Join("\n", nonExistingIds).ToEmbedMessage(EMType.Bot).Build());
            }
        }

        [Command("show blacklist", RunMode = RunMode.Async), RequireDevOrTester]
        [Summary("wypisuje listę użytkowników na czarnej liśćie")]
        [Remarks("")]
        public async Task ShowBlacklistedUsersAsync()
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var bUsers = await db.Users.AsQueryable().Where(x => x.IsBlacklisted).AsNoTracking().ToListAsync();
                await ReplyAsync("", embed: $"**Czarna lista:**\n\n{string.Join("\n", bUsers.Select(x => (Context.Client.GetUserAsync(x.Id).GetAwaiter().GetResult()?.Username ?? "????") + $" {x.Id}"))}".ToEmbedMessage(EMType.Info).Build());
            }
        }

        [Command("blacklist"), RequireDev]
        [Summary("dodaje/usuwa użytkownika do/z czarnej listy")]
        [Remarks("Karna")]
        public async Task BlacklistUserAsync([Summary("nazwa użytkownika")]SocketGuildUser user)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var targetUser = await db.GetUserOrCreateSimpleAsync(user.Id);

                targetUser.IsBlacklisted = !targetUser.IsBlacklisted;
                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"user-{Context.User.Id}", "users" });

                await ReplyAsync("", embed: $"{user.Mention} - blacklist: {targetUser.IsBlacklisted}".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("pool"), RequireDev]
        [Summary("zmienia użytkownikowi źródło postaci")]
        [Remarks("Karna 1")]
        public async Task SetUserPoolAsync([Summary("nazwa użytkownika")]SocketGuildUser user, [Summary("żródło")]CharacterPoolType poolType)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var targetUser = await db.GetUserOrCreateSimpleAsync(user.Id);

                targetUser.PoolType = poolType;
                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"user-{Context.User.Id}", "users" });

                await ReplyAsync("", embed: $"{user.Mention}: {targetUser.PoolType}".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("rmsg", RunMode = RunMode.Async), RequireDev]
        [Summary("wysyła wiadomość na kanał na danym serwerze jako odpowiedź do podanej innej wiadomości")]
        [Remarks("15188451644 101155483 1231231 Nie masz racji!")]
        public async Task SendResponseMsgToChannelInGuildAsync([Summary("id serwera")]ulong gId, [Summary("id kanału")]ulong chId, [Summary("id wiadomości")]ulong msgId, [Summary("treść wiadomości")][Remainder]string msg)
        {
            try
            {
                var msg2r = await Context.Client.GetGuild(gId).GetTextChannel(chId).GetMessageAsync(msgId);
                if (msg2r is IUserMessage umsg) await umsg.ReplyAsync(msg);
            }
            catch (Exception ex)
            {
                await ReplyAsync(ex.Message);
            }
        }

        [Command("smsg", RunMode = RunMode.Async), RequireDev]
        [Summary("wysyła wiadomość na kanał na danym serwerze")]
        [Remarks("15188451644 101155483 elo ziomki")]
        public async Task SendMsgToChannelInGuildAsync([Summary("id serwera")]ulong gId, [Summary("id kanału")]ulong chId, [Summary("treść wiadomości")][Remainder]string msg)
        {
            try
            {
                await Context.Client.GetGuild(gId).GetTextChannel(chId).SendMessageAsync(msg);
            }
            catch (Exception ex)
            {
                await ReplyAsync(ex.Message);
            }
        }

        [Command("semsg", RunMode = RunMode.Async), RequireDev]
        [Summary("wysyła wiadomość w formie embed na kanał na danym serwerze")]
        [Remarks("15188451644 101155483 bot elo ziomki")]
        public async Task SendEmbedMsgToChannelInGuildAsync([Summary("id serwera")]ulong gId, [Summary("id kanału")]ulong chId, [Summary("typ wiadomości (Neutral/Warning/Success/Error/Info/Bot)")]EMType type, [Summary("treść wiadomości")][Remainder]string msg)
        {
            try
            {
                await Context.Client.GetGuild(gId).GetTextChannel(chId).SendMessageAsync("", embed: msg.ToEmbedMessage(type).Build());
            }
            catch (Exception ex)
            {
                await ReplyAsync(ex.Message);
            }
        }

        [Command("r2msg", RunMode = RunMode.Async), RequireDev]
        [Summary("dodaje reakcję do wiadomości")]
        [Remarks("15188451644 101155483 825724399512453140 <:Redpill:455880209711759400>")]
        public async Task AddReactionToMsgOnChannelInGuildAsync([Summary("id serwera")]ulong gId, [Summary("id kanału")]ulong chId, [Summary("id wiadomości")]ulong msgId, [Summary("reakcja")]string reaction)
        {
            try
            {
                var msg = await Context.Client.GetGuild(gId).GetTextChannel(chId).GetMessageAsync(msgId);
                if (Emote.TryParse(reaction, out var emote))
                {
                    await msg.AddReactionAsync(emote);
                }
                else
                {
                    await msg.AddReactionAsync(new Emoji(reaction));
                }
            }
            catch (Exception ex)
            {
                await ReplyAsync(ex.Message);
            }
        }

        [Command("rcu")]
        [Summary("podmienia custom url na kartach"), RequireDev]
        [Remarks("Sniku https://i.imgur.com/ https://sanakan.pl/i/ss/")]
        public async Task UpdateCardsCustomImageUrlAsync([Summary("nazwa użytkownika")]SocketGuildUser user, [Summary("stary url")]string oldUrl, [Summary("nowy url")]string newUrl)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var cards = db.Cards.AsQueryable().AsSplitQuery().Where(x => x.GameDeckId == user.Id && x.CustomImage != null && x.CustomImage.StartsWith(oldUrl)).ToList();
                if (cards.Count < 1)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie odnaleziono kart.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                foreach (var card in cards)
                {
                    card.CustomImage = card.CustomImage.Replace(oldUrl, newUrl);
                }

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"users" });

                await ReplyAsync("", embed: $"Zaktualizowano {cards.Count} kart.".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("cup")]
        [Summary("wymusza update na kartach"), RequireDevOrTester]
        [Remarks("3123 121")]
        public async Task ForceUpdateCardsAsync([Summary("WIDs")]params ulong[] ids)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var cards = db.Cards.AsQueryable().AsSplitQuery().Where(x => ids.Any(c => c == x.Id)).ToList();
                if (cards.Count < 1)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie odnaleziono kart.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                foreach (var card in cards)
                {
                    try
                    {
                        await card.Update(null, _shClient);
                        _waifu.DeleteCardImageIfExist(card);
                    }
                    catch (Exception) { }
                }

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"users" });

                await ReplyAsync("", embed: $"Zaktualizowano {cards.Count} kart.".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("time", RunMode = RunMode.Async), RequireDevOrTester]
        [Summary("wyświetla czas serwera")]
        [Remarks("")]
        public async Task ShowTimeAsync() => await ReplyAsync("", embed: $"{DateTime.Now}".ToEmbedMessage(EMType.Info).Build());

        [Command("btime", RunMode = RunMode.Async), RequireDevOrTester]
        [Summary("wyświetla czas bota")]
        [Remarks("")]
        public async Task ShowBotTimeAsync() => await ReplyAsync("", embed: $"{_time.Now()}".ToEmbedMessage(EMType.Info).Build());

        [Command("rozdajm", RunMode = RunMode.Async), RequireDev]
        [Summary("rozdaje karty kilka razy")]
        [Remarks("1 10 5 10")]
        public async Task GiveawayCardsMultiAsync([Summary("id użytkownika")]ulong id, [Summary("liczba kart")]uint count, [Summary("czas w minutach")]uint duration = 5, [Summary("liczba powtórzeń")]uint repeat = 1)
        {
            var exe = new Executable("lotery-start", new Func<Task>(async () =>
            {
                using (var db = new Database.DatabaseContext(Config))
                {
                    await db.UserActivities.AddAsync(new Services.UserActivityBuilder(_time)
                        .WithType(Database.Models.ActivityType.LotteryStarted).Build());
                    await db.SaveChangesAsync();
                }
            }), 1, Priority.High);
            await _executor.TryAdd(exe, TimeSpan.FromSeconds(1));

            var source = new CancellationTokenSource();
            var lid = $"{Context.User.Id}{_time.Now()}-{repeat}".Replace(' ', 'x');
            _lotteries.Add(lid, source);

            for (uint i = 0; i < repeat; i++)
            {
                try
                {
                    await GiveawayCardsAsync(id, count, duration, i, repeat);
                    await Task.Delay(TimeSpan.FromSeconds(10));
                }
                catch (Exception) {}

                if (source.Token.IsCancellationRequested)
                {
                    source.Dispose();
                    await ReplyAsync("", embed: $"Loteria `{lid}` anulowana.".ToEmbedMessage(EMType.Bot).Build());
                    return;
                }
            }

            if (_lotteries.ContainsKey(lid))
            {
                _lotteries.Remove(lid);
                source.Dispose();
            }
        }

        [Command("rozdajmkill", RunMode = RunMode.Async), RequireDev]
        [Summary("wyłącza loterie")]
        [Remarks("1233633432534543")]
        public async Task ShowOrKillLotteryAsync([Summary("id loteri"), Remainder]string lid = "")
        {
            if (string.IsNullOrEmpty(lid))
            {
                await ReplyAsync("", embed: $"Aktywne loterie:\n\n{string.Join('\n', _lotteries.Select(x => x.Key))}".ToEmbedMessage(EMType.Bot).Build());
                return;
            }

            if (_lotteries.ContainsKey(lid))
            {
                _lotteries[lid].Cancel();
                _lotteries.Remove(lid);
                await ReplyAsync("", embed: "Rest in pepperoni.".ToEmbedMessage(EMType.Bot).Build());
                return;
            }

            await ReplyAsync("", embed: "??????????".ToEmbedMessage(EMType.Error).Build());
        }

        [Command("test lrt"), RequireDevOrTester]
        [Summary("odpala długo wykonującego się polecenie")]
        [Remarks("30")]
        public async Task StartLongRunningTaskAsync([Summary("czas działania w sekundach")]uint time = 30)
        {
            await Context.Message.AddReactionAsync(new Emoji("✋"));
            await Task.Delay(TimeSpan.FromSeconds(time));
            await Context.Message.AddReactionAsync(new Emoji("👌"));
        }

        [Command("test wlrt"), RequireDevOrTester]
        [Summary("odpala długo wykonującego się polecenie z dodatkowym userem")]
        [Remarks("Jeeda 30")]
        public async Task StartLongRunningTaskWithUserAsync([Summary("nazwa użytkownika")]SocketGuildUser user, [Summary("czas działania w sekundach")]uint time = 30)
        {
            _ = user;
            await Context.Message.AddReactionAsync(new Emoji("✋"));
            await Task.Delay(TimeSpan.FromSeconds(time));
            await Context.Message.AddReactionAsync(new Emoji("👌"));
        }

        [Command("test mlrt", RunMode = RunMode.Async), RequireDevOrTester]
        [Summary("odpala długo wykonującego się polecenie należące do kilku osób")]
        [Remarks("30 1 2 3")]
        public async Task StartMultiLongRunningTaskAsync([Summary("czas działania w sekundach")]uint time = 30,  [Summary("id")]params ulong[] ids)
        {
            var exe = new Executable("test mlrt", new Func<Task>(async () =>
            {
                await Context.Message.AddReactionAsync(new Emoji("✋"));
                await Task.Delay(TimeSpan.FromSeconds(time));
                await Context.Message.AddReactionAsync(new Emoji("👌"));
            }), 1);

            exe.AddOwner(Context.User.Id);

            if (ids.IsNullOrEmpty())
            {
                foreach(var id in ids)
                    exe.AddOwner(id);
            }

            await _executor.TryAdd(exe, TimeSpan.FromSeconds(1));
        }

        [Command("test glrt", RunMode = RunMode.Async), RequireDevOrTester]
        [Summary("odpala długo wykonującego się polecenie blokujące globalnie")]
        [Remarks("30")]
        public async Task StartMultiLongRunningGlobalTaskAsync([Summary("czas działania w sekundach")]uint time = 30)
        {
            var exe = new Executable("test glrt", new Func<Task>(async () =>
            {
                await Context.Message.AddReactionAsync(new Emoji("✋"));
                await Task.Delay(TimeSpan.FromSeconds(time));
                await Context.Message.AddReactionAsync(new Emoji("👌"));
            }), 0);

            await _executor.TryAdd(exe, TimeSpan.FromSeconds(1));
        }

        [Command("rozdaj", RunMode = RunMode.Async), RequireDev]
        [Summary("rozdaje karty")]
        [Remarks("1 10 5")]
        public async Task GiveawayCardsAsync([Summary("id użytkownika")]ulong id, [Summary("liczba kart")]uint count, [Summary("czas w minutach")]uint duration = 5, [Summary("które wywołanie?"), Hidden]long progress = -1, [Summary("ile wywołań?"), Hidden]uint howMuch = 0)
        {
            var emote = Emote.Parse("<a:success:467493778752798730>");
            var time = _time.Now().AddMinutes(duration);

            var mention = "";
            using (var db = new Database.DatabaseContext(_config))
            {
                var config = await db.GetCachedGuildFullConfigAsync(Context.Guild.Id);
                if (config != null)
                {
                    var wRole = Context.Guild.GetRole(config.WaifuRole);
                    if (wRole != null) mention = wRole.Mention;
                }
            }

            var sendMsg = $"Loteria kart. Zareaguj {emote}, aby wziąć udział.\n\nKoniec {time.ToRemTime()}".ToEmbedMessage(EMType.Bot);
            if (progress > -1) sendMsg.Footer = (new EmbedFooterBuilder()).WithText($"{progress+1}/{howMuch}");
            var msg = await ReplyAsync(mention, embed: sendMsg.Build());
            await msg.AddReactionAsync(emote);

            await Task.Delay(TimeSpan.FromMinutes(duration));
            await msg.RemoveReactionAsync(emote, Context.Client.CurrentUser);

            var reactions = await msg.GetReactionUsersAsync(emote, 300).FlattenAsync();
            var users = reactions.Shuffle().ToList();

            IUser winner = null;
            using (var db = new Database.DatabaseContext(_config))
            {
                var watch = Stopwatch.StartNew();
                while (winner == null)
                {
                    if (watch.ElapsedMilliseconds > 60000)
                    {
                        await msg.ModifyAsync(x => x.Embed = "Timeout!".ToEmbedMessage(EMType.Error).Build());
                        return;
                    }

                    if (users.Count < 1)
                    {
                        await msg.ModifyAsync(x => x.Embed = "Na loterie nie stawił się żaden użytkownik!".ToEmbedMessage(EMType.Error).Build());
                        return;
                    }

                    var selected = Services.Fun.GetOneRandomFrom(users);
                    if (!await db.IsUserMutedOnGuildAsync(selected.Id, Context.Guild.Id))
                    {
                        var dUser = await db.GetBaseUserAndDontTrackAsync(selected.Id);
                        if (dUser != null)
                        {
                            if (!dUser.IsBlacklisted)
                                winner = selected;
                        }
                    }
                    users.Remove(selected);
                }
            }

            var exe = new Executable("lotery", new Func<Task>(async () =>
            {
                using (var db = new Database.DatabaseContext(Config))
                {
                    var user = await db.GetUserOrCreateAsync(id);
                    if (user == null)
                    {
                        await msg.ModifyAsync(x => x.Embed = "Nie odnaleziono kart do rozdania!".ToEmbedMessage(EMType.Error).Build());
                        return;
                    }

                    var loteryCards = user.GameDeck.Cards.ToList();
                    if (loteryCards.Count < 1)
                    {
                        await msg.ModifyAsync(x => x.Embed = "Nie odnaleziono kart do rozdania!".ToEmbedMessage(EMType.Error).Build());
                        return;
                    }

                    var winnerUser = await db.GetUserOrCreateAsync(winner.Id);
                    if (winnerUser == null)
                    {
                        await msg.ModifyAsync(x => x.Embed = "Nie odnaleziono docelowego użytkownika!".ToEmbedMessage(EMType.Error).Build());
                        return;
                    }

                    bool wonSSS = false;
                    var cardsIds = new List<string>();
                    var idsToSelect = loteryCards.Select(x => x.Id).ToList();

                    for (int i = 0; i < count; i++)
                    {
                        if (idsToSelect.Count < 1)
                            break;

                        var wid = Services.Fun.GetOneRandomFrom(idsToSelect);
                        var thisCard = loteryCards.FirstOrDefault(x => x.Id == wid);
                        if (thisCard.Rarity == Rarity.SSS) wonSSS = true;

                        thisCard.Active = false;
                        thisCard.InCage = false;
                        thisCard.Tags.Clear();
                        thisCard.Expedition = CardExpedition.None;

                        thisCard.GameDeckId = winnerUser.GameDeck.Id;

                        bool isOnUserWishlist = await winnerUser.GameDeck.RemoveCharacterFromWishListAsync(thisCard.Character, db);
                        cardsIds.Add($"{thisCard.ToHeartWishlist(isOnUserWishlist)}{thisCard.GetString(false, false, true, hideStats: true)}");
                        winnerUser.GameDeck.RemoveCardFromWishList(thisCard.Id);

                        db.AddActivityFromNewCard(thisCard, isOnUserWishlist, _time, winnerUser, winner.GetUserNickInGuild());

                        idsToSelect.Remove(wid);
                    }

                    await db.UserActivities.AddAsync(new Services.UserActivityBuilder(_time)
                        .WithUser(winnerUser, winner).WithType(Database.Models.ActivityType.WonLottery).Build());

                    await db.SaveChangesAsync();
                    await msg.DeleteAsync();

                    QueryCacheManager.ExpireTag(new string[] { $"user-{Context.User.Id}", "users", $"user-{id}" });

                    var msgType = wonSSS ? EMType.Warning : EMType.Success;
                    var embToSend =  $"Loterie wygrywa {winner.Mention} i otrzymuje:\n\n{string.Join("\n", cardsIds.OrderBy(x => x))}".TrimToLength().ToEmbedMessage(msgType);
                    if (progress > -1) embToSend.Footer = new EmbedFooterBuilder().WithText($"{progress+1}/{howMuch}");
                    msg = await ReplyAsync(embed: embToSend.Build());

                    try
                    {
                        var privEmb = new EmbedBuilder()
                        {
                            Color = EMType.Info.Color(),
                            Description = $"Na [loterii]({msg.GetJumpUrl()}) zdobyłeś {cardsIds.Count} kart."
                        };

                        var pw = await winner.CreateDMChannelAsync();
                        if (pw != null) await pw.SendMessageAsync("", embed: privEmb.Build());
                    }
                    catch(Exception){}
                }
            }), winner.Id, Priority.High);

            await _executor.TryAdd(exe, TimeSpan.FromSeconds(1));
            await msg.RemoveAllReactionsAsync();
        }

        [Command("tranc"), Priority(1), RequireDev]
        [Summary("przenosi karty między użytkownikami")]
        [Remarks("Sniku 41231 41232")]
        public async Task TransferUserCardAsync([Summary("nazwa użytkownika")]SocketUser user, [Summary("WIDs")]params ulong[] wids) => await TransferCardAsync(user.Id, wids);

        [Command("tranc"), Priority(1), RequireDev]
        [Summary("przenosi karty między użytkownikami")]
        [Remarks("Sniku 41231 41232")]
        public async Task TransferCardAsync([Summary("id użytkownika")]ulong userId, [Summary("WIDs")]params ulong[] wids)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var deck = await db.GameDecks.Include(x => x.Wishes).FirstOrDefaultAsync(x => x.UserId == userId);
                if (deck == null)
                {
                    await ReplyAsync("", embed: "W bazie nie ma użytkownika o podanym id!".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var thisCards = db.Cards.AsQueryable().Include(x => x.Tags).AsSingleQuery().Where(x => wids.Contains(x.Id)).ToList();
                if (thisCards.Count < 1)
                {
                    await ReplyAsync("", embed: "Nie odnaleziono kart!".ToEmbedMessage(EMType.Bot).Build());
                    return;
                }

                string reply = $"Karta {thisCards.First().GetString(false, false, true)} została przeniesiona.";
                if (thisCards.Count > 1) reply = $"Przeniesiono {thisCards.Count} kart.";

                foreach (var thisCard in thisCards)
                {
                    thisCard.Active = false;
                    thisCard.InCage = false;
                    thisCard.Tags.Clear();
                    thisCard.GameDeckId = userId;
                    thisCard.Expedition = CardExpedition.None;

                    deck.RemoveCardFromWishList(thisCard.Id);
                    await deck.RemoveCharacterFromWishListAsync(thisCard.Character, db);
                }

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"user-{userId}", "users" });

                await ReplyAsync("", embed: reply.ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("rmcards"), Priority(1), RequireDev]
        [Summary("kasuje podane karty")]
        [Remarks("41231 41232")]
        public async Task RemoveCardsAsync([Summary("WIDs")]params ulong[] wids)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var thisCards = db.Cards.AsQueryable().Include(x => x.Tags).AsSingleQuery().Where(x => wids.Contains(x.Id)).ToList();
                if (thisCards.Count < 1)
                {
                    await ReplyAsync("", embed: "Nie odnaleziono kart!".ToEmbedMessage(EMType.Bot).Build());
                    return;
                }

                string reply = $"Karta {thisCards.First().GetString(false, false, true)} została skasowana.";
                if (thisCards.Count > 1) reply = $"Skasowano {thisCards.Count} kart.";

                foreach (var thisCard in thisCards)
                {
                    _waifu.DeleteCardImageIfExist(thisCard);
                    db.Cards.Remove(thisCard);
                }

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { "users" });

                await ReplyAsync("", embed: reply.ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("prst"), RequireDev]
        [Summary("resetuje profil użytkownika")]
        [Remarks("Karna")]
        public async Task ResetUserProfileAsync([Summary("nazwa użytkownika")]SocketGuildUser user)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var bUser = await db.GetUserOrCreateSimpleAsync(user.Id);

                bUser.BackgroundProfileUri = Dir.GetResource("np/pbg.png");
                bUser.StatsStyleSettings = ProfileSettings.Default;
                bUser.PremiumCustomProfileOverlayUrl = null;
                bUser.StatsReplacementProfileUri = null;
                bUser.AvatarBorder = AvatarBorder.Base;
                bUser.ProfileType = ProfileType.Stats;
                bUser.CustomProfileOverlayUrl = null;
                bUser.ProfileShadowsOpacity = 0.7f;

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"user-{Context.User.Id}", "users" });

                await ReplyAsync("", embed: $"Zresetowano profil użytkownika: {user.Mention}".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("level"), RequireDev]
        [Summary("ustawia podany poziom użytkownikowi")]
        [Remarks("Karna 1")]
        public async Task ChangeUserLevelAsync([Summary("nazwa użytkownika")]SocketGuildUser user, [Summary("poziom")]long level)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var bUser = await db.GetUserOrCreateSimpleAsync(user.Id);

                bUser.Level = level;
                bUser.ExpCnt = Services.ExperienceManager.CalculateExpForLevel(level);

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"user-{Context.User.Id}", "users" });

                await ReplyAsync("", embed: $"{user.Mention} ustawiono {level} poziom.".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("mkick", RunMode = RunMode.Async), RequireDev]
        [Summary("wyrzuca użytkowników z serwera")]
        [Remarks("Jupson Moe")]
        public async Task MultiKickAsync([Summary("nazwy użytkowników")]params SocketGuildUser[] users)
        {
            var count = 0;

            foreach (var user in users)
            {
                try
                {
                    await user.KickAsync("Multi kick - łamanie regulaminu");
                    ++count;
                }
                catch (Exception) {}
            }

            await ReplyAsync("", embed: $"Wyrzucono {count} użytkowników.".ToEmbedMessage(EMType.Success).Build());
        }

        [Command("mban", RunMode = RunMode.Async), RequireDev]
        [Summary("banuje użytkowników z serwera")]
        [Remarks("Jupson Moe")]
        public async Task MultiBankAsync([Summary("nazwy użytkowników")]params SocketGuildUser[] users)
        {
            var count = 0;

            foreach (var user in users)
            {
                try
                {
                    await Context.Guild.AddBanAsync(user, 0, "Multi ban - łamanie regulaminu");
                    ++count;
                }
                catch (Exception) {}
            }

            await ReplyAsync("", embed: $"Zbanowano {count} użytkowników.".ToEmbedMessage(EMType.Success).Build());
        }

        [Command("restore"), Priority(1), RequireDev]
        [Summary("przenosi karty na nowo do użytkownika")]
        [Remarks("Sniku")]
        public async Task RestoreCardsAsync([Summary("użytkownik")]SocketGuildUser user)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var bUser = await db.GetUserOrCreateAsync(user.Id);
                var thisCards = db.Cards.Include(x => x.Tags).Where(x => (x.LastIdOwner == user.Id || (x.FirstIdOwner == user.Id && x.LastIdOwner == 0)) && x.GameDeckId == 1).ToList();
                if (thisCards.Count < 1)
                {
                    await ReplyAsync("", embed: "Nie odnaleziono kart!".ToEmbedMessage(EMType.Bot).Build());
                    return;
                }

                string reply = $"Karta {thisCards.First().GetString(false, false, true)} została przeniesiona.";
                if (thisCards.Count > 1) reply = $"Przeniesiono {thisCards.Count} kart.";

                foreach (var thisCard in thisCards)
                {
                    thisCard.Active = false;
                    thisCard.InCage = false;
                    thisCard.Tags.Clear();
                    thisCard.GameDeckId = user.Id;
                }

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"user-{Context.User.Id}", "users" });

                await ReplyAsync("", embed: reply.ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("missingc", RunMode = RunMode.Async), RequireDev]
        [Summary("generuje listę id kart, których właścicieli nie widzi bot na serwerach")]
        [Remarks("true")]
        public async Task GenerateMissingUsersCardListAsync([Summary("czy wypisać id'ki?")]bool ids = false)
        {
            var allUsers = Context.Client.Guilds.SelectMany(x => x.Users).Distinct();
            using (var db = new Database.DatabaseContext(Config))
            {
                var nonExistingIds = db.Cards.AsQueryable().AsSplitQuery().Where(x => !allUsers.Any(u => u.Id == x.GameDeckId)).Select(x => x.Id).ToList();
                await ReplyAsync("", embed: $"Kart: {nonExistingIds.Count}".ToEmbedMessage(EMType.Bot).Build());

                if (ids)
                    await ReplyAsync("", embed: string.Join("\n", nonExistingIds).ToEmbedMessage(EMType.Bot).Build());
            }
        }

        [Command("cstats", RunMode = RunMode.Async), RequireDev]
        [Summary("generuje statystyki kart począwszy od podanej karty")]
        [Remarks("1")]
        public async Task GenerateCardStatsAsync([Summary("WID")]ulong wid)
        {
            var stats = new long[(int)Rarity.E + 1];
            using (var db = new Database.DatabaseContext(Config))
            {
                foreach (var rarity in (Rarity[])Enum.GetValues(typeof(Rarity)))
                    stats[(int)rarity] = db.Cards.Count(x => x.Rarity == rarity && x.Id >= wid);

                string info = "";
                for (int i = 0; i < stats.Length; i++)
                    info += $"{(Rarity)i}: `{stats[i]}`\n";

                await ReplyAsync("", embed: info.ToEmbedMessage(EMType.Bot).Build());
            }
        }

        [Command("dusrcards"), Priority(1), RequireDev]
        [Summary("usuwa karty użytkownika o podanym id z bazy")]
        [Remarks("845155646123")]
        public async Task RemoveCardsUserAsync([Summary("id użytkownika")]ulong id)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var user = await db.GetUserOrCreateAsync(id);
                user.GameDeck.Cards.Clear();

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { "users", $"user-{id}" });
            }

            await ReplyAsync("", embed: $"Karty użytkownika o id: `{id}` zostały skasowane.".ToEmbedMessage(EMType.Success).Build());
        }

        [Command("duser"), Priority(1), RequireDev]
        [Summary("usuwa użytkownika o podanym id z bazy")]
        [Remarks("845155646123")]
        public async Task FactoryUserAsync([Summary("id użytkownika")]ulong id, [Summary("czy usunąć karty?")]bool cards = false)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var fakeu = await db.GetUserOrCreateAsync(1);
                var user = await db.GetUserOrCreateAsync(id);

                if (!cards)
                {
                    foreach (var card in user.GameDeck.Cards)
                    {
                        card.InCage = false;
                        card.Tags.Clear();
                        card.LastIdOwner = id;
                        card.GameDeckId = fakeu.GameDeck.Id;
                    }
                }

                foreach (var w in user.GameDeck.Wishes.Where(x => x.Type == Database.Models.WishlistObjectType.Character))
                {
                    await db.WishlistCountData.CreateOrChangeWishlistCountByAsync(w.ObjectId, w.ObjectName, -1);
                }

                db.Users.Remove(user);
                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { "users", $"user-{id}" });
            }

            await ReplyAsync("", embed: $"Użytkownik o id: `{id}` został wymazany.".ToEmbedMessage(EMType.Success).Build());
        }

        [Command("utitle"), Priority(1), RequireDev]
        [Summary("aktualizuje tytuł karty")]
        [Remarks("ssało")]
        public async Task ChangeTitleCardAsync([Summary("WID")]ulong wid, [Summary("tytuł")][Remainder]string title = null)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var thisCard = db.Cards.FirstOrDefault(x => x.Id == wid);
                if (thisCard == null)
                {
                    await ReplyAsync("", embed: $"Taka karta nie istnieje.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (title != null)
                {
                    thisCard.Title = title;
                }
                else
                {
                    var res = await _shClient.GetCharacterInfoAsync(thisCard.Character);
                    if (res.IsSuccessStatusCode())
                    {
                        thisCard.Title = res.Body?.Relations?.OrderBy(x => x.Id)?.FirstOrDefault()?.Title ?? "????";
                    }
                }

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { "users" });

                await ReplyAsync("", embed: $"Nowy tytuł to: `{thisCard.Title}`".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("delq"), Priority(1), RequireDev]
        [Summary("kasuje zagadkę o podanym id")]
        [Remarks("20")]
        public async Task RemoveQuizAsync([Summary("id zagadki")]ulong id)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var question = db.Questions.FirstOrDefault(x => x.Id == id);
                if (question == null)
                {
                    await ReplyAsync("", embed: $"Zagadka o ID: `{id}` nie istnieje!".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                db.Questions.Remove(question);
                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"quiz" });
                await ReplyAsync("", embed: $"Zagadka o ID: `{id}` została skasowana!".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("addq"), Priority(1), RequireDev]
        [Summary("dodaje nową zagadkę")]
        [Remarks("{...}")]
        public async Task AddNewQuizAsync([Summary("zagadka w formie jsona")][Remainder]string json)
        {
            try
            {
                var question = JsonConvert.DeserializeObject<Question>(json);
                using (var db = new Database.DatabaseContext(Config))
                {
                    db.Questions.Add(question);
                    await db.SaveChangesAsync();

                    QueryCacheManager.ExpireTag(new string[] { $"quiz" });
                    await ReplyAsync("", embed: $"Nowa zagadka dodana, jej ID to: `{question.Id}`".ToEmbedMessage(EMType.Success).Build());
                }
            }
            catch (Exception)
            {
                await ReplyAsync("", embed: $"Coś poszło nie tak przy parsowaniu jsona!".ToEmbedMessage(EMType.Error).Build());
            }
        }

        [Command("shuri"), Priority(1), RequireDev]
        [Summary("ustawia bazowe uri do api shindena")]
        [Remarks("")]
        public async Task SetBaseShindenUriAsync([Summary("uri do api")][Remainder] string uri)
        {
            var config = Config.Get();
            config.Shinden.BaseUri = uri;
            Config.Save();

            await ReplyAsync("", embed: $"Ustawiono URI na `{uri}`".ToEmbedMessage(EMType.Success).Build());
        }

        [Command("ppd"), Priority(1), RequireDev]
        [Summary("ustawia liczbę pakietów na dzień")]
        [Remarks("true")]
        public async Task SetPacksPerDayAsync([Summary("liczba pakietów")]long count, [Summary("true/false - czy zapisać?")]bool save = false)
        {
            var config = Config.Get();
            config.PacksPerDay = count;
            if (save) Config.Save();

            await ReplyAsync("", embed: $"Ustawiono liczbę pakietów `{count}`. `Zapisano: {save.GetYesNo()}`".ToEmbedMessage(EMType.Success).Build());
        }

        [Command("chpp"), Priority(1), RequireDev]
        [Summary("ustawia liczbę znaków na pakiet")]
        [Remarks("true")]
        public async Task SetCharCntPerPacketAsync([Summary("liczba znaków")]long count, [Summary("true/false - czy zapisać?")]bool save = false)
        {
            var config = Config.Get();
            config.CharPerPacket = count;
            if (save) Config.Save();

            await ReplyAsync("", embed: $"Ustawiono próg `{count}` znaków na pakiet. `Zapisano: {save.GetYesNo()}`".ToEmbedMessage(EMType.Success).Build());
        }

        [Command("chpe"), Priority(1), RequireDev]
        [Summary("ustawia liczbę znaków na punkt doświadczenia")]
        [Remarks("true")]
        public async Task SetCharCntPerExpAsync([Summary("liczba znaków")]long count, [Summary("true/false - czy zapisać?")]bool save = false)
        {
            var config = Config.Get();
            config.Exp.CharPerPoint = count;
            if (save) Config.Save();

            await ReplyAsync("", embed: $"Ustawiono próg `{count}` znaków na punkt doświadczenia. `Zapisano: {save.GetYesNo()}`".ToEmbedMessage(EMType.Success).Build());
        }

        [Command("turlban"), Priority(1), RequireDev]
        [Summary("wyłącza/włącza banowanie za spam url")]
        [Remarks("")]
        public async Task ToggleSafariAsync()
        {
            var config = Config.Get();
            config.GiveBanForUrlSpam = !config.GiveBanForUrlSpam;
            Config.Save();

            await ReplyAsync("", embed: $"Banowanko: `{config.GiveBanForUrlSpam.GetYesNo()}`".ToEmbedMessage(EMType.Success).Build());
        }

        [Command("tsafari"), Priority(1), RequireDev]
        [Summary("wyłącza/włącza safari")]
        [Remarks("true")]
        public async Task ToggleSafariAsync([Summary("true/false - czy zapisać?")]bool save = false)
        {
            var config = Config.Get();
            config.SafariEnabled = !config.SafariEnabled;
            if (save) Config.Save();

            await ReplyAsync("", embed: $"Safari: `{config.SafariEnabled.GetYesNo()}` `Zapisano: {save.GetYesNo()}`".ToEmbedMessage(EMType.Success).Build());
        }

        [Command("tautoclean"), Priority(1), RequireDev]
        [Summary("wyłącza/włącza czyszczenie obrazków")]
        [Remarks("")]
        public async Task ToggleCardsCleanAsync()
        {
            var config = Config.Get();
            config.AutoCleanCards = !config.AutoCleanCards;
            Config.Save();

            await ReplyAsync("", embed: $"AutoClean: `{config.AutoCleanCards.GetYesNo()}`".ToEmbedMessage(EMType.Success).Build());
        }

        [Command("force clean", RunMode = RunMode.Async), Priority(1), RequireDev]
        [Summary("wymusza czyszczenie obrazków kart")]
        [Remarks("30")]
        public async Task ForceCardsCleanAsync([Summary("jak stare obrazki wyczyścić (dni)")]uint dayCount)
        {
            await ReplyAsync("", embed: $"Rozpoczęto czyszczenie!".ToEmbedMessage(EMType.Success).Build());
            var raport = _waifu.CleanCards((int)dayCount);

            using var stream = new MemoryStream();
            using var writer = new StreamWriter(stream, leaveOpen: true);
            writer.Write(string.Join("\n", raport));
            writer.Flush();
            stream.Position = 0;

            await Context.Channel.SendFileAsync(stream, "tldr.txt");
        }

        [Command("lvlbadge", RunMode = RunMode.Async), RequireDevOrTester]
        [Summary("generuje przykładowy obrazek otrzymania poziomu")]
        [Remarks("")]
        public async Task GenerateLevelUpBadgeAsync([Summary("użytkownik(opcjonalne)")]SocketGuildUser user = null)
        {
            var usr = user ?? Context.User as SocketGuildUser;
            if (usr == null) return;

            using (var badge = await _img.GetLevelUpBadgeAsync("Very very long nickname of trolly user",
                2154, usr.GetUserOrDefaultAvatarUrl(), usr.Roles.OrderByDescending(x => x.Position).First().Color))
            {
                using (var badgeStream = badge.ToWebpStream())
                {
                    await Context.Channel.SendFileAsync(badgeStream, $"{usr.Id}.webp");
                }
            }
        }

        [Command("devr", RunMode = RunMode.Async), RequireDev]
        [Summary("przyznaje lub odbiera role developera")]
        [Remarks("")]
        public async Task ToggleDeveloperRoleAsync()
        {
            var user = Context.User as SocketGuildUser;
            if (user == null) return;

            var devr = Context.Guild.Roles.FirstOrDefault(x => x.Name == "Developer");
            if (devr == null) return;

            if (user.Roles.Contains(devr))
            {
                await user.RemoveRoleAsync(devr);
                await ReplyAsync("", embed: $"{user.Mention} stracił role deva.".ToEmbedMessage(EMType.Success).Build());
            }
            else
            {
                await user.AddRoleAsync(devr);
                await ReplyAsync("", embed: $"{user.Mention} otrzymał role deva.".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("gitem"), Priority(1), RequireDev]
        [Summary("generuje przedmiot i daje go użytkownikowi")]
        [Remarks("Sniku 2 1")]
        public async Task GenerateItemAsync([Summary("nazwa użytkownika")]SocketGuildUser user, [Summary("przedmiot")]ItemType itemType, [Summary("liczba przedmiotów")]uint count = 1,
            [Summary("jakość przedmiotu")]Quality quality = Quality.Broken)
        {
            var item = itemType.ToItem(count, quality);
            using (var db = new Database.DatabaseContext(Config))
            {
                var botuser = await db.GetUserOrCreateAsync(user.Id);
                botuser.GameDeck.AddItem(item);

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"user-{botuser.Id}", "users" });

                string cnt = (count > 1) ? $" x{count}" : "";
                await ReplyAsync("", embed: $"{user.Mention} otrzymał _{item.Name}_{cnt}.".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("ritem"), Priority(1), RequireDev]
        [Summary("odejmuje od użytkownika x sztuk przedmiotu")]
        [Remarks("Sniku 2 1")]
        public async Task RemoveItemAsync([Summary("nazwa użytkownika")]SocketGuildUser user, [Summary("przedmiot")]ItemType itemType, [Summary("liczba przedmiotów")]uint count = 1,
            [Summary("jakość przedmiotu")]Quality quality = Quality.Broken)
        {
            var item = itemType.ToItem(count, quality);
            using (var db = new Database.DatabaseContext(Config))
            {
                var botuser = await db.GetUserOrCreateAsync(user.Id);
                if (botuser.GameDeck.RemoveItem(item, 1, false))
                {
                    await db.SaveChangesAsync();

                    string cnt = (count > 1) ? $" x{count}" : "";
                    await ReplyAsync("", embed: $"{user.Mention} stracił _{item.Name}_{cnt}.".ToEmbedMessage(EMType.Success).Build());

                    QueryCacheManager.ExpireTag(new string[] { $"user-{botuser.Id}", "users" });
                    return;
                }
                await ReplyAsync("", embed: $"{user.Mention} nie posiada _{item.Name}_.".ToEmbedMessage(EMType.Error).Build());
            }
        }

        [Command("gcard"), Priority(1), RequireDev]
        [Summary("generuje kartę i daje ją użytkownikowi")]
        [Remarks("Sniku 54861")]
        public async Task GenerateCardAsync([Summary("nazwa użytkownika")]SocketGuildUser user, [Summary("id postaci na shinden (nie podanie - losowo)")]ulong id = 0,
            [Summary("ranga karty (nie podanie - losowo)")]Rarity rarity = Rarity.E, [Summary("jakość karty (nadpisuje range)")]Quality quality = Quality.Broken)
        {
            var character = (id == 0) ? await _waifu.GetRandomCharacterAsync(CharacterPoolType.All) : (await _shClient.GetCharacterInfoAsync(id)).Body;
            var card = (rarity == Rarity.E && quality == Quality.Broken) ? _waifu.GenerateNewCard(user, character) : _waifu.GenerateNewCard(user, character, rarity, quality);

            card.Source = CardSource.GodIntervention;
            using (var db = new Database.DatabaseContext(Config))
            {
                var botuser = await db.GetUserOrCreateAsync(user.Id);
                botuser.GameDeck.Cards.Add(card);

                await botuser.GameDeck.RemoveCharacterFromWishListAsync(card.Character, db);

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"user-{botuser.Id}", "users" });

                await ReplyAsync("", embed: $"{user.Mention} otrzymał {card.GetString(false, false, true)}.".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("gfcard"), Priority(1), RequireDev]
        [Summary("generuje kartę nieistneijącej postaci i daje ją użytkownikowi")]
        [Remarks("Sniku")]
        public async Task GenerateFakeCardAsync([Summary("nazwa użytkownika")]SocketGuildUser user, [Summary("nazwa")]string name, [Summary("tytuł")]string title)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var botuser = await db.GetUserOrCreateAsync(user.Id);

                var card = _waifu.GenerateNewCard(user, name, title, 50453);
                card.Source = CardSource.GodIntervention;
                card.Unique = true;

                botuser.GameDeck.Cards.Add(card);

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"user-{botuser.Id}", "users" });

                await ReplyAsync("", embed: $"{user.Mention} otrzymał {card.GetString(false, false, true)}.".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("ctou"), Priority(1), RequireDev]
        [Summary("zamienia kartę na ultimate")]
        [Remarks("54861 Zeta 100 100 1000")]
        public async Task MakeUltimateCardAsync([Summary("WID")]ulong id, [Summary("jakość karty")]Quality quality,
            [Summary("dodatkowy atak")]int atk = 0, [Summary("dodatkowa obrona")]int def = 0, [Summary("dodatkowe hp")]int hp = 0)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var card = await db.Cards.AsQueryable().Include(x => x.Tags).AsSingleQuery().FirstOrDefaultAsync(x => x.Id == id);
                if (card == null)
                {
                    await ReplyAsync("", embed: "W bazie nie ma takiej karty!".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                card.PAS = PreAssembledFigure.None;
                card.Rarity = Rarity.SSS;
                card.FromFigure = true;
                card.Quality = quality;

                if (def != 0) card.DefenceBonus = def;
                if (atk != 0) card.AttackBonus = atk;
                if (hp != 0) card.HealthBonus = hp;

                await db.SaveChangesAsync();

                _waifu.DeleteCardImageIfExist(card);

                QueryCacheManager.ExpireTag(new string[] { $"user-{card.GameDeckId}", "users" });

                await ReplyAsync("", embed: $"Utworzono: {card.GetString(false, false, true)}.".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("setr"), Priority(1), RequireDev]
        [Summary("zamienia ustawia restarty na karcie")]
        [Remarks("54861 100")]
        public async Task ChangeDereOnCardAsync([Summary("WID")]ulong id, [Summary("liczba restartów")]uint rst)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var card = await db.Cards.AsQueryable().Include(x => x.Tags).AsSingleQuery().FirstOrDefaultAsync(x => x.Id == id);
                if (card == null)
                {
                    await ReplyAsync("", embed: "W bazie nie ma takiej karty!".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                card.RestartCnt = (int)rst;

                await db.SaveChangesAsync();

                _waifu.DeleteCardImageIfExist(card);

                QueryCacheManager.ExpireTag(new string[] { $"user-{card.GameDeckId}", "users" });

                await ReplyAsync("", embed: $"Karta została zmodyfikowana: {card.GetString(false, false, true)}.".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("cdere"), Priority(1), RequireDev]
        [Summary("zamienia dere na karcie")]
        [Remarks("54861 Yami")]
        public async Task ChangeDereOnCardAsync([Summary("WID")]ulong id, [Summary("charakter")]Dere dere)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var card = await db.Cards.AsQueryable().Include(x => x.Tags).AsSingleQuery().FirstOrDefaultAsync(x => x.Id == id);
                if (card == null)
                {
                    await ReplyAsync("", embed: "W bazie nie ma takiej karty!".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                card.Dere = dere;

                await db.SaveChangesAsync();

                _waifu.DeleteCardImageIfExist(card);

                QueryCacheManager.ExpireTag(new string[] { $"user-{card.GameDeckId}", "users" });

                await ReplyAsync("", embed: $"Karta została zmodyfikowana: {card.GetString(false, false, true)}.".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("sc"), Priority(1), RequireDev]
        [Summary("dodaje podaną wartość SC użytkownikowi")]
        [Remarks("Sniku 10000")]
        public async Task ChangeUserScAsync([Summary("nazwa użytkownika")]SocketGuildUser user, [Summary("liczba SC")]long amount)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var botuser = await db.GetUserOrCreateSimpleAsync(user.Id);
                botuser.ScCnt += amount;

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"user-{botuser.Id}", "users" });

                await ReplyAsync("", embed: $"{user.Mention} ma teraz {botuser.ScCnt} SC".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("ac"), Priority(1), RequireDev]
        [Summary("dodaje podaną wartość AC użytkownikowi")]
        [Remarks("Sniku 10000")]
        public async Task ChangeUserAcAsync([Summary("nazwa użytkownika")]SocketGuildUser user, [Summary("liczba AC")]long amount)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var botuser = await db.GetUserOrCreateSimpleAsync(user.Id);
                botuser.AcCnt += amount;

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"user-{botuser.Id}", "users" });

                await ReplyAsync("", embed: $"{user.Mention} ma teraz {botuser.AcCnt} AC".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("tc"), Priority(1), RequireDev]
        [Summary("dodaje podaną wartość TC użytkownikowi")]
        [Remarks("Sniku 10000")]
        public async Task ChangeUserTcAsync([Summary("nazwa użytkownika")]SocketGuildUser user, [Summary("liczba TC")]long amount)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var botuser = await db.GetUserOrCreateSimpleAsync(user.Id);
                botuser.TcCnt += amount;

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"user-{botuser.Id}", "users" });

                await ReplyAsync("", embed: $"{user.Mention} ma teraz {botuser.TcCnt} TC".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("pc"), Priority(1), RequireDev]
        [Summary("dodaje podaną wartość PC użytkownikowi")]
        [Remarks("Sniku 10000")]
        public async Task ChangeUserPcAsync([Summary("nazwa użytkownika")]SocketGuildUser user, [Summary("liczba PC")]long amount)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var botuser = await db.GetUserOrCreateSimpleAsync(user.Id);
                botuser.GameDeck.PVPCoins += amount;

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"user-{botuser.Id}", "users" });

                await ReplyAsync("", embed: $"{user.Mention} ma teraz {botuser.GameDeck.PVPCoins} PC".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("ct"), Priority(1), RequireDev]
        [Summary("dodaje podaną wartość CT użytkownikowi")]
        [Remarks("Sniku 10000")]
        public async Task ChangeUserCtAsync([Summary("nazwa użytkownika")]SocketGuildUser user, [Summary("liczba CT")]long amount)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var botuser = await db.GetUserOrCreateSimpleAsync(user.Id);
                botuser.GameDeck.CTCnt += amount;

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"user-{botuser.Id}", "users" });

                await ReplyAsync("", embed: $"{user.Mention} ma teraz {botuser.GameDeck.CTCnt} CT".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("exp"), Priority(1), RequireDev]
        [Summary("dodaje podaną wartość punktów doświadczenia użytkownikowi")]
        [Remarks("Sniku 10000")]
        public async Task ChangeUserExpAsync([Summary("nazwa użytkownika")]SocketGuildUser user, [Summary("liczba punktów doświadczenia")]long amount)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var botuser = await db.GetUserOrCreateSimpleAsync(user.Id);
                botuser.ExpCnt += amount;

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"user-{botuser.Id}", "users" });

                await ReplyAsync("", embed: $"{user.Mention} ma teraz {botuser.ExpCnt} punktów doświadczenia.".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("ost"), Priority(1), RequireDev]
        [Summary("zmienia liczbę ostrzeżeń użytkownika")]
        [Remarks("Jeeda 10000")]
        public async Task ChangeUserOstAsync([Summary("nazwa użytkownika")]SocketGuildUser user, [Summary("liczba ostrzeżeń")]long amount)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var botuser = await db.GetUserOrCreateSimpleAsync(user.Id);
                botuser.Warnings += amount;

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"user-{botuser.Id}", "users" });

                await ReplyAsync("", embed: $"{user.Mention} ma teraz {botuser.Warnings} punktów ostrzeżeń.".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("sime"), Priority(1), RequireDevOrTester]
        [Summary("symuluje wyprawę daną kartą")]
        [Remarks("12312 n")]
        public async Task SimulateExpeditionAsync([Summary("WID")]ulong wid, [Summary("typ wyprawy")]CardExpedition expedition = CardExpedition.None, [Summary("karma")]double k = -1, [Summary("relacja")]double a = -1, [Summary("czas w minutach")]int time = -1)
        {
            if (expedition == CardExpedition.None)
                return;

            using (var db = new Database.DatabaseContext(Config))
            {
                var thisCard = await db.Cards.AsNoTracking().Include(x => x.Tags).Include(x => x.GameDeck).ThenInclude(x => x.User)
                    .Include(x => x.GameDeck).ThenInclude(x => x.ExpContainer).Include(x => x.GameDeck).ThenInclude(x => x.Items).FirstOrDefaultAsync(x => x.Id == wid);
                if (thisCard == null) return;

                thisCard.ExpeditionDate = _time.Now().AddMinutes(time > 0 ? -time : -10080);

                if (k != -1)
                {
                    thisCard.GameDeck.Karma = k;
                }

                if (a != -1)
                {
                    thisCard.Affection = a;
                }

                thisCard.Expedition = expedition;
                var message = _waifu.EndExpedition(thisCard.GameDeck.User, thisCard, true);

                await ReplyAsync("", embed: $"Karta {thisCard.GetString(false, false, true)} wróciła z {expedition.GetName("ej")} wyprawy!\n\n{message}".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("kill", RunMode = RunMode.Async), RequireDev]
        [Summary("wyłącza bota")]
        [Remarks("")]
        public async Task TurnOffAsync()
        {
            await ReplyAsync("", embed: "To dobry czas by umrzeć.".ToEmbedMessage(EMType.Bot).Build());
            await Context.Client.LogoutAsync();
            _spawn.DumpData();

            await Task.Delay(1500);
            Environment.Exit(0);
        }

        [Command("update", RunMode = RunMode.Async), RequireDev]
        [Summary("wyłącza bota z kodem 200")]
        [Remarks("")]
        public async Task TurnOffWithUpdateAsync()
        {
            await ReplyAsync("", embed: "To już czas?".ToEmbedMessage(EMType.Bot).Build());
            await Context.Client.LogoutAsync();
            System.IO.File.Create("./updateNow");
            _spawn.DumpData();

            await Task.Delay(1500);
            Environment.Exit(200);
        }

        [Command("spawncof", RunMode = RunMode.Async), RequireDev]
        [Summary("tworzy nowe polowanie na kartę")]
        [Remarks("")]
        public async Task SpawnCardOnSafariAsync()
        {
            using (var db = new Database.DatabaseContext(_config))
            {
                var config = await db.GetCachedGuildFullConfigAsync(Context.Guild.Id);
                if (config == null) return;

                var sch = Context.Guild.GetTextChannel(config.WaifuConfig.SpawnChannel);
                var tch = Context.Guild.GetTextChannel(config.WaifuConfig.TrashSpawnChannel);
                if (sch != null && tch != null)
                {
                    string mention = "";
                    var wRole = Context.Guild.GetRole(config.WaifuRole);
                    if (wRole != null) mention = wRole.Mention;

                    _spawn.ForceSpawnCard(sch, tch, mention);

                    await ReplyAsync("", embed: new EmbedBuilder().WithImageUrl("https://sanakan.pl/i/gif/seal_ok.gif").WithColor(EMType.Bot.Color()).Build());
                    return;
                }
                await ReplyAsync("", embed: "Serwer nie jest poprawnie skonfigurowany.".ToEmbedMessage(EMType.Error).Build());
            }
        }

        [Command("sci", RunMode = RunMode.Async), RequireDevOrTester]
        [Summary("wypisuje url do obrazka karty")]
        [Remarks("")]
        public async Task ShowCustomImageUrl([Summary("WIDs")]params ulong[] ids)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var cards = db.Cards.AsQueryable().AsSplitQuery().Where(x => ids.Any(c => c == x.Id)).ToList();
                if (cards.Count < 1)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie odnaleziono kart.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                await ReplyAsync("", embed: $"{string.Join("\n", cards.Select(x => $"{x.Id}: {x.CustomImage ?? "---"}"))}".ToEmbedMessage(EMType.Info).Build());
            }
        }

        [Command("rmconfig", RunMode = RunMode.Async), RequireDev]
        [Summary("wyświetla konfiguracje powiadomień na obecnym serwerze")]
        [Remarks("")]
        public async Task ShowRMConfigAsync()
        {
            var serverConfig = Config.Get().RMConfig.Where(x => x.GuildId == Context.Guild.Id || x.GuildId == 0).ToList();
            if (serverConfig.Count > 0)
            {
                await ReplyAsync("", embed: $"**RMC:**\n{string.Join("\n\n", serverConfig)}".TrimToLength().ToEmbedMessage(EMType.Bot).Build());
                return;
            }
            await ReplyAsync("", embed: $"**RMC:**\n\nBrak.".ToEmbedMessage(EMType.Bot).Build());
        }

        [Command("mrmconfig"), RequireDev]
        [Summary("zmienia istniejący wpis w konfiguracji powiadomień w odniesieniu do serwera, lub tworzy nowy gdy go nie ma")]
        [Remarks("News 1232321314 1232412323 tak")]
        public async Task ChangeRMConfigAsync([Summary("typ wpisu")]Api.Models.RichMessageType type, [Summary("id kanału")]ulong channelId, [Summary("id roli")]ulong roleId, [Summary("czy zapisać?")]bool save = false)
        {
            var config = Config.Get();
            var thisRM = config.RMConfig.FirstOrDefault(x => x.Type == type && x.GuildId == Context.Guild.Id);
            if (thisRM == null)
            {
                thisRM = new Config.Model.RichMessageConfig
                {
                    GuildId = Context.Guild.Id,
                    Type = type
                };
                config.RMConfig.Add(thisRM);
            }

            thisRM.ChannelId = channelId;
            thisRM.RoleId = roleId;

            if (save) Config.Save();

            await ReplyAsync("", embed: "Wpis został zmodyfikowany!".ToEmbedMessage(EMType.Success).Build());
        }

        [Command("addwebrmconfig"), Priority(1), RequireDev]
        [Summary("dodaje nowy wpis w konfiguracji powiadomień oparty o webhook")]
        [Remarks("News http://twojweebhook.com/1232412323")]
        public async Task AddWebRMConfigAsync([Summary("typ wpisu")]Api.Models.RichMessageType type, [Summary("url")][Remainder] string url)
        {
            var config = Config.Get();
            var thisRM = config.RMConfig.FirstOrDefault(x => x.Type == type && x.WebHookUrl == url);
            if (thisRM == null)
            {
                thisRM = new Config.Model.RichMessageConfig
                {
                    WebHookUrl = url,
                    Type = type
                };
                config.RMConfig.Add(thisRM);
                Config.Save();
            }
            else
            {
                await ReplyAsync("", embed: "Taki wpis już istnieje!".ToEmbedMessage(EMType.Error).Build());
                return;
            }

            await ReplyAsync("", embed: "Wpis został dodany!".ToEmbedMessage(EMType.Success).Build());
        }

        [Command("removewebrmconfig"), Priority(1), RequireDev]
        [Summary("kasuje wpis w konfiguracji powiadomień oparty o webhook")]
        [Remarks("News http://twojweebhook.com/1232412323")]
        public async Task RemoveWebRMConfigAsync([Summary("typ wpisu")]Api.Models.RichMessageType type, [Summary("url")][Remainder] string url)
        {
            var config = Config.Get();
            var thisRM = config.RMConfig.FirstOrDefault(x => x.Type == type && x.WebHookUrl == url);
            if (thisRM == null)
            {
                await ReplyAsync("", embed: "Taki wpis nie istnieje!".ToEmbedMessage(EMType.Error).Build());
                return;
            }
            else
            {
                config.RMConfig.Remove(thisRM);
                Config.Save();
            }

            await ReplyAsync("", embed: "Wpis został skasowany!".ToEmbedMessage(EMType.Success).Build());
        }

        [Command("ignore"), Priority(1), RequireDev]
        [Summary("dodaje serwer do ignorowanych lub usuwa go z listy")]
        [Remarks("News 1232321314 1232412323 tak")]
        public async Task IgnoreServerAsync()
        {
            var config = Config.Get();
            if (config.BlacklistedGuilds.Contains(Context.Guild.Id))
            {
                config.BlacklistedGuilds.Remove(Context.Guild.Id);
                await ReplyAsync("", embed: "Serwer został usunięty z czarnej listy.".ToEmbedMessage(EMType.Success).Build());
            }
            else
            {
                config.BlacklistedGuilds.Add(Context.Guild.Id);
                await ReplyAsync("", embed: "Serwer został dodany do czarnej listy.".ToEmbedMessage(EMType.Success).Build());
            }
            Config.Save();
        }

        [Command("pomoc", RunMode = RunMode.Async), RequireDevOrTester]
        [Alias("help", "h")]
        [Summary("wypisuje polecenia")]
        [Remarks("kasuj")]
        public async Task SendHelpAsync([Summary("nazwa polecenia")][Remainder]string command = null)
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

                    await ReplyAsync(_helper.GiveHelpAboutPrivateCmd("Debug", command, prefix));
                }
                catch (Exception ex)
                {
                    await ReplyAsync("", embed: ex.Message.ToEmbedMessage(EMType.Error).Build());
                }

                return;
            }

            await ReplyAsync(_helper.GivePrivateHelp("Debug"));
        }
    }
}
