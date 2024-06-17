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
using Sanakan.Config;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Preconditions;
using Sanakan.Services;
using Sanakan.Services.Commands;
using Sanakan.Services.Executor;
using Sanakan.Services.PocketWaifu;
using Sanakan.Services.Session;
using Sanakan.Services.Session.Models;
using Sanakan.Services.Time;
using Shinden.Logger;
using Z.EntityFramework.Plus;
using Sden = Shinden;

namespace Sanakan.Modules
{
    [Name("PocketWaifu"), RequireUserRole]
    public class PocketWaifu : SanakanModuleBase<SocketCommandContext>
    {
        private Services.ImageProcessing _img;
        private Sden.ShindenClient _shclient;
        private Services.Shinden _shinden;
        private SessionManager _session;
        private Services.Helper _helepr;
        private Expedition _expedition;
        private IExecutor _executor;
        private ISystemTime _time;
        private Lottery _lottery;
        private TagHelper _tags;
        private ILogger _logger;
        private IConfig _config;
        private Waifu _waifu;

        public PocketWaifu(Waifu waifu, Sden.ShindenClient client, ILogger logger, Lottery lottery,
            SessionManager session, IConfig config, IExecutor executor, Services.Helper helper,
            ISystemTime time, Services.ImageProcessing img, Services.Shinden shinden, TagHelper tags,
            Expedition expedition)
        {
            _img = img;
            _time = time;
            _tags = tags;
            _waifu = waifu;
            _logger = logger;
            _config = config;
            _helepr = helper;
            _shinden = shinden;
            _lottery = lottery;
            _shclient = client;
            _session = session;
            _executor = executor;
            _expedition = expedition;
        }

        [Command("harem", RunMode = RunMode.Async)]
        [Alias("cards", "karty")]
        [Summary("wyświetla wszystkie posiadane karty")]
        [Remarks("tag konie"), RequireWaifuCommandChannel]
        public async Task ShowCardsAsync([Summary("typ sortowania (klatka/jakość/atak/obrona/relacja/życie/tag(-)/uszkodzone/niewymienialne/obrazek(-/c)/unikat)")] HaremType type = HaremType.Rarity, [Summary("oznaczenie")][Remainder] string tag = null)
        {
            var session = new ListSession<Card>(Context.User, Context.Client.CurrentUser);
            await _session.KillSessionIfExistAsync(session);

            if (type == HaremType.Tag && tag == null)
            {
                await ReplyAsync("", embed: $"{Context.User.Mention} musisz sprecyzować nazwę oznaczenia!".ToEmbedMessage(EMType.Error).Build());
                return;
            }

            using (var db = new Database.DatabaseContext(Config))
            {
                var user = await db.GetCachedFullUserAsync(Context.User.Id);
                if (user?.GameDeck?.Cards?.Count() < 1)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie masz żadnych kart.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                session.Enumerable = false;
                session.ListItems = _waifu.GetListInRightOrder(user.GameDeck.Cards, type, tag);
                if (!string.IsNullOrEmpty(tag) && (tag.Split('|').LastOrDefault()?.Contains("tldr", StringComparison.CurrentCultureIgnoreCase) ?? false))
                {
                    var res = await _helepr.SendAsFileOnDMAsync(Context.User, session.ListItems
                        .Select(x => $"{x.Id} {x.Name} ({x.Character}) {x.GetPocketUrl()}"));

                    await ReplyAsync("", embed: res.ToEmbedMessage($"{Context.User.Mention} ").Build());
                    return;
                }

                session.Embed = new EmbedBuilder
                {
                    Color = EMType.Info.Color(),
                    Title = "Harem"
                };

                try
                {
                    var dm = await Context.User.CreateDMChannelAsync();
                    var msg = await dm.SendMessageAsync("", embed: session.BuildPage(0));
                    await msg.AddReactionsAsync(new[] { new Emoji("⬅"), new Emoji("➡") });

                    session.Message = msg;
                    await _session.TryAddSession(session);

                    await ReplyAsync("", embed: $"{Context.User.Mention} lista poszła na PW!".ToEmbedMessage(EMType.Success).Build());
                }
                catch (Exception)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie można wysłać do Ciebie PW!".ToEmbedMessage(EMType.Error).Build());
                }
            }
        }

        [Command("przedmioty", RunMode = RunMode.Async)]
        [Alias("items", "item", "przedmiot")]
        [Summary("wypisuje posiadane przedmioty (informacje o przedmiocie, gdy podamy jego numer)")]
        [Remarks("tort/1"), RequireWaifuCommandChannel]
        public async Task ShowItemsAsync([Summary("nazwa przedmiotu/nr przedmiotu")] string filter = "")
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var bUser = await db.GetUserAndDontTrackAsync(Context.User.Id);
                var itemList = bUser.GetAllItems();

                if (itemList.IsNullOrEmpty())
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie masz żadnych przemiotów.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var isNumber = int.TryParse(filter, out var numberOfItem);
                if (!isNumber || numberOfItem <= 0)
                {
                    filter = (!string.IsNullOrEmpty(filter) && !isNumber)
                        ? _waifu.NormalizeItemFilter(filter)
                        : string.Empty;

                    var pages = _waifu.GetItemList(Context.User, itemList, filter);
                    if (pages.Count < 1)
                    {
                        await ReplyAsync("", embed: $"{Context.User.Mention} nie odnaleniono przedmiotów zawierających **{filter}** w nazwie.".ToEmbedMessage(EMType.Error).Build());
                        return;
                    }

                    if (pages.Count == 1)
                    {
                        await ReplyAsync("", embed: pages.FirstOrDefault());
                        return;
                    }

                    var res = await _helepr.SendEmbedsOnDMAsync(Context.User, pages);
                    await ReplyAsync("", embed: res.ToEmbedMessage($"{Context.User.Mention} ").Build());
                }

                if (bUser.GameDeck.Items.Count < numberOfItem)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie masz aż tylu przedmiotów.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var item = itemList.ToArray()[numberOfItem - 1];
                var embed = new EmbedBuilder
                {
                    Color = EMType.Info.Color(),
                    Author = new EmbedAuthorBuilder().WithUser(Context.User),
                    Description = $"**{item.Name}**\n_{item.Type.Desc()}_\n\nLiczba: **{item.Count}**".TrimToLength()
                };

                await ReplyAsync("", embed: embed.Build());
            }
        }

        [Command("karta obrazek", RunMode = RunMode.Async)]
        [Alias("card image", "ci", "ko")]
        [Summary("pozwala wyświetlić obrazek karty")]
        [Remarks("685 nie"), RequireAnyCommandChannelLevelOrNitro(40)]
        public async Task ShowCardImageAsync([Summary("WID")] ulong wid, [Summary("czy wyświetlić statystyki?")] bool showStats = false)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var card = db.Cards.Include(x => x.GameDeck).AsNoTracking().FirstOrDefault(x => x.Id == wid);
                if (card == null)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} taka karta nie istnieje.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                SocketUser user = Context.Guild.GetUser(card.GameDeck.UserId);
                if (user == null) user = Context.Client.GetUser(card.GameDeck.UserId);

                var gConfig = await db.GetCachedGuildFullConfigAsync(Context.Guild.Id);
                var trashChannel = Context.Guild.GetTextChannel(gConfig.WaifuConfig.TrashCommandsChannel);
                await ReplyAsync("", embed: await _waifu.BuildCardImageAsync(card, trashChannel, user, showStats));
            }
        }

        [Command("figurki")]
        [Alias("figures")]
        [Summary("pozwala wyświetlić liste figurę/ustawić aktywną figurkę")]
        [Remarks("2"), RequireWaifuCommandChannel]
        public async Task ShowFigureListAsync([Summary("ID")] ulong id = 0)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var deck = db.GameDecks.Include(x => x.Figures).FirstOrDefault(x => x.Id == Context.User.Id);
                if (id > 0)
                {
                    var oldFig = deck.Figures.FirstOrDefault(x => x.IsFocus);
                    var fig = deck.Figures.FirstOrDefault(x => x.Id == id);
                    if (fig == null)
                    {
                        await ReplyAsync("", embed: $"{Context.User.Mention} taka figurka nie istnieje.".ToEmbedMessage(EMType.Error).Build());
                        return;
                    }
                    if (fig.IsComplete)
                    {
                        await ReplyAsync("", embed: $"{Context.User.Mention} nie można aktywować skończonej figurki.".ToEmbedMessage(EMType.Error).Build());
                        return;
                    }
                    if (oldFig != null)
                    {
                        if (fig.Id == oldFig.Id)
                        {
                            await ReplyAsync("", embed: $"{Context.User.Mention} ta figurką już jest wybrana.".ToEmbedMessage(EMType.Error).Build());
                            return;
                        }

                        oldFig.IsFocus = false;
                    }

                    fig.IsFocus = true;

                    await db.SaveChangesAsync();

                    await ReplyAsync("", embed: $"{Context.User.Mention} ustawiono figurkę {fig.Id} jako aktywną.".ToEmbedMessage(EMType.Success).Build());
                }
                else
                {
                    await ReplyAsync("", embed: deck.GetFiguresList().TrimToLength().ToEmbedMessage(EMType.Info).WithAuthor(new EmbedAuthorBuilder().WithUser(Context.User)).Build());
                }
            }
        }

        [Command("wybierz element")]
        [Alias("select part")]
        [Summary("pozwala wybrać część w aktywnej figurce do przekazywania doświadczenia")]
        [Remarks("lewa noga"), RequireWaifuCommandChannel]
        public async Task SelectActiveFigurePartAsync([Summary("część")][Remainder] FigurePart part)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var deck = db.GameDecks.Include(x => x.Figures).Where(x => x.Id == Context.User.Id).FirstOrDefault();
                var fig = deck.Figures.FirstOrDefault(x => x.IsFocus);
                if (fig == null)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} żadna figurka nie jest aktywna.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }
                if (part == FigurePart.None || part == FigurePart.All || fig.FocusedPart == part)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} podano niepoprawną część figurki.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }
                if (fig.GetQualityOfPart(part) != Quality.Broken)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} dana część została już zainstalowana do figurki i nie można zbierać na nią doświadczenia.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                fig.FocusedPart = part;
                fig.PartExp = 0;

                await db.SaveChangesAsync();

                await ReplyAsync("", embed: $"Wybrana część to: {part.ToName()}".ToEmbedMessage(EMType.Info).WithAuthor(new EmbedAuthorBuilder().WithUser(Context.User)).Build());
            }
        }

        [Command("figurka koniec")]
        [Alias("figure end")]
        [Summary("zamienia aktywną figurkę w kartę ultimate")]
        [Remarks(""), RequireWaifuCommandChannel]
        public async Task EndCreatingFigureAsync()
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var deck = db.GameDecks.Include(x => x.User).Include(x => x.Figures).Include(x => x.Cards).Where(x => x.Id == Context.User.Id).FirstOrDefault();
                var fig = deck.Figures.FirstOrDefault(x => x.IsFocus);
                if (fig == null)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} żadna figurka nie jest aktywna.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }
                if (!fig.CanCreateUltimateCard())
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie można utowrzyć karty ultimate, brakuje części lub doświadczenia.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var endTime = _time.Now();
                var card = fig.ToCard(endTime);
                deck.Cards.Add(card);

                var wishlists = db.GameDecks.Include(x => x.Wishes).AsNoTracking().Where(x => !x.WishlistIsPrivate && x.Wishes.Any(c => c.Type == WishlistObjectType.Character && c.ObjectId == card.Character)).ToList();
                card.WhoWantsCount = wishlists.Count;

                fig.CompletionDate = endTime;
                fig.IsComplete = true;
                fig.IsFocus = false;

                await db.SaveChangesAsync();

                fig.CreatedCardId = card.Id;

                await db.UserActivities.AddAsync(new Services.UserActivityBuilder(_time).WithUser(deck.User, Context.User)
                    .WithCard(card).WithType(Database.Models.ActivityType.CreatedUltiamte).Build());

                await db.SaveChangesAsync();

                await ReplyAsync("", embed: $"Utworzono nową kartę ultimate: {card.GetString(false, false, true)}".ToEmbedMessage(EMType.Success).WithAuthor(new EmbedAuthorBuilder().WithUser(Context.User)).Build());
            }
        }

        [Command("figurka", RunMode = RunMode.Async)]
        [Alias("figure")]
        [Summary("pozwala wyświetlić figurkę")]
        [Remarks("2"), RequireWaifuCommandChannel]
        public async Task ShowFigureAsync([Summary("ID")] ulong id = 0)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var fig = db.Figures.AsQueryable().AsNoTracking().FirstOrDefault(x => x.Id == id || (id == 0 && x.IsFocus && x.GameDeckId == Context.User.Id));
                if (fig == null)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} taka figurka nie istnieje.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                await ReplyAsync("", embed: fig.GetDesc().TrimToLength().ToEmbedMessage(EMType.Info)
                    .WithUser(Context.Guild.GetUser(fig.GameDeckId)).Build());
            }
        }

        [Command("karta-", RunMode = RunMode.Async)]
        [Alias("card-")]
        [Summary("pozwala wyświetlić kartę w prostej postaci")]
        [Remarks("685"), RequireAnyCommandChannelOrLevel(40)]
        public async Task ShowCardStringAsync([Summary("WID")] ulong wid)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var card = db.Cards.Include(x => x.GameDeck).Include(x => x.Tags).AsNoTracking().FirstOrDefault(x => x.Id == wid);
                if (card == null)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} taka karta nie istnieje.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                SocketUser user = Context.Guild.GetUser(card.GameDeck.UserId);
                if (user == null) user = Context.Client.GetUser(card.GameDeck.UserId);

                await ReplyAsync("", embed: card.GetDescSmall(_tags, _time).TrimToLength().ToEmbedMessage(EMType.Info).WithAuthor(new EmbedAuthorBuilder().WithUser(user)).Build());
            }
        }

        [Command("karta", RunMode = RunMode.Async)]
        [Alias("card")]
        [Summary("pozwala wyświetlić kartę")]
        [Remarks("685"), RequireWaifuCommandChannel]
        public async Task ShowCardAsync([Summary("WID")] ulong wid)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var card = db.Cards.Include(x => x.GameDeck).Include(x => x.Tags).AsNoTracking().FirstOrDefault(x => x.Id == wid);
                if (card == null)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} taka karta nie istnieje.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                SocketUser user = Context.Guild.GetUser(card.GameDeck.UserId);
                if (user == null) user = Context.Client.GetUser(card.GameDeck.UserId);

                var gConfig = await db.GetCachedGuildFullConfigAsync(Context.Guild.Id);
                var trashChannel = Context.Guild.GetTextChannel(gConfig.WaifuConfig.TrashCommandsChannel);
                await ReplyAsync("", embed: await _waifu.BuildCardViewAsync(card, trashChannel, user));
            }
        }

        [Command("koszary")]
        [Alias("pvp shop")]
        [Summary("listowanie/zakup przedmiotu/wypisanie informacji")]
        [Remarks("1 info/4"), RequireWaifuCommandChannel]
        public async Task BuyItemPvPAsync([Summary("nr przedmiotu")] int itemNumber = 0, [Summary("informacje/liczba przedmiotów do zakupu")] string info = "0")
        {
            await ReplyAsync("", embed: await _waifu.ExecuteShopAsync(ShopType.Pvp, Config, Context.User, itemNumber, info));
        }

        [Command("kiosk")]
        [Alias("ac shop")]
        [Summary("listowanie/zakup przedmiotu/wypisanie informacji")]
        [Remarks("1 info/4"), RequireWaifuCommandChannel]
        public async Task BuyItemActivityAsync([Summary("nr przedmiotu")] int itemNumber = 0, [Summary("informacje/liczba przedmiotów do zakupu")] string info = "0")
        {
            await ReplyAsync("", embed: await _waifu.ExecuteShopAsync(ShopType.Activity, Config, Context.User, itemNumber, info));
        }

        [Command("sklepik")]
        [Alias("shop", "p2w")]
        [Summary("listowanie/zakup przedmiotu/wypisanie informacji (du użycia wymagany 10 lvl)")]
        [Remarks("1 info/4"), RequireWaifuCommandChannel, RequireLevel(10)]
        public async Task BuyItemAsync([Summary("nr przedmiotu")] int itemNumber = 0, [Summary("informacje/id tytułu/liczba przedmiotów do zakupu")] string info = "0")
        {
            await ReplyAsync("", embed: await _waifu.ExecuteShopAsync(ShopType.Normal, Config, Context.User, itemNumber, info));
        }

        [Command("użyjbk")]
        [Alias("usewc", "uzyjbk")]
        [Summary("używa przedmiot")]
        [Remarks("1 1 tak"), RequireWaifuCommandChannel]
        public async Task UseItemAsync([Summary("nr przedmiotu")] int itemNumber, [Summary("liczba przedmiotów")] string detail = "1", [Summary("czy zamienić część figurki na exp?")] bool itemToExp = false, [Hidden] ulong wid = 0)
            => await UseItemOnCardAsync(itemNumber, wid, detail, itemToExp);

        [Command("użyj")]
        [Alias("uzyj", "use")]
        [Summary("używa przedmiot na karcie")]
        [Remarks("1 4212 2"), RequireWaifuCommandChannel]
        public async Task UseItemOnCardAsync([Summary("nr przedmiotu")] int itemNumber, [Summary("WID")] ulong wid = 0, [Summary("liczba przedmiotów/link do obrazka/typ gwiazdki")] string detail = "1", [Hidden] bool itemToExp = false)
        {
            if (detail.Equals("att", StringComparison.CurrentCultureIgnoreCase))
            {
                if (Context.Message.Attachments.IsNullOrEmpty())
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} wybrano upload przez załącznik, lecz nie został on wykryty.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }
                detail = Context.Message.Attachments.FirstOrDefault()?.Url ?? "";
            }

            using (var db = new Database.DatabaseContext(Config))
            {
                var bUser = await db.GetUserOrCreateAsync(Context.User.Id);
                var res = await _waifu.UseItemAsync(bUser, Context.User.GetUserNickInGuild(), itemNumber, wid, detail, itemToExp);

                if (res.IsError())
                {
                    await ReplyAsync("", embed: res.ToEmbedMessage($"{Context.User.Mention} ").Build());
                    return;
                }
                else if (res.IsActivity())
                {
                    await db.UserActivities.AddAsync(res.Activity.AddMisc($"u:{Context.User.GetUserNickInGuild()}").Build());
                }

                await ReplyAsync("", embed: res.ToEmbedMessage().WithUser(Context.User).Build());

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"user-{bUser.Id}", "users" });
            }
        }

        [Command("chce wszystkie waifu")]
        [Alias("i want them all")]
        [Summary("zwiększa pule postaci o te z mang")]
        [Remarks(""), RequireWaifuCommandChannel]
        public async Task SetCharacterPoolAsync()
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var bUser = await db.GetUserOrCreateAsync(Context.User.Id);
                if (bUser.PoolType != CharacterPoolType.Anime)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} twoja pula postaci została już wcześniej rozszerzona.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                bUser.PoolType = CharacterPoolType.All;

                await db.SaveChangesAsync();

                await ReplyAsync("", embed: $"{Context.User.Mention} twoja pula postaci została rozszerzona.".ToEmbedMessage(EMType.Success).Build());

                QueryCacheManager.ExpireTag(new string[] { $"user-{bUser.Id}", "users" });
            }
        }

        [Command("lazyp")]
        [Alias("lp")]
        [Summary("otwiera pierwszy pakiet z domyślnie ustawionym niszczeniem kc na 3 oraz tagiem wymiana")]
        [Remarks("2 nie Wymiana Ulubione"), RequireAnyCommandChannelOrLevel(200)]
        public async Task OpenPacketLazyModeAsync([Summary("czy zniszczyć karty nie będące na liście życzeń i nie posiadające danej kc?")] uint destroyCards = 3, [Summary("czy zamienić niszczenie na uwalnianie?")] bool changeToRelease = false,
            [Summary("oznacz niezniszczone karty")] string tag = "wymiana", [Summary("oznacz karty z wishlisty")] string tagWishlist = "ulubione")
                => await OpenPacketAsync(1, 1, true, destroyCards, changeToRelease, tag, tagWishlist);

        [Command("pakiet")]
        [Alias("pakiet kart", "booster", "booster pack", "pack")]
        [Summary("wypisuje dostępne pakiety/otwiera pakiety(maksymalna suma kart z pakietów do otworzenia to 20)")]
        [Remarks("1 1 tak 2 nie Wymiana Ulubione"), RequireWaifuCommandChannel]
        public async Task OpenPacketAsync([Summary("nr pakietu kart")] int numberOfPack = 0, [Summary("liczba kolejnych pakietów")] int count = 1, [Summary("czy sprawdzić listy życzeń?")] bool checkWishlists = true,
            [Summary("czy zniszczyć karty nie będące na liście życzeń i nie posiadające danej kc?")] uint destroyCards = 0, [Summary("czy zamienić niszczenie na uwalnianie?")] bool changeToRelease = false, [Summary("oznacz niezniszczone karty")] string tag = "",
            [Summary("oznacz karty z wishlisty")] string tagWishlist = "")
        {
            if (!string.IsNullOrEmpty(tag) && tag.Contains(" "))
            {
                await ReplyAsync("", embed: $"{Context.User.Mention} oznaczenie nie może zawierać spacji.".ToEmbedMessage(EMType.Error).Build());
                return;
            }

            if (!string.IsNullOrEmpty(tagWishlist) && tagWishlist.Contains(" "))
            {
                await ReplyAsync("", embed: $"{Context.User.Mention} oznaczenie nie może zawierać spacji.".ToEmbedMessage(EMType.Error).Build());
                return;
            }

            using (var db = new Database.DatabaseContext(Config))
            {
                await db.Database.OpenConnectionAsync();
                var bUser = await db.GetUserOrCreateAsync(Context.User.Id);

                if (bUser.GameDeck.BoosterPacks.Count < 1)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie masz żadnych pakietów.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (numberOfPack == 0)
                {
                    await ReplyAsync("", embed: _waifu.GetBoosterPackList(Context.User, bUser.GameDeck.BoosterPacks.ToList()));
                    return;
                }

                if (bUser.GameDeck.BoosterPacks.Count < numberOfPack || numberOfPack <= 0)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie masz aż tylu pakietów.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (bUser.GameDeck.BoosterPacks.Count < (count + numberOfPack - 1) || count < 1)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie masz tylu pakietów.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var packs = bUser.GameDeck.BoosterPacks.ToList().GetRange(numberOfPack - 1, count);
                var cardsCount = packs.Sum(x => x.CardCnt);

                if (cardsCount > 20)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} suma kart z otwieranych pakietów nie może być większa niż dwadzieścia.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (bUser.GameDeck.Cards.Count + cardsCount > bUser.GameDeck.MaxNumberOfCards)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie masz już miejsca na kolejną kartę!".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var mission = bUser.TimeStatuses.FirstOrDefault(x => x.Type == Database.Models.StatusType.DPacket);
                if (mission == null)
                {
                    mission = StatusType.DPacket.NewTimeStatus();
                    bUser.TimeStatuses.Add(mission);
                }

                var totalCards = new List<Card>();
                var charactersOnWishlist = new List<string>();
                foreach (var pack in packs)
                {
                    var cards = await _waifu.OpenBoosterPackAsync(Context.User, pack, bUser.PoolType);
                    if (cards.Count < pack.CardCnt)
                    {
                        await ReplyAsync("", embed: $"{Context.User.Mention} nie udało się otworzyć pakietu. Brak połączania z Shindenem!".ToEmbedMessage(EMType.Error).Build());
                        return;
                    }

                    if (pack.CardSourceFromPack != CardSource.Api)
                        mission.Count(_time.Now());

                    if (pack.CardSourceFromPack == CardSource.Activity || pack.CardSourceFromPack == CardSource.Migration)
                    {
                        bUser.Stats.OpenedBoosterPacksActivity += 1;
                    }
                    else
                    {
                        bUser.Stats.OpenedBoosterPacks += 1;
                    }

                    bUser.GameDeck.BoosterPacks.Remove(pack);
                    totalCards.AddRange(cards);
                }

                var allWWCnt = await db.WishlistCountData.AsQueryable().AsNoTracking().ToListAsync();
                foreach (var card in totalCards)
                {
                    if (await bUser.GameDeck.RemoveCharacterFromWishListAsync(card.Character, db))
                        charactersOnWishlist.Add(card.Name);

                    if (checkWishlists)
                    {
                        bool isOnUserWishlist = charactersOnWishlist.Any(x => x == card.Name);
                        var wishlistsCnt = allWWCnt.FirstOrDefault(x => x.Id == card.Character)?.Count ?? 0;
                        if (destroyCards > 0)
                        {
                            if (wishlistsCnt < destroyCards && !isOnUserWishlist)
                            {
                                card.DestroyOrRelease(bUser, changeToRelease);
                                continue;
                            }
                        }

                        card.WhoWantsCount = wishlistsCnt;
                        if (!string.IsNullOrEmpty(tag) && !isOnUserWishlist)
                        {
                            var btag = await db.GetTagAsync(_tags, tag, Context.User.Id);
                            if (btag != null) card.Tags.Add(btag);
                        }

                        if (!string.IsNullOrEmpty(tagWishlist) && isOnUserWishlist)
                        {
                            var btag = await db.GetTagAsync(_tags, tagWishlist, Context.User.Id);
                            if (btag != null) card.Tags.Add(btag);
                        }
                    }
                    card.Affection += bUser.GameDeck.AffectionFromKarma();
                    bUser.GameDeck.Cards.Add(card);
                }

                await db.SaveChangesAsync();
                QueryCacheManager.ExpireTag(new string[] { $"user-{bUser.Id}", "users" });

                string openString = "";
                string packString = $"{count} pakietów";
                if (count == 1) packString = $"pakietu **{packs.First().Name}**";

                bool saveAgain = false;
                foreach (var card in totalCards)
                {
                    if (checkWishlists)
                    {
                        bool isOnUserWishlist = charactersOnWishlist.Any(x => x == card.Name);
                        if (card.WhoWantsCount < destroyCards && !isOnUserWishlist && destroyCards > 0)
                        {
                            openString += "🖤 ";
                        }
                        else
                        {
                            openString += card.ToHeartWishlist(isOnUserWishlist);
                            if (db.AddActivityFromNewCard(card, isOnUserWishlist, _time, bUser, Context.User.GetUserNickInGuild()))
                            {
                                saveAgain = true;
                            }
                        }
                    }
                    openString += $"{card.GetString(false, false, true)}\n";
                }

                if (saveAgain)
                {
                    await db.SaveChangesAsync();
                }

                await db.Database.CloseConnectionAsync();
                await ReplyAsync("", embed: $"{Context.User.Mention} z {packString} wypadło:\n\n{openString.TrimToLength()}".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("reset")]
        [Alias("restart")]
        [Summary("restartuje kartę SSS do rangi E i dodaje stały bonus")]
        [Remarks("5412"), RequireWaifuCommandChannel]
        public async Task ResetCardAsync([Summary("WID")] ulong id)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var bUser = await db.GetUserOrCreateAsync(Context.User.Id);
                var card = bUser.GameDeck.Cards.FirstOrDefault(x => x.Id == id);

                if (card == null)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie posiadasz takiej karty.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (card.Rarity != Rarity.SSS)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} ta karta nie ma najwyższego poziomu.".ToEmbedMessage(EMType.Bot).Build());
                    return;
                }

                if (card.FromFigure)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} tej karty nie można restartować.".ToEmbedMessage(EMType.Bot).Build());
                    return;
                }

                if (card.Expedition != CardExpedition.None)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} ta karta jest na wyprawie!".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (card.IsUnusable())
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} ta karta ma zbyt niską relację, aby dało się ją zrestartować.".ToEmbedMessage(EMType.Bot).Build());
                    return;
                }

                bUser.GameDeck.Karma -= 5;

                card.Defence = Waifu.RandomizeDefence(Rarity.E);
                card.Attack = Waifu.RandomizeAttack(Rarity.E);
                card.Dere = Waifu.RandomizeDere();
                card.Rarity = Rarity.E;
                card.UpgradesCnt = 2;
                card.RestartCnt += 1;
                card.ExpCnt = 0;

                card.Affection = card.RestartCnt * -0.2;

                _ = card.CalculateCardPower();

                if (card.RestartCnt > 1 && card.RestartCnt % 10 == 0 && card.RestartCnt <= 100)
                {
                    bUser.GameDeck.AddItem(ItemType.SetCustomImage.ToItem());
                }

                await db.SaveChangesAsync();
                _waifu.DeleteCardImageIfExist(card);

                QueryCacheManager.ExpireTag(new string[] { $"user-{bUser.Id}", "users" });

                await ReplyAsync("", embed: $"{Context.User.Mention} zrestartował kartę do: {card.GetString(false, false, true)}.".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("aktualizuj")]
        [Alias("update")]
        [Summary("pobiera dane na tamat karty z shindena")]
        [Remarks("5412 nie"), RequireWaifuCommandChannel]
        public async Task UpdateCardAsync([Summary("WID")] ulong id, [Summary("czy przywrócić obrazek ze strony?")] bool defaultImage = false)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var bUser = await db.GetUserOrCreateAsync(Context.User.Id);
                var card = bUser.GameDeck.Cards.FirstOrDefault(x => x.Id == id);

                if (card == null)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie posiadasz takiej karty.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (defaultImage)
                {
                    if (Dir.IsLocal(card.CustomImage))
                    {
                        if (File.Exists(card.CustomImage))
                            File.Delete(card.CustomImage);
                    }
                    card.CustomImage = null;
                }

                card.CalculateCardPower();

                _waifu.DeleteCardImageIfExist(card);

                QueryCacheManager.ExpireTag(new string[] { $"user-{bUser.Id}", "users" });

                try
                {
                    await card.Update(Context.User, _shclient, defaultImage);

                    var wCount = await db.GameDecks.Include(x => x.Wishes).AsNoTracking().Where(x => !x.WishlistIsPrivate && x.Wishes.Any(c => c.Type == WishlistObjectType.Character && c.ObjectId == card.Character)).CountAsync();
                    await db.WishlistCountData.CreateOrChangeWishlistCountByAsync(card.Character, card.Name, wCount, true);

                    await db.SaveChangesAsync();

                    await ReplyAsync("", embed: $"{Context.User.Mention} zaktualizował kartę: {card.GetString(false, false, true)}.".ToEmbedMessage(EMType.Success).Build());
                }
                catch (Exception ex)
                {
                    await db.SaveChangesAsync();
                    await ReplyAsync("", embed: $"{Context.User.Mention}: {ex.Message}".ToEmbedMessage(EMType.Error).Build());
                }
            }
        }

        [Command("ulepsz")]
        [Alias("upgrade")]
        [Summary("ulepsza kartę na lepszą jakość")]
        [Remarks("5412"), RequireWaifuCommandChannel]
        public async Task UpgradeCardAsync([Summary("WID karty do ulepszenia")] ulong id, [Summary("WID karty do poświęcenia")] ulong cardToSac = 0)
        {
            if (_session.SessionExist(Context.User, typeof(ExchangeSession)))
            {
                await ReplyAsync("", embed: $"{Context.User.Mention} znajdujesz się obecnie w trakcie wymiany.".ToEmbedMessage(EMType.Error).Build());
                return;
            }

            using (var db = new Database.DatabaseContext(Config))
            {
                var bUser = await db.GetUserOrCreateAsync(Context.User.Id);
                var card = bUser.GameDeck.Cards.FirstOrDefault(x => x.Id == id);

                if (card == null)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie posiadasz takiej karty.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (card.Rarity == Rarity.SSS && card.Quality == Quality.Broken)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} ta karta ma już najwyższy poziom.".ToEmbedMessage(EMType.Bot).Build());
                    return;
                }

                if (card.Expedition != CardExpedition.None)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} ta karta jest na wyprawie!".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (card.UpgradesCnt < 1 && card.Quality == Quality.Broken)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} ta karta nie ma już dostępnych ulepszeń.".ToEmbedMessage(EMType.Bot).Build());
                    return;
                }

                if (card.ExpCnt < card.ExpToUpgrade() && card.Quality == Quality.Broken)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} ta karta ma niewystarczającą ilość punktów doświadczenia. Wymagane {card.ExpToUpgrade():F}.".ToEmbedMessage(EMType.Bot).Build());
                    return;
                }

                if (card.UpgradesCnt < 5 && card.Rarity == Rarity.SS)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} ta karta ma zbyt małą ilość ulepszeń.".ToEmbedMessage(EMType.Bot).Build());
                    return;
                }

                if (!card.CanGiveBloodOrUpgradeToSSS() && card.Rarity == Rarity.SS)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} ta karta ma zbyt małą relację, aby ją ulepszyć.".ToEmbedMessage(EMType.Bot).Build());
                    return;
                }

                if (card.Quality != Quality.Broken)
                {
                    if (card.Quality == Quality.Omega)
                    {
                        await ReplyAsync("", embed: $"{Context.User.Mention} tej karty nie można już ulepszyć.".ToEmbedMessage(EMType.Error).Build());
                        return;
                    }

                    if (cardToSac == 0)
                    {
                        var fig = bUser.GameDeck.Figures.FirstOrDefault(x => x.IsFocus);
                        if (fig == null)
                        {
                            await ReplyAsync("", embed: $"{Context.User.Mention} nie posiadasz aktywnej figurki.".ToEmbedMessage(EMType.Error).Build());
                            return;
                        }

                        if (fig.IsComplete)
                        {
                            await ReplyAsync("", embed: $"{Context.User.Mention} z tej figurki powstała już karta.".ToEmbedMessage(EMType.Error).Build());
                            return;
                        }

                        if (!fig.CanCreateUltimateCard())
                        {
                            await ReplyAsync("", embed: $"{Context.User.Mention} twoja figurka nie posiada wszystkich elementów.".ToEmbedMessage(EMType.Error).Build());
                            return;
                        }

                        if (card.Quality != fig.GetAvgQuality())
                        {
                            await ReplyAsync("", embed: $"{Context.User.Mention} ta figurka nie ma takiej samej jakości jak karta którą chcesz ulepszyć.".ToEmbedMessage(EMType.Error).Build());
                            return;
                        }

                        bUser.GameDeck.Figures.Remove(fig);
                    }
                    else
                    {
                        var cardSac = bUser.GameDeck.Cards.FirstOrDefault(x => x.Id == cardToSac);
                        if (cardSac == null)
                        {
                            await ReplyAsync("", embed: $"{Context.User.Mention} nie posiadasz takiej karty.".ToEmbedMessage(EMType.Error).Build());
                            return;
                        }

                        if (card.Quality != cardSac.Quality)
                        {
                            await ReplyAsync("", embed: $"{Context.User.Mention} ta karta nie ma takiej samej jakości jak karta którą chcesz ulepszyć.".ToEmbedMessage(EMType.Error).Build());
                            return;
                        }

                        var figure = bUser.GameDeck.Figures.FirstOrDefault(x => x.CreatedCardId == cardToSac);
                        if (figure == null)
                        {
                            await ReplyAsync("", embed: $"{Context.User.Mention} nie posiadasz figurki tej karty.".ToEmbedMessage(EMType.Error).Build());
                            return;
                        }

                        bUser.GameDeck.Cards.Remove(cardSac);
                        bUser.GameDeck.Figures.Remove(figure);
                    }

                    card.Quality = card.Quality.Next();
                }
                else
                {
                    ++bUser.Stats.UpgaredCards;
                    bUser.GameDeck.Karma += 1;

                    card.Defence = _waifu.GetDefenceAfterLevelUp(card.Rarity, card.Defence);
                    card.Attack = _waifu.GetAttactAfterLevelUp(card.Rarity, card.Attack);
                    card.UpgradesCnt -= (card.Rarity == Rarity.SS ? 5 : 1);
                    card.Rarity = --card.Rarity;
                    card.Affection += 1;
                    card.ExpCnt = 0;

                    _ = card.CalculateCardPower();

                    if (card.Rarity == Rarity.SSS)
                    {
                        if (card.RestartCnt < 1)
                        {
                            if (bUser.Stats.UpgradedToSSS % 10 == 0)
                            {
                                bUser.GameDeck.AddItem(ItemType.SetCustomImage.ToItem());
                            }
                            ++bUser.Stats.UpgradedToSSS;
                        }

                        await db.UserActivities.AddAsync(new Services.UserActivityBuilder(_time).WithUser(bUser, Context.User)
                            .WithCard(card).WithType(Database.Models.ActivityType.CreatedSSS).Build());
                    }
                }

                await db.SaveChangesAsync();
                _waifu.DeleteCardImageIfExist(card);

                QueryCacheManager.ExpireTag(new string[] { $"user-{bUser.Id}", "users" });

                await ReplyAsync("", embed: $"{Context.User.Mention} ulepszył kartę do: {card.GetString(false, false, true)}.".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("uwolnij")]
        [Alias("release", "puśmje")]
        [Summary("uwalnia posiadaną kartę(nie podanie kart, uwalnia te oznaczone jako kosz)")]
        [Remarks("5412 5413"), RequireWaifuCommandChannel]
        public async Task ReleaseCardAsync([Summary("WIDs")] params ulong[] ids)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var bUser = await db.GetUserOrCreateAsync(Context.User.Id);
                var res = _waifu.DestroyOrReleaseCards(bUser, ids, true, _tags.GetTagId(Services.PocketWaifu.TagType.TrashBin));

                if (res.IsError())
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} {res.Message}".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"user-{bUser.Id}", "users" });

                await ReplyAsync("", embed: $"{Context.User.Mention} {res.Message}".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("zniszcz")]
        [Alias("destroy")]
        [Summary("niszczy posiadaną kartę(nie podanie kart, niszczy te oznaczone jako kosz)")]
        [Remarks("5412"), RequireWaifuCommandChannel]
        public async Task DestroyCardAsync([Summary("WIDs")] params ulong[] ids)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var bUser = await db.GetUserOrCreateAsync(Context.User.Id);
                var res = _waifu.DestroyOrReleaseCards(bUser, ids, false, _tags.GetTagId(Services.PocketWaifu.TagType.TrashBin));

                if (res.IsError())
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} {res.Message}".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"user-{bUser.Id}", "users" });

                await ReplyAsync("", embed: $"{Context.User.Mention} {res.Message}".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("skrzynia")]
        [Alias("chest")]
        [Summary("przenosi doświadczenie z skrzyni do karty lub figurki gdy podane wid 0 (kosztuje CT)")]
        [Remarks("2154 10"), RequireWaifuCommandChannel]
        public async Task TransferExpFromChestAsync([Summary("WID")] ulong id, [Summary("ilość doświadczenia")] uint exp)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var bUser = await db.GetUserOrCreateAsync(Context.User.Id);
                if (bUser.GameDeck.ExpContainer.Level == ExpContainerLevel.Disabled)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie posiadasz jeszcze skrzyni doświadczenia.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                Figure focusedFigure = null;
                if (id == 0)
                {
                    focusedFigure = bUser.GameDeck.Figures.FirstOrDefault(x => x.IsFocus);
                    if (focusedFigure == null)
                    {
                        await ReplyAsync("", embed: $"{Context.User.Mention} nie posiadasz aktywnej figurki.".ToEmbedMessage(EMType.Error).Build());
                        return;
                    }
                }

                Card card = null;
                if (focusedFigure == null)
                {
                    card = bUser.GameDeck.Cards.FirstOrDefault(x => x.Id == id);
                    if (card == null)
                    {
                        await ReplyAsync("", embed: $"{Context.User.Mention} nie posiadasz takiej karty.".ToEmbedMessage(EMType.Error).Build());
                        return;
                    }

                    if (card.FromFigure)
                    {
                        await ReplyAsync("", embed: $"{Context.User.Mention} na tą kartę nie można przenieść doświadczenia.".ToEmbedMessage(EMType.Error).Build());
                        return;
                    }
                }

                var maxExpInOneTime = bUser.GameDeck.ExpContainer.GetMaxExpTransferToCard();
                if (maxExpInOneTime != -1 && exp > maxExpInOneTime)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} na tym poziomie możesz jednorazowo przelać tylko {maxExpInOneTime} doświadczenia.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (bUser.GameDeck.ExpContainer.ExpCount < exp)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie posiadasz wystarczającej ilości doświadczenia.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var cost = bUser.GameDeck.ExpContainer.GetTransferCTCost();
                if (bUser.GameDeck.CTCnt < cost)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie masz wystarczającej liczby CT. ({cost})".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (focusedFigure == null)
                {
                    card.ExpCnt += exp;
                }
                else
                {
                    focusedFigure.ExpCnt += exp;
                }

                bUser.GameDeck.ExpContainer.ExpCount -= exp;
                bUser.GameDeck.CTCnt -= cost;

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"user-{bUser.Id}", "users" });

                await ReplyAsync("", embed: $"{Context.User.Mention} przeniesiono doświadczenie na kartę lub figurkę.".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("tworzenie skrzyni")]
        [Alias("make chest")]
        [Summary("tworzy lub ulepsza skrzynię doświadczenia")]
        [Remarks("2154"), RequireWaifuCommandChannel]
        public async Task CreateChestAsync([Summary("WIDs")] params ulong[] ids)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var bUser = await db.GetUserOrCreateAsync(Context.User.Id);
                var cardsToSac = bUser.GetCards(ids).ToList();

                if (cardsToSac.Count < 1)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie posiadasz takich kart.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                foreach (var card in cardsToSac)
                {
                    if (card.Rarity != Rarity.SSS)
                    {
                        await ReplyAsync("", embed: $"{Context.User.Mention} ta karta nie jest kartą SSS.".ToEmbedMessage(EMType.Error).Build());
                        return;
                    }

                    if (card.IsProtectedFromDiscarding(_tags))
                    {
                        await ReplyAsync("", embed: $"{Context.User.Mention} tej karty z jakiegoś powodu nie można zniszczyć.".ToEmbedMessage(EMType.Error).Build());
                        return;
                    }
                }

                long cardNeeded = bUser.GameDeck.ExpContainer.GetChestUpgradeCostInCards();
                long bloodNeeded = bUser.GameDeck.ExpContainer.GetChestUpgradeCostInBlood();
                if (cardNeeded == -1 || bloodNeeded == -1)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie można bardziej ulepszyć skrzyni.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (cardsToSac.Count < cardNeeded)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} podałeś za mało kart SSS. ({cardNeeded})".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var bloodYour = bUser.GameDeck.Items.FirstOrDefault(x => x.Type == ItemType.BetterIncreaseUpgradeCnt);
                var bloodWaifu = bUser.GameDeck.Items.FirstOrDefault(x => x.Type == ItemType.BloodOfYourWaifu);
                if (bloodYour is null && bloodWaifu is null)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie posiadasz kropel krwi.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var totalBlood = (bloodYour?.Count ?? 0) + (bloodWaifu?.Count ?? 0);
                if (totalBlood < bloodNeeded)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie posiadasz wystarczającej liczby kropel krwi. ({bloodNeeded})".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var toRemoveYour = ((bloodYour?.Count ?? 0) -  bloodNeeded) >= 0 ? bloodNeeded : (bloodYour?.Count ?? 0);
                if (toRemoveYour > 0)
                {
                    bloodYour.Count -= toRemoveYour;
                    if (bloodYour.Count <= 0)
                        bUser.GameDeck.Items.Remove(bloodYour);

                    bloodNeeded -= toRemoveYour;
                }

                var toRemoveWaifu = ((bloodWaifu?.Count ?? 0) -  bloodNeeded) >= 0 ? bloodNeeded : (bloodWaifu?.Count ?? 0);
                if (toRemoveWaifu > 0)
                {
                    bloodWaifu.Count -= toRemoveWaifu;
                    if (bloodWaifu.Count <= 0)
                        bUser.GameDeck.Items.Remove(bloodWaifu);
                }

                for (int i = 0; i < cardNeeded; i++)
                    bUser.GameDeck.Cards.Remove(cardsToSac[i]);

                ++bUser.GameDeck.ExpContainer.Level;
                bUser.GameDeck.Karma -= 15;

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"user-{bUser.Id}", "users" });

                string action = bUser.GameDeck.ExpContainer.Level == ExpContainerLevel.Level1 ? "otrzymałeś" : "ulepszyłeś";
                await ReplyAsync("", embed: $"{Context.User.Mention} {action} skrzynię doświadczenia.".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("lazyc")]
        [Alias("lc")]
        [Summary("dostajesz jedną darmową kart z domyślnie ustawionym niszczeniem kc na 3 oraz tagiem wymiana")]
        [Remarks("3 nie Wymiana Ulubione"), RequireAnyCommandChannelLevelOrNitro(40)]
        public async Task GetLazyFreeCardAsync([Summary("czy zniszczyć karty nie będące na liście życzeń i nie posiadające danej kc?")] uint destroyCards = 3,
            [Summary("czy zamienić niszczenie na uwalnianie?")] bool changeToRelease = false, [Summary("oznacz niezniszczone karty")] string tag = "wymiana", [Summary("oznacz karty z wishlisty")] string tagWishlist = "ulubione")
                => await GetFreeCardAsync(destroyCards, changeToRelease, tag, tagWishlist);

        [Command("karta+")]
        [Alias("free card")]
        [Summary("dostajesz jedną darmową kartę")]
        [Remarks("3 nie Wymiana Ulubione"), RequireAnyCommandChannelLevelOrNitro(40)]
        public async Task GetFreeCardAsync([Summary("czy zniszczyć karty nie będące na liście życzeń i nie posiadające danej kc?")] uint destroyCards = 0,
            [Summary("czy zamienić niszczenie na uwalnianie?")] bool changeToRelease = false, [Summary("oznacz niezniszczone karty")] string tag = "", [Summary("oznacz karty z wishlisty")] string tagWishlist = "")
        {
            if (!string.IsNullOrEmpty(tag) && tag.Contains(" "))
            {
                await ReplyAsync("", embed: $"{Context.User.Mention} oznaczenie nie może zawierać spacji.".ToEmbedMessage(EMType.Error).Build());
                return;
            }

            if (!string.IsNullOrEmpty(tagWishlist) && tagWishlist.Contains(" "))
            {
                await ReplyAsync("", embed: $"{Context.User.Mention} oznaczenie nie może zawierać spacji.".ToEmbedMessage(EMType.Error).Build());
                return;
            }

            using (var db = new Database.DatabaseContext(Config))
            {
                var botuser = await db.GetUserOrCreateAsync(Context.User.Id);
                var freeCard = botuser.TimeStatuses.FirstOrDefault(x => x.Type == StatusType.Card);
                if (freeCard == null)
                {
                    freeCard = StatusType.Card.NewTimeStatus();
                    botuser.TimeStatuses.Add(freeCard);
                }

                var ultimateCards = botuser.GameDeck.Cards.Where(x => x.FromFigure).ToList();
                var cnt = ultimateCards.Count;
                if (cnt > 5) cnt = 5;

                cnt += ultimateCards.Any(x => x.Quality > Quality.Alpha) ? 1 : 0;
                cnt += ultimateCards.Any(x => x.Quality > Quality.Gamma) ? 2 : 0;
                cnt += ultimateCards.Any(x => x.Quality > Quality.Zeta) ? 3 : 0;
                cnt += ultimateCards.Any(x => x.Quality > Quality.Lambda) ? 5 : 0;
                if (cnt > 12) cnt = 12;

                var ns = freeCard.Sub(TimeSpan.FromHours(cnt));

                if (ns.IsActive(_time.Now()))
                {
                    var timeTo = (int)ns.RemainingMinutes(_time.Now());
                    await ReplyAsync("", embed: $"{Context.User.Mention} możesz otrzymać następną darmową kartę dopiero za {timeTo / 60}h {timeTo % 60}m!".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (botuser.GameDeck.Cards.Count + 1 > botuser.GameDeck.MaxNumberOfCards)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie masz już miejsca na kolejną kartę!".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var mission = botuser.TimeStatuses.FirstOrDefault(x => x.Type == Database.Models.StatusType.WCardPlus);
                if (mission == null)
                {
                    mission = StatusType.WCardPlus.NewTimeStatus();
                    botuser.TimeStatuses.Add(mission);
                }
                mission.Count(_time.Now());

                freeCard.EndsAt = _time.Now().AddHours(22);

                var character = await _waifu.GetRandomCharacterAsync(botuser.PoolType);
                if (character == null)
                {
                    await ReplyAsync("", embed: "Brak połączania z Shindenem!".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var card = _waifu.GenerateNewCard(Context.User, character,
                    new List<Rarity>() { Rarity.SS, Rarity.S, Rarity.A });

                bool isOnUserWishlist = await botuser.GameDeck.RemoveCharacterFromWishListAsync(card.Character, db);
                card.Affection += botuser.GameDeck.AffectionFromKarma();
                card.Source = CardSource.Daily;

                var wishlists = db.GameDecks.Include(x => x.Wishes).AsNoTracking().Where(x => !x.WishlistIsPrivate && x.Wishes.Any(c => c.Type == WishlistObjectType.Character && c.ObjectId == card.Character)).ToList();
                card.WhoWantsCount = wishlists.Count;

                if (destroyCards > 0 && card.WhoWantsCount < destroyCards && !isOnUserWishlist)
                {
                    card.DestroyOrRelease(botuser, changeToRelease);
                }
                else
                {
                    if (!string.IsNullOrEmpty(tag) && !isOnUserWishlist)
                    {
                        var btag = await db.GetTagAsync(_tags, tag, Context.User.Id);
                        if (btag != null) card.Tags.Add(btag);
                    }

                    if (!string.IsNullOrEmpty(tagWishlist) && isOnUserWishlist)
                    {
                        var btag = await db.GetTagAsync(_tags, tagWishlist, Context.User.Id);
                        if (btag != null) card.Tags.Add(btag);
                    }

                    botuser.GameDeck.Cards.Add(card);
                }

                await db.SaveChangesAsync();

                bool cardDestroyed = card.WhoWantsCount < destroyCards && !isOnUserWishlist && destroyCards > 0;
                string wishStr = cardDestroyed ? "🖤 " : card.ToHeartWishlist(isOnUserWishlist);

                if (db.AddActivityFromNewCard(card, isOnUserWishlist, _time, botuser, Context.User.GetUserNickInGuild()))
                {
                    await db.SaveChangesAsync();
                }

                QueryCacheManager.ExpireTag(new string[] { $"user-{botuser.Id}", "users" });

                await ReplyAsync("", embed: $"{Context.User.Mention} otrzymałeś {wishStr}{card.GetString(false, false, true)}".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("rynek")]
        [Alias("market")]
        [Summary("udajesz się na rynek z wybraną przez Ciebie kartą, aby pohandlować")]
        [Remarks("2145"), RequireWaifuCommandChannel]
        public async Task GoToMarketAsync([Summary("WID")] ulong wid)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var botuser = await db.GetUserOrCreateAsync(Context.User.Id);
                if (botuser.GameDeck.IsMarketDisabled())
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} wszyscy na twój widok się rozbiegli, nic dziś nie zdziałasz.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var card = botuser.GameDeck.Cards.FirstOrDefault(x => x.Id == wid);
                if (card == null)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie posiadasz takiej karty.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (card.FromFigure)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} z tą kartą nie można iść na rynek.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (card.Expedition != CardExpedition.None)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} ta karta jest na wyprawie!".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (card.IsUnusable())
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} ktoś kto Cię nienawidzi, nie pomoże Ci w niczym.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var market = botuser.TimeStatuses.FirstOrDefault(x => x.Type == StatusType.Market);
                if (market == null)
                {
                    market = StatusType.Market.NewTimeStatus();
                    botuser.TimeStatuses.Add(market);
                }

                if (market.IsActive(_time.Now()))
                {
                    var timeTo = (int)market.RemainingMinutes(_time.Now());
                    await ReplyAsync("", embed: $"{Context.User.Mention} możesz udać się ponownie na rynek za {timeTo / 60}h {timeTo % 60}m!".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var mission = botuser.TimeStatuses.FirstOrDefault(x => x.Type == Database.Models.StatusType.DMarket);
                if (mission == null)
                {
                    mission = Database.Models.StatusType.DMarket.NewTimeStatus();
                    botuser.TimeStatuses.Add(mission);
                }
                mission.Count(_time.Now());

                int nextMarket = 20 - (int)(botuser.GameDeck.Karma / 100);
                if (nextMarket > 22) nextMarket = 22;
                if (nextMarket < 4) nextMarket = 4;

                if (botuser.GameDeck.Karma >= 3000)
                {
                    int tK = (int)(botuser.GameDeck.Karma - 2000) / 1000;
                    nextMarket -= tK;

                    if (nextMarket < 1)
                        nextMarket = 1;
                }

                int itemCnt = 1 + (int)(card.Affection / 15);
                itemCnt += (int)(botuser.GameDeck.Karma / 180);
                if (itemCnt > 10) itemCnt = 10;
                if (itemCnt < 1) itemCnt = 1;

                if (card.CanGiveRing()) ++itemCnt;
                if (botuser.GameDeck.CanCreateAngel()) ++itemCnt;

                market.EndsAt = _time.Now().AddHours(nextMarket);
                card.Affection += 0.1;

                _ = card.CalculateCardPower();

                string reward = "";
                for (int i = 0; i < itemCnt; i++)
                {
                    var itmType = _waifu.RandomizeItemFromMarket();
                    var item = itmType.ToItem(1);
                    botuser.GameDeck.AddItem(item);

                    reward += $"+{item.Name}\n";
                }

                if (Services.Fun.TakeATry(30d))
                {
                    botuser.GameDeck.CTCnt += 1;
                    reward += "\nUps, twoja waifu się potknęła a Ty się jeszcze z tego cieszysz. (+1CT)\n";

                    if (Services.Fun.TakeATry(20d))
                    {
                        var bitem = ItemType.BloodOfYourWaifu.ToItem();
                        botuser.GameDeck.AddItem(bitem);

                        reward += $"+{bitem.Name}";
                    }
                }

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"user-{botuser.Id}", "users" });

                await ReplyAsync("", embed: $"{Context.User.Mention} udało Ci się zdobyć:\n\n{reward}".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("czarny rynek")]
        [Alias("black market")]
        [Summary("udajesz się na czarny rynek z wybraną przez Ciebie kartą, wolałbym nie wiedzieć co tam będziesz robić")]
        [Remarks("2145"), RequireWaifuCommandChannel]
        public async Task GoToBlackMarketAsync([Summary("WID")] ulong wid)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var botuser = await db.GetUserOrCreateAsync(Context.User.Id);
                if (botuser.GameDeck.IsBlackMarketDisabled())
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} halo koleżko, to nie miejsce dla Ciebie!".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var card = botuser.GameDeck.Cards.FirstOrDefault(x => x.Id == wid);
                if (card == null)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie posiadasz takiej karty.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (card.FromFigure)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} z tą kartą nie można iść na czarny rynek.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (card.Expedition != CardExpedition.None)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} ta karta jest na wyprawie!".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var market = botuser.TimeStatuses.FirstOrDefault(x => x.Type == StatusType.Market);
                if (market == null)
                {
                    market = StatusType.Market.NewTimeStatus();
                    botuser.TimeStatuses.Add(market);
                }

                if (market.IsActive(_time.Now()))
                {
                    var timeTo = (int)market.RemainingMinutes(_time.Now());
                    await ReplyAsync("", embed: $"{Context.User.Mention} możesz udać się ponownie na czarny rynek za {timeTo / 60}h {timeTo % 60}m!".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var mission = botuser.TimeStatuses.FirstOrDefault(x => x.Type == Database.Models.StatusType.DMarket);
                if (mission == null)
                {
                    mission = Database.Models.StatusType.DMarket.NewTimeStatus();
                    botuser.TimeStatuses.Add(mission);
                }
                mission.Count(_time.Now());

                int nextMarket = 20 + (int)(botuser.GameDeck.Karma / 100);
                if (nextMarket > 22) nextMarket = 22;
                if (nextMarket < 4) nextMarket = 4;

                if (botuser.GameDeck.Karma <= -3000)
                {
                    int tK = (int)(botuser.GameDeck.Karma + 2000) / 1000;
                    nextMarket += tK;

                    if (nextMarket < 1)
                        nextMarket = 1;
                }

                int itemCnt = 1 + (int)(card.Affection / 15);
                itemCnt -= (int)(botuser.GameDeck.Karma / 180);
                if (itemCnt > 10) itemCnt = 10;
                if (itemCnt < 1) itemCnt = 1;

                if (card.CanGiveBloodOrUpgradeToSSS()) ++itemCnt;
                if (botuser.GameDeck.CanCreateDemon()) ++itemCnt;

                market.EndsAt = _time.Now().AddHours(nextMarket);
                card.Affection -= 0.2;

                _ = card.CalculateCardPower();

                string reward = "";
                for (int i = 0; i < itemCnt; i++)
                {
                    var itmType = _waifu.RandomizeItemFromBlackMarket();
                    var item = itmType.ToItem(1);
                    botuser.GameDeck.AddItem(item);

                    reward += $"+{item.Name}\n";
                }

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"user-{botuser.Id}", "users" });

                await ReplyAsync("", embed: $"{Context.User.Mention} udało Ci się zdobyć:\n\n{reward}".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("poświęć")]
        [Alias("sacrifice", "poswiec", "poświec", "poświeć", "poswięć", "poswieć")]
        [Summary("dodaje exp do karty, poświęcając kilka innych")]
        [Remarks("5412 5411 5410"), RequireWaifuCommandChannel]
        public async Task SacraficeCardMultiAsync([Summary("WID (do ulepszenia)")] ulong idToUp, [Summary("WIDs (do poświęcenia)")] params ulong[] idsToSac)
        {
            if (idsToSac.Any(x => x == idToUp))
            {
                await ReplyAsync("", embed: $"{Context.User.Mention} podałeś ten sam WID do ulepszenia i zniszczenia.".ToEmbedMessage(EMType.Error).Build());
                return;
            }

            using (var db = new Database.DatabaseContext(Config))
            {
                var bUser = await db.GetUserOrCreateAsync(Context.User.Id);

                var cardToUp = bUser.GetCard(idToUp);
                var cardsToSac = bUser.GetCards(idsToSac).ToList();

                if (cardsToSac.Count < 1 || cardToUp == null)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie posiadasz takiej karty.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (cardToUp.InCage)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} ta karta znajduje się w klatce.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (cardToUp.Expedition != CardExpedition.None)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} ta karta jest na wyprawie!".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                double totalExp = 0;
                var broken = new List<Card>();
                foreach (var card in cardsToSac)
                {
                    if (card.IsProtectedFromDiscarding(_tags))
                    {
                        broken.Add(card);
                        continue;
                    }

                    ++bUser.Stats.SacraficeCards;
                    bUser.GameDeck.Karma -= 0.28;

                    var exp = _waifu.GetExpToUpgrade(cardToUp, card);
                    cardToUp.Affection += 0.07;
                    cardToUp.ExpCnt += exp;
                    totalExp += exp;

                    bUser.GameDeck.Cards.Remove(card);
                    _waifu.DeleteCardImageIfExist(card);
                }

                _ = cardToUp.CalculateCardPower();

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"user-{bUser.Id}", "users" });

                if (cardsToSac.Count > broken.Count)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} ulepszył kartę: {cardToUp.GetString(false, false, true)} o {totalExp:F} exp.".ToEmbedMessage(EMType.Success).Build());
                }

                if (broken.Count > 0)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie udało się poświęcić {broken.Count} kart.".ToEmbedMessage(EMType.Error).Build());
                }
            }
        }

        [Command("klatka")]
        [Alias("cage")]
        [Summary("otwiera klatkę z kartami (sprecyzowanie wid wyciąga tylko jedną kartę)")]
        [Remarks(""), RequireWaifuCommandChannel]
        public async Task OpenCageAsync([Summary("WID")] ulong wid = 0)
        {
            var user = Context.User as SocketGuildUser;
            if (user == null) return;

            using (var db = new Database.DatabaseContext(Config))
            {
                var bUser = await db.GetUserOrCreateAsync(user.Id);
                var cardsInCage = bUser.GameDeck.Cards.Where(x => x.InCage);

                var cntIn = cardsInCage.Count();
                if (cntIn < 1)
                {
                    await ReplyAsync("", embed: $"{user.Mention} nie posiadasz kart w klatce.".ToEmbedMessage(EMType.Info).Build());
                    return;
                }

                if (wid == 0)
                {
                    bUser.GameDeck.Karma += 0.01;

                    foreach (var card in cardsInCage)
                    {
                        card.InCage = false;
                        var charInfo = await _shinden.GetCharacterInfoAsync(card.Character);
                        if (charInfo != null)
                        {
                            if (charInfo?.Points != null)
                            {
                                if (charInfo.Points.Any(x => x.Name.Equals((user.Nickname ?? user.GlobalName) ?? user.Username)))
                                    card.Affection += 0.8;
                            }
                        }

                        var span = _time.Now() - card.CreationDate;
                        if (span.TotalDays > 5) card.Affection -= (int)span.TotalDays * 0.1;

                        _ = card.CalculateCardPower();
                    }
                }
                else
                {
                    var thisCard = cardsInCage.FirstOrDefault(x => x.Id == wid);
                    if (thisCard == null)
                    {
                        await ReplyAsync("", embed: $"{user.Mention} taka karta nie znajduje się w twojej klatce.".ToEmbedMessage(EMType.Error).Build());
                        return;
                    }

                    bUser.GameDeck.Karma -= 0.1;
                    thisCard.InCage = false;
                    cntIn = 1;

                    var span = _time.Now() - thisCard.CreationDate;
                    if (span.TotalDays > 5) thisCard.Affection -= (int)span.TotalDays * 0.1;

                    _ = thisCard.CalculateCardPower();

                    foreach (var card in cardsInCage)
                    {
                        if (card.Id != thisCard.Id)
                            card.Affection -= 0.3;

                        _ = card.CalculateCardPower();
                    }
                }

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"user-{bUser.Id}", "users" });

                await ReplyAsync("", embed: $"{user.Mention} wyciągnął {cntIn} kart z klatki.".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("żusuń")]
        [Alias("wremove", "zusuń", "żusun", "zusun")]
        [Summary("usuwa karty/tytuły/postacie z listy życzeń")]
        [Remarks("karta 4212 21452"), RequireWaifuCommandChannel]
        public async Task RemoveFromWishlistAsync([Summary("typ (p - postać, t - tytuł, c - karta)")] WishlistObjectType type, [Summary("IDs/WIDs")] params ulong[] ids)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var bUser = await db.GetUserOrCreateSimpleAsync(Context.User.Id);
                var objs = bUser.GameDeck.Wishes.Where(x => x.Type == type && ids.Any(c => c == x.ObjectId)).ToList();
                if (objs.Count < 1)
                {
                    await ReplyAsync("", embed: "Nie posiadasz takich pozycji na liście życzeń!".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                foreach (var obj in objs)
                {
                    await db.CreateOrChangeWishlistCountByAsync(obj.ObjectId, obj.ObjectName, -1);
                    bUser.GameDeck.Wishes.Remove(obj);
                }

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"user-{bUser.Id}", "users" });

                await ReplyAsync("", embed: $"{Context.User.Mention} usunął pozycję z listy życzeń.".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("żdodaj")]
        [Alias("wadd", "zdodaj")]
        [Summary("dodaje kartę/tytuł/postać do listy życzeń")]
        [Remarks("karta 4212 tak"), RequireWaifuCommandChannel]
        public async Task AddToWishlistAsync([Summary("typ (p - postać, t - tytuł)")] WishlistObjectType type, [Summary("ID/WID")] ulong id, [Summary("czy wpis ma zostać na liście po otrzymaniu karty?")]bool isStatic = false)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                string response = "";
                var bUser = await db.GetUserOrCreateSimpleAsync(Context.User.Id);
                if (bUser.GameDeck.Wishes.Any(x => x.Type == type && x.ObjectId == id))
                {
                    await ReplyAsync("", embed: "Już posiadasz taki wpis w liście życzeń!".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var obj = new WishlistObject
                {
                    Entry = isStatic ? WishlistEntryType.Persistent : WishlistEntryType.Normal,
                    ObjectId = id,
                    Type = type
                };

                switch (type)
                {
                    case WishlistObjectType.Card:
                        await ReplyAsync("", embed: $"{Context.User.Mention} dodawanie kart do listy życzeń nie jest już wspierane!".ToEmbedMessage(EMType.Error).Build());
                        return;

                    case WishlistObjectType.Title:
                        var res1 = await _shclient.Title.GetInfoAsync(id);
                        if (!res1.IsSuccessStatusCode())
                        {
                            await ReplyAsync("", embed: $"Nie odnaleziono serii!".ToEmbedMessage(EMType.Error).Build());
                            return;
                        }
                        response = res1.Body.Title;
                        obj.ObjectName = res1.Body.Title;
                        if (!bUser.GameDeck.WishlistIsPrivate)
                        {
                            await db.UserActivities.AddAsync(new Services.UserActivityBuilder(_time).AddMisc($"t:{res1.Body.Title}")
                               .WithUser(bUser, Context.User).WithType(Database.Models.ActivityType.AddedToWishlistTitle, id).Build());
                        }
                        break;

                    case WishlistObjectType.Character:
                        var charInfo = await _shinden.GetCharacterInfoAsync(id);
                        if (charInfo == null)
                        {
                            await ReplyAsync("", embed: $"Nie odnaleziono postaci!".ToEmbedMessage(EMType.Error).Build());
                            return;
                        }
                        response = charInfo.ToString();
                        obj.ObjectName = response;
                        await db.CreateOrChangeWishlistCountByAsync(obj.ObjectId, obj.ObjectName);
                        if (!bUser.GameDeck.WishlistIsPrivate)
                        {
                            await db.UserActivities.AddAsync(new Services.UserActivityBuilder(_time).AddMisc($"c:{response.ToString().Trim()}")
                               .WithUser(bUser, Context.User).WithType(Database.Models.ActivityType.AddedToWishlistCharacter, id).Build());
                        }
                        break;
                }

                bUser.GameDeck.Wishes.Add(obj);

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"user-{bUser.Id}", "users" });

                await ReplyAsync("", embed: $"{Context.User.Mention} dodał do listy życzeń: {response}".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("życzenia widok")]
        [Alias("wishlist view", "zyczenia widok")]
        [Summary("pozwala ukryć listę życzeń przed innymi graczami")]
        [Remarks("tak"), RequireWaifuCommandChannel]
        public async Task SetWishlistViewAsync([Summary("czy ma być widoczna? (tak/nie)")] bool view)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var bUser = await db.GetUserOrCreateAsync(Context.User.Id);
                bUser.GameDeck.WishlistIsPrivate = !view;

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"user-{bUser.Id}", "users" });

                string response = (!view) ? $"ukrył" : $"udostępnił";
                await ReplyAsync("", embed: $"{Context.User.Mention} {response} swoją listę życzeń!".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("na życzeniach", RunMode = RunMode.Async)]
        [Alias("on wishlist", "na zyczeniach", "onwl")]
        [Summary("wyświetla obiekty dodane do listy życzeń")]
        [Remarks("Karna"), RequireWaifuCommandChannel]
        public async Task ShowThingsOnWishlistAsync([Summary("nazwa użytkownika")] SocketGuildUser usr = null, [Summary("czy wysłać jako plik tekstowy?")] bool tldr = false)
        {
            var user = (usr ?? Context.User) as SocketGuildUser;
            if (user == null) return;

            using (var db = new Database.DatabaseContext(Config))
            {
                var bUser = await db.GetCachedFullUserAsync(user.Id);
                var res = await _waifu.CheckWishlistAndSendToDMAsync(db, Context.User, bUser, showContentOnly: true, tldr: tldr);

                await ReplyAsync("", embed: res.ToEmbedMessage($"{Context.User.Mention} ").Build());
            }
        }

        [Command("życzenia", RunMode = RunMode.Async)]
        [Alias("wishlist", "zyczenia", "wl")]
        [Summary("wyświetla liste życzeń użytkownika")]
        [Remarks("Dzida tak tak tak tak nie"), RequireWaifuCommandChannel]
        public async Task ShowWishlistAsync([Summary("nazwa użytkownika")] SocketGuildUser usr = null,
            [Summary("czy pokazać ulubione, domyślnie ukryte, wymaga podania użytkownika? (true/false)")] bool showFavs = false,
            [Summary("czy pokazać niewymienialne, domyślnie pokazane? (true/false)")] bool showBlocked = true,
            [Summary("czy zamienić oznaczenia na nicki?")] bool showNames = false,
            [Summary("czy dodać linki do profili?")] bool showShindenUrl = false,
            [Summary("czy ignorować anime?")] bool ignoreTitles = false,
            [Summary("czy wysłać jako plik tekstowy?")] bool tldr = false)
        {
            var user = (usr ?? Context.User) as SocketGuildUser;
            if (user == null) return;

            using (var db = new Database.DatabaseContext(Config))
            {
                var bUser = await db.GetCachedFullUserAsync(user.Id);
                var res = await _waifu.CheckWishlistAndSendToDMAsync(db, Context.User, bUser, !showFavs,
                    !showBlocked, !showNames, showShindenUrl, Context.Guild, false, 0, ignoreTitles, tldr);

                await ReplyAsync("", embed: res.ToEmbedMessage($"{Context.User.Mention} ").Build());
            }
        }

        [Command("życzenia filtr", RunMode = RunMode.Async)]
        [Alias("wishlistf", "zyczeniaf", "wlf")]
        [Summary("wyświetla pozycje z listy życzeń użytkownika zawierające tylko drugiego użytkownika")]
        [Remarks("Dzida Kokos tak tak tak tak nie"), RequireWaifuCommandChannel]
        public async Task ShowFilteredWishlistAsync([Summary("użytkownik do którego należy lista życzeń")] SocketGuildUser user,
            [Summary("użytkownik po którym odbywa się filtracja")] SocketGuildUser usrf = null,
            [Summary("czy pokazać ulubione, domyślnie ukryte, wymaga podania użytkownika? (true/false)")] bool showFavs = false,
            [Summary("czy pokazać niewymienialne, domyślnie pokazane? (true/false)")] bool showBlocked = true,
            [Summary("czy zamienić oznaczenia na nicki?")] bool showNames = false,
            [Summary("czy dodać linki do profili?")] bool showShindenUrl = false,
            [Summary("czy ignorować anime?")] bool ignoreTitles = false,
            [Summary("czy wysłać jako plik tekstowy?")] bool tldr = false)
        {
            var userf = (usrf ?? Context.User) as SocketGuildUser;
            if (userf == null) return;

            if (user.Id == userf.Id)
            {
                await ReplyAsync("", embed: $"{Context.User.Mention} podałeś dwa razy tego samego użytkownika.".ToEmbedMessage(EMType.Error).Build());
                return;
            }

            using (var db = new Database.DatabaseContext(Config))
            {
                var bUser = await db.GetCachedFullUserAsync(user.Id);
                ulong searchId = userf.Id == Context.Client.CurrentUser.Id ? 1 : userf.Id;
                var res = await _waifu.CheckWishlistAndSendToDMAsync(db, Context.User, bUser, !showFavs,
                    !showBlocked, !showNames, showShindenUrl, Context.Guild, false, searchId, ignoreTitles, tldr);

                await ReplyAsync("", embed: res.ToEmbedMessage($"{Context.User.Mention} ").Build());
            }
        }

        [Command("kto chce", RunMode = RunMode.Async)]
        [Alias("who wants", "kc", "ww")]
        [Summary("wyszukuje na listach życzeń użytkowników danej karty, pomija tytuły")]
        [Remarks("51545 tak"), RequireWaifuCommandChannel]
        public async Task WhoWantsCardAsync([Summary("WID")] ulong wid, [Summary("czy zamienić oznaczenia na nicki?")] bool showNames = false)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var thisCards = await db.Cards.AsNoTracking().Include(x => x.Tags).FirstOrDefaultAsync(x => x.Id == wid);
                if (thisCards == null)
                {
                    await ReplyAsync("", embed: $"Nie odnaleziono karty.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var wishlists =  await db.GameDecks.AsQueryable().AsNoTracking().Where(x => !x.WishlistIsPrivate).Include(x => x.Wishes).Include(x => x.User).Where(x => x.Wishes.Any(c => c.Type == WishlistObjectType.Character && c.ObjectId == thisCards.Character)).ToListAsync();
                if (wishlists.Count < 1)
                {
                    await ReplyAsync("", embed: $"Nikt nie chce tej karty.".ToEmbedMessage(EMType.Error).Build());
                }
                else
                {
                    var usersStr = await _waifu.GetWhoWantsCardsStringAsync(wishlists, showNames, Context.Guild);
                    if (usersStr.Count > 50)
                    {
                        try
                        {
                            var msgs = usersStr.SplitList();
                            var dm = await Context.User.CreateDMChannelAsync();
                            for (int i = 0; i < msgs.Count; i++)
                            {
                                var mes = $"**[{i + 1}/{msgs.Count}]:**\n\n{string.Join('\n', msgs[i])}";
                                if (i == 0) mes = $"**{thisCards.GetNameWithUrl()} chcą ({usersStr.Count})** {mes}";
                                await dm.SendMessageAsync("", embed: mes.ToEmbedMessage(EMType.Info).Build());
                                await Task.Delay(TimeSpan.FromSeconds(2));
                            }
                            await ReplyAsync("", embed: $"{Context.User.Mention} lista poszła na PW!".ToEmbedMessage(EMType.Success).Build());
                        }
                        catch (Exception)
                        {
                            await ReplyAsync("", embed: $"{Context.User.Mention} nie można wysłać do Ciebie PW!".ToEmbedMessage(EMType.Error).Build());
                        }
                    }
                    else
                    {
                        await ReplyAsync("", embed: $"**{thisCards.GetNameWithUrl()} chcą ({usersStr.Count}):**\n\n {string.Join('\n', usersStr)}".TrimToLength().ToEmbedMessage(EMType.Info).Build());
                    }
                }

                var exe = new Executable($"kc-check-{thisCards.Character}", new Func<Task>(async () =>
                {
                    using (var dbs = new Database.DatabaseContext(_config))
                    {
                        var wCount = await dbs.GameDecks.Include(x => x.Wishes).AsNoTracking().Where(x => !x.WishlistIsPrivate && x.Wishes.Any(c => c.Type == WishlistObjectType.Character && c.ObjectId == thisCards.Character)).CountAsync();
                        var ww = await dbs.CreateOrChangeWishlistCountByAsync(thisCards.Character, thisCards.Name, wCount, true);
                        if (ww)
                        {
                            var cds = await dbs.Cards.AsQueryable().Where(x => x.Character == thisCards.Character && x.WhoWantsCount != wCount).ToListAsync();
                            foreach (var c in cds) c.WhoWantsCount = wCount;
                        }
                        await dbs.SaveChangesAsync();
                    }
                }));
                await _executor.TryAdd(exe, TimeSpan.FromSeconds(1));
            }
        }

        [Command("napraw tytuł")]
        [Alias("fix title", "napraw tytul")]
        [Summary("zmienia tytuł na karcie")]
        [Remarks("5412 Słabe ssało"), RequireWaifuCommandChannel]
        public async Task UpdateCardTitleLoosyAsync([Summary("WID")] ulong id, [Summary("tytuł lub jego część")][Remainder] string title)
            => await UpdateCardTitleAsync(false, id, title);

        [Command("napraw tytuł")]
        [Alias("fix title", "napraw tytul")]
        [Summary("zmienia tytuł na karcie")]
        [Remarks("5412 Słabe ssało"), RequireWaifuCommandChannel]
        public async Task UpdateCardTitleAsync([Summary("czy wymusić?")]bool force, [Summary("WID")] ulong id, [Summary("tytuł lub jego część")][Remainder] string title)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var card = await db.Cards.AsQueryable().Where(x => x.GameDeckId == Context.User.Id).FirstOrDefaultAsync(x => x.Id == id);
                if (card == null)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie posiadasz takiej karty.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var response = await _shclient.GetCharacterInfoAsync(card.Character);
                if (!response.IsSuccessStatusCode() || response?.Body?.Relations?.Count == 0)
                {
                    if (response.Code == System.Net.HttpStatusCode.NotFound)
                        card.Unique = true;
                }
                else
                {
                    var nTitle = response.Body.Relations.FirstOrDefault(x => x.Title.Contains(title))?.Title ?? "";
                    if (force) nTitle = response.Body.Relations.FirstOrDefault(x => x.Title.Equals(title))?.Title ?? "";
                    if (string.IsNullOrEmpty(nTitle))
                    {
                        await ReplyAsync("", embed: $"{Context.User.Mention} nie odnaleziono takiego tytułu w powiązaniach postaci.".ToEmbedMessage(EMType.Error).Build());
                        return;
                    }
                    title = nTitle;
                }

                card.Title = title;
                await db.SaveChangesAsync();

                await ReplyAsync("", embed: $"{Context.User.Mention} ustawiono tytuł na: {title}.".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("kto chce anime", RunMode = RunMode.Async)]
        [Alias("who wants anime", "kca", "wwa")]
        [Summary("wyszukuje na wishlistach danego anime")]
        [Remarks("21 tak"), RequireWaifuCommandChannel]
        public async Task WhoWantsCardsFromAnimeAsync([Summary("id anime")] ulong id, [Summary("czy zamienić oznaczenia na nicki?")] bool showNames = false)
        {
            var response = await _shclient.Title.GetInfoAsync(id);
            if (!response.IsSuccessStatusCode())
            {
                await ReplyAsync("", embed: $"Nie odnaleziono tytułu!".ToEmbedMessage(EMType.Error).Build());
                return;
            }

            using (var db = new Database.DatabaseContext(Config))
            {
                var wishlists = db.GameDecks.Include(x => x.Wishes).Include(x => x.User).Where(x => !x.WishlistIsPrivate && x.Wishes.Any(c => c.Type == WishlistObjectType.Title && c.ObjectId == id)).ToList();
                if (wishlists.Count < 1)
                {
                    await ReplyAsync("", embed: $"Nikt nie ma tego tytułu wpisanego na listę życzeń.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var usersStr = await _waifu.GetWhoWantsCardsStringAsync(wishlists, showNames, Context.Guild);
                await ReplyAsync("", embed: $"**Karty z {response.Body.Title} chcą:**\n\n {string.Join('\n', usersStr)}".TrimToLength().ToEmbedMessage(EMType.Info).Build());
            }
        }

        [Command("wyzwól")]
        [Alias("unleash", "wyzwol")]
        [Summary("zmienia kartę niewymienialną na wymienialną (200/150 CT lub 2000 CT w przypadku ultimate)")]
        [Remarks("8651"), RequireWaifuCommandChannel]
        public async Task UnleashCardAsync([Summary("WID")] ulong wid)
        {
            int cost = 200;
            using (var db = new Database.DatabaseContext(Config))
            {
                var bUser = await db.GetUserOrCreateAsync(Context.User.Id);
                var thisCard = bUser.GameDeck.Cards.FirstOrDefault(x => x.Id == wid);

                if (thisCard == null)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie odnaleziono karty.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (thisCard.IsTradable)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} ta karta jest wymienialna.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (thisCard.Expedition != CardExpedition.None)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} ta karta jest na wyprawie!".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (bUser.GameDeck.CanCreateAngel() || bUser.GameDeck.CanCreateDemon()) cost = 150;
                if (thisCard.FromFigure) cost = 2000;

                if (bUser.GameDeck.CTCnt < cost)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie masz wystarczającej liczby CT.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                bUser.Stats.UnleashedCards += 1;
                bUser.GameDeck.CTCnt -= cost;
                bUser.GameDeck.Karma += 2;
                thisCard.IsTradable = true;

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"user-{bUser.Id}", "users" });

                await ReplyAsync("", embed: $"{Context.User.Mention} wyzwolił kartę {thisCard.GetString(false, false, true)}".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("limit kart")]
        [Alias("card limit")]
        [Summary("zwiększa limit kart, jakie można posiadać o 100, podanie 0 jako krotności wypisuje obecny limit")]
        [Remarks("10"), RequireWaifuCommandChannel]
        public async Task IncCardLimitAsync([Summary("krotność użycia polecenia")] uint count = 0)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var bUser = await db.GetUserOrCreateSimpleAsync(Context.User.Id);
                if (count < 1)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} obecny limit to: {bUser.GameDeck.MaxNumberOfCards}".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (count > 20)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} jednorazowo można zwiększyć limit tylko o 2000.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                long cost = bUser.GameDeck.CalculatePriceOfIncMaxCardCount(count);
                if (bUser.TcCnt < cost)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie masz wystarczającej liczby TC, aby zwiększyć limit o {100 * count} potrzebujesz {cost} TC.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                bUser.TcCnt -= cost;
                bUser.GameDeck.MaxNumberOfCards += 100 * count;

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"user-{bUser.Id}", "users" });

                await ReplyAsync("", embed: $"{Context.User.Mention} powiększył swój limit kart do {bUser.GameDeck.MaxNumberOfCards}.".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("kolor strony")]
        [Alias("site color")]
        [Summary("zmienia kolor przewodni profilu na stronie waifu (500 TC)")]
        [Remarks("#dc5341"), RequireWaifuCommandChannel]
        public async Task ChangeWaifuSiteForegroundColorAsync([Summary("kolor w formacie hex")] string color)
        {
            var tcCost = 500;

            using (var db = new Database.DatabaseContext(Config))
            {
                var botuser = await db.GetUserOrCreateSimpleAsync(Context.User.Id);
                if (botuser.TcCnt < tcCost)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie posiadasz wystarczającej liczby TC!".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (!color.IsAColorInHEX())
                {
                    await ReplyAsync("", embed: "Nie wykryto koloru! Upewnij się, że podałeś poprawny kod HEX!".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                botuser.TcCnt -= tcCost;
                botuser.GameDeck.ForegroundColor = color;

                await db.SaveChangesAsync();

                await ReplyAsync("", embed: $"Zmieniono kolor na stronie waifu użytkownika: {Context.User.Mention}!".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("szczegół strony")]
        [Alias("szczegoł strony", "szczegol strony", "szczegól strony", "site fg", "site foreground")]
        [Summary("zmienia obrazek nakładany na tło profilu na stronie waifu (500 TC)")]
        [Remarks("https://sanakan.pl/i/example_foreground.png"), RequireWaifuCommandChannel]
        public async Task ChangeWaifuSiteForegroundAsync([Summary("bezpośredni adres do obrazka")] string imgUrl)
        {
            var tcCost = 500;

            using (var db = new Database.DatabaseContext(Config))
            {
                var botuser = await db.GetUserOrCreateSimpleAsync(Context.User.Id);
                if (botuser.TcCnt < tcCost)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie posiadasz wystarczającej liczby TC!".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var imageCheck = await _img.CheckImageUrlAsync(imgUrl);
                if (imageCheck.IsError())
                {
                    await ReplyAsync("", embed: ExecutionResult.From(imageCheck).ToEmbedMessage($"{Context.User.Mention} ").Build());
                    return;
                }

                botuser.TcCnt -= tcCost;
                botuser.GameDeck.ForegroundImageUrl = imageCheck.Url;

                await db.SaveChangesAsync();

                await ReplyAsync("", embed: $"Zmieniono szczegół na stronie waifu użytkownika: {Context.User.Mention}!".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("tło strony")]
        [Alias("tlo strony", "site bg", "site background")]
        [Summary("zmienia obrazek tła profilu na stronie waifu (2000 TC)")]
        [Remarks("https://sanakan.pl/i/example_background.jpg"), RequireWaifuCommandChannel]
        public async Task ChangeWaifuSiteBackgroundAsync([Summary("bezpośredni adres do obrazka")] string imgUrl)
        {
            var tcCost = 2000;

            using (var db = new Database.DatabaseContext(Config))
            {
                var botuser = await db.GetUserOrCreateSimpleAsync(Context.User.Id);
                if (botuser.TcCnt < tcCost)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie posiadasz wystarczającej liczby TC!".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var imageCheck = await _img.CheckImageUrlAsync(imgUrl);
                if (imageCheck.IsError())
                {
                    await ReplyAsync("", embed: ExecutionResult.From(imageCheck).ToEmbedMessage($"{Context.User.Mention} ").Build());
                    return;
                }

                botuser.TcCnt -= tcCost;
                botuser.GameDeck.BackgroundImageUrl = imageCheck.Url;

                await db.SaveChangesAsync();

                await ReplyAsync("", embed: $"Zmieniono tło na stronie waifu użytkownika: {Context.User.Mention}!".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("pozycja tła strony")]
        [Alias("pozycja tla strony", "site bgp", "site background position")]
        [Summary("zmienia położenie obrazka tła profilu na stronie waifu")]
        [Remarks("65"), RequireWaifuCommandChannel]
        public async Task ChangeWaifuSiteBackgroundPositionAsync([Summary("pozycja w % od 0 do 100")] uint position)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var botuser = await db.GetUserOrCreateSimpleAsync(Context.User.Id);
                if (position > 100)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} podano niepoprawną wartość!".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                botuser.GameDeck.BackgroundPosition = (int)position;

                await db.SaveChangesAsync();

                await ReplyAsync("", embed: $"Zmieniono pozycję tła na stronie waifu użytkownika: {Context.User.Mention}!".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("pozycja szczegółu strony")]
        [Alias("pozycja szczególu strony", "pozycja szczegolu strony", "pozycja szczegołu strony", "site fgp", "site foreground position")]
        [Summary("zmienia położenie obrazka szczegółu profilu na stronie waifu")]
        [Remarks("78"), RequireWaifuCommandChannel]
        public async Task ChangeWaifuSiteForegroundPositionAsync([Summary("pozycja w % od 0 do 100")] uint position)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var botuser = await db.GetUserOrCreateSimpleAsync(Context.User.Id);
                if (position > 100)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} podano niepoprawną wartość!".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                botuser.GameDeck.ForegroundPosition = (int)position;

                await db.SaveChangesAsync();

                await ReplyAsync("", embed: $"Zmieniono pozycję szczegółu na stronie waifu użytkownika: {Context.User.Mention}!".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("wyprawa na koniec")]
        [Alias("expedition on end")]
        [Summary("ustawia co zrobić z kartą oznaczoną jako kosz po zakończonej wyprawie")]
        [Remarks("zniszcz"), RequireWaifuCommandChannel]
        public async Task OnEndExpedionAsync([Summary("akcja(nic/zniszcz/uwolnij)")] ActionAfterExpedition action)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var bUser = await db.GetUserOrCreateSimpleAsync(Context.User.Id);
                if (action == bUser.GameDeck.EndOfExpeditionAction)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} obecnie jest już ustawiona taka akcja.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                bUser.GameDeck.EndOfExpeditionAction = action;

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"user-{bUser.Id}", "users" });

                var actionName = action switch
                {
                    ActionAfterExpedition.Destroy => "niszczenie",
                    ActionAfterExpedition.Release => "uwalnianie",
                    _ => "nic"
                };

                await ReplyAsync("", embed: $"{Context.User.Mention} ustawiono akcje: **{actionName}**.".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("sortowanie galerii")]
        [Alias("sort gallery")]
        [Summary("ustawia sortowanie w galerii, podaje się kolejno WID kart jak mają być wyświetlone odzielone spacją")]
        [Remarks("23 234 123 1231"), RequireWaifuCommandChannel]
        public async Task GalleryOrderAsync([Summary("WIDs")][Remainder] string ids)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var bUser = await db.GetUserOrCreateSimpleAsync(Context.User.Id);
                if (string.IsNullOrEmpty(ids) || !ids.Split(" ").All(x => x.All(c => char.IsDigit(c))))
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie wykryto odpowiedniego sortowania.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                bUser.GameDeck.GalleryOrderedIds = ids;

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"user-{bUser.Id}", "users" });

                await ReplyAsync("", embed: $"{Context.User.Mention} zapisano nowe sortowanie.".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("galeria")]
        [Alias("gallery")]
        [Summary("wykupuje dodatkowe 5 pozycji w galerii (koszt 100 TC), podanie 0 jako krotności wypisuje obecny limit")]
        [Remarks("0"), RequireWaifuCommandChannel]
        public async Task IncGalleryLimitAsync([Summary("krotność użycia polecenia")] uint count = 0)
        {
            int cost = 100 * (int)count;
            using (var db = new Database.DatabaseContext(Config))
            {
                var bUser = await db.GetUserOrCreateSimpleAsync(Context.User.Id);
                if (count < 1)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} obecny limit to: {bUser.GameDeck.CardsInGallery}.".ToEmbedMessage(EMType.Info).Build());
                    return;
                }

                if (bUser.TcCnt < cost)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie masz wystarczającej liczby TC.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                bUser.TcCnt -= cost;
                bUser.GameDeck.CardsInGallery += 5 * (int)count;

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"user-{bUser.Id}", "users" });

                await ReplyAsync("", embed: $"{Context.User.Mention} powiększył swój limit kart w galerii do {bUser.GameDeck.CardsInGallery}.".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("loteria")]
        [Alias("lottery", "dej")]
        [Summary("wybierasz się na loterię i liczysz że coś fajnego Ci z niej wypadnie (wymagana przepustka)")]
        [Remarks(""), RequireWaifuCommandChannel]
        public async Task GoToLotteryAsync()
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var bUser = await db.GetUserOrCreateAsync(Context.User.Id);
                var ticket = bUser.GameDeck.Items.FirstOrDefault(x => x.Type == ItemType.LotteryTicket);
                if (ticket == null)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} niestety nie masz przepustki.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (ticket.Count == 1)
                {
                    bUser.GameDeck.Items.Remove(ticket);
                }
                else ticket.Count--;

                var rewardInfo = await _lottery.GetAndApplyRewardAsync(bUser);
                bUser.Stats.LotteryTicketsUsed++;

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"user-{bUser.Id}", "users" });

                await ReplyAsync("", embed: $"{Context.User.Mention} wygrał na loterii: **{rewardInfo}**".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("lazyt")]
        [Alias("lt")]
        [Summary("towrzy karty z ich fragmentów z domyślnie ustawionym niszczeniem kc na 3 oraz tagiem wymiana")]
        [Remarks("2 3 nie Wymiana Ulubione"), RequireAnyCommandChannelOrLevel(200)]
        public async Task MakeCardsFromFragmentsLazyModeAsync([Summary("ilość kart do utworzenia")] uint count = 20, [Summary("czy zniszczyć karty nie będące na liście życzeń i nie posiadające danej kc?")] uint destroyCards = 3,
            [Summary("czy zamienić niszczenie na uwalnianie?")] bool changeToRelease = false, [Summary("oznacz niezniszczone karty")] string tag = "wymiana", [Summary("oznacz karty z wishlisty")] string tagWishlist = "ulubione")
                => await MakeCardsFromFragmentsAsync(count, destroyCards, changeToRelease, tag, tagWishlist);

        [Command("druciarstwo")]
        [Alias("tinkering")]
        [Summary("towrzy karty z ich fragmentów(maks 20")]
        [Remarks("5"), RequireWaifuCommandChannel]
        public async Task MakeCardsFromFragmentsAsync([Summary("ilość kart do utworzenia(1337 fragmentów na kartę, co dwie karty podwaja się cena w danym dniu)")] uint count = 1, [Summary("czy zniszczyć karty nie będące na liście życzeń i nie posiadające danej kc?")] uint destroyCards = 0,
            [Summary("czy zamienić niszczenie na uwalnianie?")] bool changeToRelease = false, [Summary("oznacz niezniszczone karty")] string tag = "", [Summary("oznacz karty z wishlisty")] string tagWishlist = "")
        {
            if (count < 1)
                count = 1;

            if (count > 20)
                count = 20;

            if (!string.IsNullOrEmpty(tag) && tag.Contains(" "))
            {
                await ReplyAsync("", embed: $"{Context.User.Mention} oznaczenie nie może zawierać spacji.".ToEmbedMessage(EMType.Error).Build());
                return;
            }

            if (!string.IsNullOrEmpty(tagWishlist) && tagWishlist.Contains(" "))
            {
                await ReplyAsync("", embed: $"{Context.User.Mention} oznaczenie nie może zawierać spacji.".ToEmbedMessage(EMType.Error).Build());
                return;
            }

            using (var db = new Database.DatabaseContext(Config))
            {
                var bUser = await db.GetUserOrCreateAsync(Context.User.Id);
                var dt = bUser.TimeStatuses.FirstOrDefault(x => x.Type == StatusType.Tinkering);
                if (dt == null)
                {
                    dt = StatusType.Tinkering.NewTimeStatus();
                    bUser.TimeStatuses.Add(dt);
                }

                if (!dt.IsActive(_time.Now()))
                {
                    dt.EndsAt = _time.Now().AddDays(1).Date;
                    dt.IValue = 0;
                }

                long basePrice = 1337;
                if (dt.IValue > 1)
                {
                    basePrice *= (dt.IValue / 2) + 1;
                }

                long price = 0;
                var startIndex = (dt.IValue / 2) + 1;
                for (int i = 0; i < count; i++)
                {
                    var check = dt.IValue - startIndex;
                    if (check > 0 && check % 2 == 0)
                    {
                        startIndex = dt.IValue;
                        basePrice *= 2;
                    }

                    price += basePrice;
                    dt.IValue++;
                }

                if (price <= 0)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} cena dziwnie wyliczona.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var fragments = bUser.GameDeck.Items.FirstOrDefault(x => x.Type == ItemType.CardFragment);
                if (fragments == null || fragments.Count < price)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} niestety nie ma wystarczającej liczby fragmentów({price}).".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (bUser.GameDeck.Cards.Count + count > bUser.GameDeck.MaxNumberOfCards)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie masz miejsca na tyle kart!".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (fragments.Count == price)
                {
                    bUser.GameDeck.Items.Remove(fragments);
                }
                else fragments.Count -= price;

                var totalCards = new List<Card>();
                var charactersOnWishlist = new List<string>();
                var allWWCnt = await db.WishlistCountData.AsQueryable().AsNoTracking().ToListAsync();
                for (int i = 0; i < count; i++)
                {
                    var character = await _waifu.GetRandomCharacterAsync(bUser.PoolType);
                    if (character == null)
                    {
                        await ReplyAsync("", embed: "Brak połączania z Shindenem!".ToEmbedMessage(EMType.Error).Build());
                        return;
                    }

                    var card = _waifu.GenerateNewCard(Context.User, character);
                    card.Affection += bUser.GameDeck.AffectionFromKarma();
                    card.Source = CardSource.Tinkering;
                    card.IsTradable = false;

                    totalCards.Add(card);
                    bUser.Stats.CreatedCardsFromItems++;

                    bool isOnUserWishlist = await bUser.GameDeck.RemoveCharacterFromWishListAsync(card.Character, db);
                    if (isOnUserWishlist)
                        charactersOnWishlist.Add(card.Name);

                    var wishlistsCnt = allWWCnt.FirstOrDefault(x => x.Id == card.Character)?.Count ?? 0;
                    if (destroyCards > 0)
                    {
                        if (wishlistsCnt < destroyCards && !isOnUserWishlist)
                        {
                            card.DestroyOrRelease(bUser, changeToRelease);
                            continue;
                        }
                    }

                    if (!string.IsNullOrEmpty(tag) && !isOnUserWishlist)
                    {
                        var btag = await db.GetTagAsync(_tags, tag, Context.User.Id);
                        if (btag != null) card.Tags.Add(btag);
                    }

                    if (!string.IsNullOrEmpty(tagWishlist) && isOnUserWishlist)
                    {
                        var btag = await db.GetTagAsync(_tags, tagWishlist, Context.User.Id);
                        if (btag != null) card.Tags.Add(btag);
                    }

                    card.WhoWantsCount = wishlistsCnt;
                    bUser.GameDeck.Cards.Add(card);
                }

                await db.SaveChangesAsync();
                QueryCacheManager.ExpireTag(new string[] { $"user-{bUser.Id}", "users" });

                string openString = "";
                bool saveAgain = false;
                foreach (var card in totalCards)
                {
                    bool isOnUserWishlist = charactersOnWishlist.Any(x => x == card.Name);
                    if (card.WhoWantsCount < destroyCards && !isOnUserWishlist && destroyCards > 0)
                    {
                        openString += "🖤 ";
                    }
                    else
                    {
                        openString += $"{card.ToHeartWishlist(isOnUserWishlist)}";
                        if (db.AddActivityFromNewCard(card, isOnUserWishlist, _time, bUser, Context.User.GetUserNickInGuild()))
                        {
                            saveAgain = true;
                        }
                    }
                    openString += $"{card.GetString(false, false, true)}\n";
                }

                if (saveAgain)
                {
                    await db.SaveChangesAsync();
                }

                await ReplyAsync("", embed: $"{Context.User.Mention} utworzone karty to:\n\n {openString}".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("wytwórz")]
        [Alias("craft", "wytworz")]
        [Summary("towrzy przedmiot z listy przepisów")]
        [Remarks("2 20"), RequireWaifuCommandChannel]
        public async Task CraftItemAsync([Summary("przepis")]RecipeType recipe, [Summary("ilość")]int count = 1)
        {
            if (recipe == RecipeType.None || count <= 0)
            {
                await ReplyAsync("", embed: $"{Context.User.Mention} ????".ToEmbedMessage(EMType.Error).Build());
                return;
            }

            string times = count > 1 ? $" ({count})" : "";
            var itemRecipe = _waifu.GetItemRecipe(recipe);
            using (var db = new Database.DatabaseContext(Config))
            {
                var bUser = await db.GetUserOrCreateSimpleAsync(Context.User.Id);
                foreach (var currency in itemRecipe.RequiredPayments)
                {
                    if (!bUser.Pay(currency, count))
                    {
                        await ReplyAsync("", embed: $"{Context.User.Mention} nie masz wystarczającej liczby **{currency.Type}** by wytowrzyć **{itemRecipe.Name}**{times}.".ToEmbedMessage(EMType.Error).Build());
                        return;
                    }
                }

                foreach (var item in itemRecipe.RequiredItems)
                {
                    if (!bUser.GameDeck.RemoveItem(item, count))
                    {
                        await ReplyAsync("", embed: $"{Context.User.Mention} nie masz wystarczającej liczby **{item.Name}** by wytowrzyć **{itemRecipe.Name}**{times}.".ToEmbedMessage(EMType.Error).Build());
                        return;
                    }
                }

                var newItem = itemRecipe.Item.Type.ToItem(itemRecipe.Item.Count * count);
                bUser.GameDeck.AddItem(newItem);

                await ReplyAsync("", embed: $"{Context.User.Mention} utworzono: **{itemRecipe.Name}**{times}".ToEmbedMessage(EMType.Success).Build());

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"user-{bUser.Id}", "users" });
            }
        }

        [Command("przepisy", RunMode = RunMode.Async)]
        [Alias("recipes")]
        [Summary("wypisuje liste przepisów lub konkretny przepis")]
        [Remarks("2"), RequireWaifuCommandChannel]
        public async Task ShoItemRecipesAsync([Summary("przepis (opcjonalne)")]RecipeType recipe = RecipeType.None)
        {
            if (recipe == RecipeType.None)
            {
                await ReplyAsync("", embed: _waifu.GetItemRecipesList().ToEmbedMessage(EMType.Info).Build());
                return;
            }

            await ReplyAsync("", embed: _waifu.GetItemRecipe(recipe).ToString().ToEmbedMessage(EMType.Info).Build());
        }

        [Command("moje oznaczenia", RunMode = RunMode.Async)]
        [Alias("my tags")]
        [Summary("wypisuje dostepne oznaczenia")]
        [Remarks(""), RequireAnyCommandChannelOrLevel(60)]
        public async Task ShowUserTagsAsync()
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var buser = await db.GetBaseUserAndDontTrackAsync(Context.User.Id);

                var tags = new List<(Tag Tag, long Count)>();
                foreach (var tag in buser.GameDeck.Tags)
                    tags.Add((tag, await db.Cards.AsQueryable().AsNoTracking().CountAsync(x => x.GameDeckId == buser.Id &&  x.Tags.Contains(tag))));

                var dtag = new List<(TagIcon Tag, long Count)>();
                var favs = _tags.GetTag(Services.PocketWaifu.TagType.Favorite);
                dtag.Add((favs, await db.Cards.AsQueryable().AsNoTracking().CountAsync(x => x.GameDeckId == buser.Id &&  x.Tags.Any(x => x.Id == favs.Id))));
                var exch = _tags.GetTag(Services.PocketWaifu.TagType.Exchange);
                dtag.Add((exch, await db.Cards.AsQueryable().AsNoTracking().CountAsync(x => x.GameDeckId == buser.Id &&  x.Tags.Any(x => x.Id == exch.Id))));
                var gall = _tags.GetTag(Services.PocketWaifu.TagType.Gallery);
                dtag.Add((gall, await db.Cards.AsQueryable().AsNoTracking().CountAsync(x => x.GameDeckId == buser.Id &&  x.Tags.Any(x => x.Id == gall.Id))));
                var rese = _tags.GetTag(Services.PocketWaifu.TagType.Reservation);
                dtag.Add((rese, await db.Cards.AsQueryable().AsNoTracking().CountAsync(x => x.GameDeckId == buser.Id &&  x.Tags.Any(x => x.Id == rese.Id))));
                var tras = _tags.GetTag(Services.PocketWaifu.TagType.TrashBin);
                dtag.Add((tras, await db.Cards.AsQueryable().AsNoTracking().CountAsync(x => x.GameDeckId == buser.Id &&  x.Tags.Any(x => x.Id == tras.Id))));

                await ReplyAsync("", embed: ($"**Własne oznaczenia**: `{tags.Count}/{buser.GameDeck.MaxNumberOfTags}`\n\n"
                                          + $"{string.Join("\n", tags.Select(x => $"**{x.Tag.Name}** `{x.Count}`"))}\n\n"
                                          + $"**Domyślne oznaczenia**:\n\n"
                                          + $"{string.Join("\n", dtag.Select(x => $"{x.Tag.Icon} **{x.Tag.Name}** `{x.Count}`"))}")
                                          .ToEmbedMessage(EMType.Info).WithUser(Context.User).Build());
            }
        }

        [Command("przywróć oznaczenie")]
        [Alias("restore tag", "przywroć oznaczenie", "przywroc oznaczenie", "przywróc oznaczenie")]
        [Summary("przywraca stare oznaczenie, nie podanie oznaczenia wypisuje jakie są dostępne")]
        [Remarks("konie"), RequireWaifuCommandChannel]
        public async Task RestoreOldUserTagAsync([Summary("stare oznaczenie (nie może zawierać spacji)")] string tag = "", [Summary("nowe oznaczenie (nie może zawierać spacji)")] string newtag = "")
        {
            if (tag.Contains(" "))
            {
                await ReplyAsync("", embed: $"{Context.User.Mention} oznaczenie nie może zawierać spacji.".ToEmbedMessage(EMType.Error).Build());
                return;
            }

            if (newtag.Contains(" "))
            {
                await ReplyAsync("", embed: $"{Context.User.Mention} oznaczenie nie może zawierać spacji.".ToEmbedMessage(EMType.Error).Build());
                return;
            }

            using (var db = new Database.DatabaseContext(Config))
            {
                var buser = await db.Users.AsQueryable().Where(x => x.Id == Context.User.Id).Include(x => x.GameDeck).ThenInclude(x => x.Tags)
                    .Include(x => x.GameDeck).ThenInclude(x => x.Cards).ThenInclude(x => x.TagList)
                    .Include(x => x.GameDeck).ThenInclude(x => x.Cards).ThenInclude(x => x.Tags).FirstOrDefaultAsync();

                var oldTags = new List<string>();
                var tags = buser.GameDeck.Cards.Where(x => x.TagList != null).Select(x => x.TagList.Select(c => c.Name)).ToList();
                foreach(var t in tags) oldTags.AddRange(t);

                oldTags = oldTags.Distinct().Where(x  => !x.Equals("ulubione", StringComparison.CurrentCultureIgnoreCase)
                    && !x.Equals("rezerwacja", StringComparison.CurrentCultureIgnoreCase)
                    && !x.Equals("galeria", StringComparison.CurrentCultureIgnoreCase)
                    && !x.Equals("kosz", StringComparison.CurrentCultureIgnoreCase)
                    && !x.Equals("wymiana", StringComparison.CurrentCultureIgnoreCase)).ToList();

                if (oldTags.IsNullOrEmpty())
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie odnaleziono starych oznaczeń.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (string.IsNullOrEmpty(tag) || string.IsNullOrEmpty(newtag))
                {
                    await ReplyAsync("", embed: $"**Dostępne oznaczenia do przywrócenia**:\n\n{string.Join("\n", oldTags)}".ToEmbedMessage(EMType.Info).WithUser(Context.User).Build());
                    return;
                }

                if (!oldTags.Any(x => x.Equals(tag, StringComparison.CurrentCultureIgnoreCase)))
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie odnaleziono starego oznaczenia o nazwie `{tag}`.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var thisTag = await db.GetTagAsync(_tags, newtag, Context.User.Id);
                if (thisTag is null && buser.GameDeck.Tags.Count >= buser.GameDeck.MaxNumberOfTags)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie posiadasz oznaczenia o takiej nazwie oraz miejsca by utworzyć nowe.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (thisTag is null)
                {
                    if (_tags.IsSimilar(newtag))
                    {
                        await ReplyAsync("", embed: $"{Context.User.Mention} istnieje bardzo podobne oznaczenie domyślne, użyj go lub wymyśl inne.".ToEmbedMessage(EMType.Error).Build());
                        return;
                    }

                    thisTag = new Tag { Name = newtag };
                    buser.GameDeck.Tags.Add(thisTag);
                    await db.SaveChangesAsync();
                }

                var cards = buser.GameDeck.Cards.Where(x => !x.Tags.Any(x => x.Id == thisTag.Id) && x.TagList.Any(x => x.Name.Equals(tag, StringComparison.CurrentCultureIgnoreCase))).ToList();
                if (cards.IsNullOrEmpty())
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} wystąpił problem z odnalezieniem kart. Wszystkie są już oznaczone `{newtag}` lub nie ma już kart z oznaczeniem `{tag}`.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var msg = await ReplyAsync("", embed: $"{Context.User.Mention} rozpoczęto przywracanie `{cards.Count}` kart do oznaczenia `{newtag}`...".ToEmbedMessage(EMType.Bot).Build());
                foreach (var card in cards)
                {
                    card.Tags.Add(thisTag);

                    var otg = card.TagList.FirstOrDefault(x => x.Name.Equals(tag, StringComparison.CurrentCultureIgnoreCase));
                    if (otg != null) card.TagList.Remove(otg);
                }

                QueryCacheManager.ExpireTag(new string[] { $"user-{Context.User.Id}", "users" });

                await db.SaveChangesAsync();

                await msg.ModifyAsync(x => x.Embed = $"{Context.User.Mention} przywrócono `{cards.Count}` kart do oznaczenia `{newtag}`".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("utwórz oznaczenie")]
        [Alias("create tag", "utworz oznaczenie")]
        [Summary("tworzy oznaczenie")]
        [Remarks("konie"), RequireWaifuCommandChannel]
        public async Task CreateUserTagAsync([Summary("oznaczenie (nie może zawierać spacji)")] string tag)
        {
            if (tag.Contains(" "))
            {
                await ReplyAsync("", embed: $"{Context.User.Mention} oznaczenie nie może zawierać spacji.".ToEmbedMessage(EMType.Error).Build());
                return;
            }

            using (var db = new Database.DatabaseContext(Config))
            {
                var buser = await db.GetUserOrCreateSimpleAsync(Context.User.Id);
                if (buser.GameDeck.MaxNumberOfTags <= buser.GameDeck.Tags.Count)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie możesz utworzyć już więcej oznaczeń, skasuj jakieś lub zwiększ ich limit.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (_tags.IsSimilar(tag))
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} istnieje bardzo podobne oznaczenie domyślne, użyj go lub wymyśl inne.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (buser.GameDeck.Tags.Any(x => x.Name.Equals(tag, StringComparison.CurrentCultureIgnoreCase)))
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} już posiadasz takie oznaczenie.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                buser.GameDeck.Tags.Add(new Tag { Name = tag });
                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"user-{Context.User.Id}", "users" });

                await ReplyAsync("", embed: $"{Context.User.Mention} utworzono nowe oznaczenie: `{tag}`".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("modyfikuj oznaczenie")]
        [Alias("modify tag")]
        [Summary("zmienia lub kasuje oznaczenie")]
        [Remarks("zmień konie konina"), RequireWaifuCommandChannel]
        public async Task ChangeUserTagAsync([Summary("rodzaj akcji (usuń/zmień)")]ModifyTagActionType action, [Summary("oznaczenie (nie może zawierać spacji)")] string oldTag, [Summary("oznaczenie (nie może zawierać spacji, opcjonalne)")] string newTag = "")
        {
            if (oldTag.Contains(" "))
            {
                await ReplyAsync("", embed: $"{Context.User.Mention} oznaczenie nie może zawierać spacji.".ToEmbedMessage(EMType.Error).Build());
                return;
            }

            if (action == ModifyTagActionType.Rename && newTag.Contains(" "))
            {
                await ReplyAsync("", embed: $"{Context.User.Mention} oznaczenie nie może zawierać spacji.".ToEmbedMessage(EMType.Error).Build());
                return;
            }

            using (var db = new Database.DatabaseContext(Config))
            {
                var buser = await db.GetUserOrCreateSimpleAsync(Context.User.Id);
                var thisTag = buser.GameDeck.Tags.FirstOrDefault(x => x.Name.Equals(oldTag, StringComparison.CurrentCultureIgnoreCase));
                if (thisTag is null)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie odnaleziono oznaczenia.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var response = $"skasowano oznaczenie `{oldTag}`";
                if (action == ModifyTagActionType.Delete)
                {
                    buser.GameDeck.Tags.Remove(thisTag);
                }
                else
                {
                    if (_tags.IsSimilar(newTag))
                    {
                        await ReplyAsync("", embed: $"{Context.User.Mention} istnieje bardzo podobne oznaczenie domyślne, użyj go lub wymyśl inne.".ToEmbedMessage(EMType.Error).Build());
                        return;
                    }

                    if (buser.GameDeck.Tags.Any(x => x.Id != thisTag.Id && x.Name.Equals(newTag, StringComparison.CurrentCultureIgnoreCase)))
                    {
                        await ReplyAsync("", embed: $"{Context.User.Mention} już posiadasz takie oznaczenie.".ToEmbedMessage(EMType.Error).Build());
                        return;
                    }

                    response = $"nazwa została zmieniona z `{oldTag}` na `{newTag}`";
                    thisTag.Name = newTag;
                }

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"user-{Context.User.Id}", "users" });

                await ReplyAsync("", embed: $"{Context.User.Mention} {response}".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("oznacz")]
        [Alias("tag")]
        [Summary("dodaje tag do kart")]
        [Remarks("konie 231 12341 22"), RequireWaifuCommandChannel]
        public async Task ChangeCardTagAsync([Summary("oznaczenie (nie może zawierać spacji)")] string tag, [Summary("WIDs")] params ulong[] wids)
        {
            if (tag.Contains(" "))
            {
                await ReplyAsync("", embed: $"{Context.User.Mention} oznaczenie nie może zawierać spacji.".ToEmbedMessage(EMType.Error).Build());
                return;
            }

            using (var db = new Database.DatabaseContext(Config))
            {
                var thisTag = await db.GetTagAsync(_tags, tag, Context.User.Id);
                if (thisTag is null)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie odnaleziono oznaczenia `{tag}`".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var cardsSelected = await db.Cards.AsQueryable().Where(x => x.GameDeckId == Context.User.Id).Include(x => x.Tags)
                    .Where(x => wids.Any(c => c == x.Id) && !x.Tags.Any(t => t.Id == thisTag.Id)).ToListAsync();

                if (cardsSelected.IsNullOrEmpty())
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie odnaleziono nieoznaczonych kart.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                foreach (var thisCard in cardsSelected)
                    thisCard.Tags.Add(thisTag);

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"user-{Context.User.Id}", "users" });

                await ReplyAsync("", embed: $"{Context.User.Mention} oznaczono {cardsSelected.Count} kart.".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("oznacz czyść")]
        [Alias("tag clean", "oznacz czysć", "oznacz czyśc", "oznacz czysc")]
        [Summary("czyści tagi z kart")]
        [Remarks("22"), RequireWaifuCommandChannel]
        public async Task CleanCardTagAsync([Summary("WIDs")] params ulong[] wids)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var cardsSelected = await db.Cards.AsQueryable().Where(x => x.GameDeckId == Context.User.Id).Include(x => x.Tags)
                    .Where(x => wids.Any(c => c == x.Id) && x.Tags.Any()).ToListAsync();

                if (cardsSelected.IsNullOrEmpty())
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie odnaleziono nieoznaczonych kart.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                foreach (var thisCard in cardsSelected)
                    thisCard.Tags.Clear();

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"user-{Context.User.Id}", "users" });

                await ReplyAsync("", embed: $"{Context.User.Mention} usunięto oznaczenie z {cardsSelected.Count} kart.".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("oznacz puste")]
        [Alias("tag empty")]
        [Summary("dodaje tag do kart, które nie są oznaczone")]
        [Remarks("konie"), RequireWaifuCommandChannel]
        public async Task ChangeCardsTagAsync([Summary("oznaczenie (nie może zawierać spacji)")] string tag)
        {
            if (tag.Contains(" "))
            {
                await ReplyAsync("", embed: $"{Context.User.Mention} oznaczenie nie może zawierać spacji.".ToEmbedMessage(EMType.Error).Build());
                return;
            }

            using (var db = new Database.DatabaseContext(Config))
            {
                var thisTag = await db.GetTagAsync(_tags, tag, Context.User.Id);
                if (thisTag is null)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie odnaleziono oznaczenia `{tag}`".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var untaggedCards = await db.Cards.AsQueryable().Where(x => x.GameDeckId == Context.User.Id).Include(x => x.Tags).Where(x => !x.Tags.Any()).ToListAsync();
                if (untaggedCards.IsNullOrEmpty())
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie odnaleziono nieoznaczonych kart.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                foreach (var card in untaggedCards)
                    card.Tags.Add(thisTag);

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"user-{Context.User.Id}", "users" });

                await ReplyAsync("", embed: $"{Context.User.Mention} oznaczono {untaggedCards.Count} kart.".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("oznacz podmień")]
        [Alias("tag replace", "oznacz podmien")]
        [Summary("podmienia oznaczenie na wszystkich kartach, niepodanie nowego usuwa stare z kart")]
        [Remarks("konie wymiana"), RequireWaifuCommandChannel]
        public async Task ReplaceCardsTagAsync([Summary("stare oznaczenie")] string oldTag, [Summary("nowe oznaczenie")] string newTag = "%$-1")
        {
            bool removeTag = newTag.Equals("%$-1");
            if (newTag.Contains(" "))
            {
                await ReplyAsync("", embed: $"{Context.User.Mention} oznaczenie nie może zawierać spacji.".ToEmbedMessage(EMType.Error).Build());
                return;
            }

            if (!removeTag && newTag.Equals(oldTag, StringComparison.CurrentCultureIgnoreCase))
            {
                await ReplyAsync("", embed: $"{Context.User.Mention} nowe oznaczenie nie może być takie samo jak stare.".ToEmbedMessage(EMType.Error).Build());
                return;
            }

            using (var db = new Database.DatabaseContext(Config))
            {
                var oldt = await db.GetTagAsync(_tags, oldTag, Context.User.Id);
                if (oldt is null)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie odnaleziono oznaczenia `{oldTag}`".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var newt = await db.GetTagAsync(_tags, newTag, Context.User.Id);
                if (!removeTag && newt is null)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie odnaleziono oznaczenia `{oldTag}`".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var newTagId = removeTag ? 0 : newt.Id;
                var cardsSelected = await db.Cards.AsQueryable().Where(x => x.GameDeckId == Context.User.Id).Include(x => x.Tags)
                    .Where(x => x.Tags.Any(t => t.Id == oldt.Id) && !x.Tags.Any(t => t.Id == newTagId)).ToListAsync();

                if (cardsSelected.IsNullOrEmpty())
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie odnaleziono oznaczonych kart.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                foreach (var card in cardsSelected)
                {
                    card.Tags.Remove(oldt);
                    if (!removeTag)
                        card.Tags.Add(newt);
                }

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"user-{Context.User.Id}", "users" });

                string thing = removeTag ? "skasowane" : "zmienione";
                await ReplyAsync("", embed: $"{Context.User.Mention} {thing} zostało oznaczenie na {cardsSelected.Count} kartach.".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("oznacz usuń")]
        [Alias("tag remove", "oznacz usun")]
        [Summary("kasuje tag z kart")]
        [Remarks("ulubione 2211 2123 33123"), RequireWaifuCommandChannel]
        public async Task RemoveCardTagAsync([Summary("oznaczenie (nie może zawierać spacji)")] string tag, [Summary("WIDs")] params ulong[] wids)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var thisTag = await db.GetTagAsync(_tags, tag, Context.User.Id);
                if (thisTag is null)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie odnaleziono oznaczenia `{tag}`".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var cardsSelected = await db.Cards.AsQueryable().Where(x => x.GameDeckId == Context.User.Id).Include(x => x.Tags)
                    .Where(x => wids.Any(c => c == x.Id) && x.Tags.Any(t => t.Id == thisTag.Id)).ToListAsync();

                if (cardsSelected.IsNullOrEmpty())
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie odnaleziono nieoznaczonych kart.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                foreach (var card in cardsSelected)
                    card.Tags.Remove(thisTag);

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"user-{Context.User.Id}", "users" });

                await ReplyAsync("", embed: $"{Context.User.Mention} skasowane zostało oznaczenie `{tag}` z {cardsSelected.Count} kart.".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("zasady wymiany")]
        [Alias("exchange conditions")]
        [Summary("ustawia tekst będący zasadami wymiany z nami, wywołanie bez podania zasad kasuje tekst")]
        [Remarks("Wymieniam się tylko za karty z mojej listy życzeń."), RequireWaifuCommandChannel]
        public async Task SetExchangeConditionAsync([Summary("zasady wymiany")][Remainder] string condition = null)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var bUser = await db.GetUserOrCreateSimpleAsync(Context.User.Id);

                bUser.GameDeck.ExchangeConditions = condition;

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"user-{bUser.Id}", "users" });

                await ReplyAsync("", embed: $"{Context.User.Mention} ustawił nowe zasady wymiany.".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("talia")]
        [Alias("deck", "aktywne")]
        [Summary("wyświetla aktywne karty/ustawia kartę jako aktywną")]
        [Remarks("1"), RequireWaifuCommandChannel]
        public async Task ChangeDeckCardStatusAsync([Summary("WID")] ulong wid = 0)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var active = db.Cards.AsQueryable().AsNoTracking().Where(x => x.Active && x.GameDeckId == Context.User.Id).ToList();
                if (wid == 0)
                {
                    if (active.Count < 1)
                    {
                        await ReplyAsync("", embed: $"{Context.User.Mention} nie masz aktywnych kart.".ToEmbedMessage(EMType.Info).Build());
                        return;
                    }

                    var res = await _helepr.SendEmbedsOnDMAsync(Context.User, _waifu.GetActiveList(active));
                    await ReplyAsync("", embed: res.ToEmbedMessage($"{Context.User.Mention} ").Build());
                    return;
                }

                var bUser = await db.GetUserOrCreateSimpleAsync(Context.User.Id);
                var thisCard = db.Cards.AsQueryable().FirstOrDefault(x => x.Id == wid);

                if (thisCard == null || thisCard.GameDeckId != Context.User.Id)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie odnaleziono karty.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (thisCard.InCage)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} ta karta znajduje się w klatce.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var tac = active.FirstOrDefault(x => x.Id == thisCard.Id);
                if (tac == null)
                {
                    active.Add(thisCard);
                    thisCard.Active = true;
                }
                else
                {
                    active.Remove(tac);
                    thisCard.Active = false;
                }

                bUser.GameDeck.DeckPower = active.Sum(x => x.CalculateCardPower());

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"user-{bUser.Id}", "users" });

                var message = thisCard.Active ? "aktywował: " : "dezaktywował: ";
                var power = $"**Moc talii**: {bUser.GameDeck.DeckPower:F}";
                await ReplyAsync("", embed: $"{Context.User.Mention} {message}{thisCard.GetString(false, false, true)}\n\n{power}".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("kto", RunMode = RunMode.Async)]
        [Alias("who")]
        [Summary("pozwala wyszukać użytkowników posiadających kartę danej postaci")]
        [Remarks("51 tak tak"), RequireWaifuCommandChannel]
        public async Task SearchCharacterCardsAsync([Summary("id postaci na shinden")] ulong id, [Summary("czy zamienić oznaczenia na nicki?")] bool showNames = false, [Summary("czy dodać linki do profili?")] bool showShindenUrl = false, [Summary("czy wyświetlić tylko karty z skalpelem/kamerą?")] bool onlyScalpels = false, [Summary("czy sortować po użytkowniku?")] bool groupByUser = false)
        {
            var charInfo = await _shinden.GetCharacterInfoAsync(id);
            if (charInfo == null)
            {
                await ReplyAsync("", embed: $"Nie odnaleziono postaci na shindenie!".ToEmbedMessage(EMType.Error).Build());
                return;
            }

            using (var db = new Database.DatabaseContext(Config))
            {
                var cards = await db.Cards.Include(x => x.Tags).Include(x => x.GameDeck).ThenInclude(x => x.User).Where(x => x.Character == id).AsNoTracking().FromCacheAsync(new[] { "users" });

                if (onlyScalpels)
                    cards = cards.Where(x => !string.IsNullOrEmpty(x.CustomImage));

                if (groupByUser)
                    cards = cards.OrderBy(x => x.GameDeckId);

                if (cards.IsNullOrEmpty())
                {
                    await ReplyAsync("", embed: $"Nie odnaleziono kart {charInfo}".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var msgs = await _waifu.GetWaifuFromCharacterSearchResult($"[{charInfo}]({charInfo.CharacterUrl}) posiadają:", cards, !showNames, Context.Guild, showShindenUrl);
                if (msgs.Count == 1)
                {
                    await ReplyAsync("", embed: msgs.First());
                    return;
                }

                var res = await _helepr.SendEmbedsOnDMAsync(Context.User, msgs);
                await ReplyAsync("", embed: res.ToEmbedMessage($"{Context.User.Mention} ").Build());
            }
        }

        [Command("ulubione", RunMode = RunMode.Async)]
        [Alias("favs")]
        [Summary("pozwala wyszukać użytkowników posiadających karty z naszej listy ulubionych postaci")]
        [Remarks("tak tak"), RequireWaifuCommandChannel]
        public async Task SearchCharacterCardsFromFavListAsync([Summary("czy pokazać ulubione domyślnie ukryte? (true/false)")] bool showFavs = false,
            [Summary("czy zamienić oznaczenia na nicki?")] bool showNames = false,
            [Summary("czy dodać linki do profili?")] bool showShindenUrl = false)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var user = await db.GetCachedFullUserAsync(Context.User.Id);
                if (user == null)
                {
                    await ReplyAsync("", embed: "Nie posiadasz profilu bota!".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var response = await _shclient.User.GetFavCharactersAsync(user.Shinden);
                if (!response.IsSuccessStatusCode())
                {
                    await ReplyAsync("", embed: $"Nie odnaleziono listy ulubionych postaci!".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var cards = await db.Cards.AsQueryable().Include(x => x.Tags).Include(x => x.GameDeck).ThenInclude(x => x.User).AsSplitQuery()
                    .Where(x => x.GameDeckId != user.Id && response.Body.Select(x => x.Id).Contains(x.Character)).AsNoTracking().ToListAsync();

                if (!showFavs)
                {
                    var tid = _tags.GetTagId(Services.PocketWaifu.TagType.Favorite);
                    cards = cards.Where(x => !x.Tags.Any(t => t.Id == tid)).ToList();
                }

                if (cards.Count < 1)
                {
                    await ReplyAsync("", embed: $"Nie odnaleziono kart.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var embeds = await _waifu.GetWaifuFromCharacterTitleSearchResultAsync(cards, !showNames, Context.Guild, showShindenUrl);
                var res = await _helepr.SendEmbedsOnDMAsync(Context.User, embeds);
                await ReplyAsync("", embed: res.ToEmbedMessage($"{Context.User.Mention} ").Build());
            }
        }

        [Command("jakie", RunMode = RunMode.Async)]
        [Alias("which")]
        [Summary("pozwala wyszukać użytkowników posiadających karty z danego tytułu")]
        [Remarks("1 tak"), RequireWaifuCommandChannel]
        public async Task SearchCharacterCardsFromTitleAsync([Summary("id serii na shinden")] ulong id, [Summary("czy zamienić oznaczenia na nicki?")] bool showNames = false)
        {
            var response = await _shclient.Title.GetCharactersAsync(id);
            if (!response.IsSuccessStatusCode())
            {
                await ReplyAsync("", embed: $"Nie odnaleziono postaci z serii na shindenie!".ToEmbedMessage(EMType.Error).Build());
                return;
            }

            var characterIds = response.Body.Select(x => x.CharacterId).Distinct().ToList();
            if (characterIds.Count < 1)
            {
                await ReplyAsync("", embed: $"Nie odnaleziono postaci!".ToEmbedMessage(EMType.Error).Build());
                return;
            }

            using (var db = new Database.DatabaseContext(Config))
            {
                var cards = await db.Cards.AsQueryable().Include(x => x.Tags).Include(x => x.GameDeck).AsSplitQuery().Where(x => characterIds.Contains(x.Character)).AsNoTracking().FromCacheAsync(new[] { "users" });

                if (cards.Count() < 1)
                {
                    await ReplyAsync("", embed: $"Nie odnaleziono kart.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var embeds = await _waifu.GetWaifuFromCharacterTitleSearchResultAsync(cards, !showNames, Context.Guild);
                var res = await _helepr.SendEmbedsOnDMAsync(Context.User, embeds);
                await ReplyAsync("", embed: res.ToEmbedMessage($"{Context.User.Mention} ").Build());
            }
        }

        [Command("wymiana")]
        [Alias("exchange")]
        [Summary("propozycja wymiany z użytkownikiem")]
        [Remarks("Karna Ulubione"), RequireWaifuMarketChannel]
        public async Task ExchangeCardsAsync([Summary("nazwa użytkownika")] SocketGuildUser user2, [Summary("oznacza karty po wymianie podanym tagiem")] string tag = "")
        {
            if (!string.IsNullOrEmpty(tag) && tag.Contains(" "))
            {
                await ReplyAsync("", embed: $"{Context.User.Mention} oznaczenie nie może zawierać spacji.".ToEmbedMessage(EMType.Error).Build());
                return;
            }

            var user1 = Context.User as SocketGuildUser;
            if (user1 == null) return;

            if (user1.Id == user2.Id)
            {
                await ReplyAsync("", embed: $"{user1.Mention} wymiana z samym sobą?".ToEmbedMessage(EMType.Error).Build());
                return;
            }

            var session = new ExchangeSession(user1, user2, _config, _time, _tags);
            await Task.Delay(Services.Fun.GetRandomValue(30, 230));
            if (_session.SessionExist(session))
            {
                await ReplyAsync("", embed: $"{user1.Mention} Ty lub twój partner znajdujecie się obecnie w trakcie wymiany.".ToEmbedMessage(EMType.Error).Build());
                return;
            }

            using (var db = new Database.DatabaseContext(Config))
            {
                var duser1 = await db.GetCachedFullUserAsync(user1.Id);
                var duser2 = await db.GetCachedFullUserAsync(user2.Id);
                if (duser1 == null || duser2 == null)
                {
                    await ReplyAsync("", embed: "Jeden z graczy nie posiada profilu!".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (duser1.IsBlacklisted || duser2.IsBlacklisted)
                {
                    await ReplyAsync("", embed: "Jeden z graczy znajduje się na czarnej liście!".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (duser1.GameDeck.MaxNumberOfCards <= duser1.GameDeck.Cards.Count || duser2.GameDeck.MaxNumberOfCards <= duser2.GameDeck.Cards.Count)
                {
                    await ReplyAsync("", embed: "Jeden z graczy nie posiada już miejsca na karty!".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                session.P1 = new PlayerInfo
                {
                    Tag = tag,
                    User = user1,
                    Dbuser = duser1,
                    Accepted = false,
                    CustomString = "",
                    Cards = new List<Card>()
                };

                session.P2 = new PlayerInfo
                {
                    Tag = "",
                    User = user2,
                    Dbuser = duser2,
                    Accepted = false,
                    CustomString = "",
                    Cards = new List<Card>()
                };

                session.Name = "🔄 **Wymiana:**";
                session.Tips = $"Polecenia: `dodaj [WID]`, `usuń [WID]`.\n\n\u0031\u20E3 "
                    + $"- zakończenie dodawania {user1.Mention}\n\u0032\u20E3 - zakończenie dodawania {user2.Mention}";

                var msg = await ReplyAsync("", embed: session.BuildEmbed());
                await msg.AddReactionsAsync(session.StartReactions);
                session.Message = msg;

                await _session.TryAddSession(session);
            }
        }

        [Command("barter")]
        [Alias("scrape")]
        [Summary("wymienia przedmioty na fragementy kart")]
        [Remarks("3:30 5:40 1:0"), RequireWaifuCommandChannel]
        public async Task ChangeItemsToCardFragmentsAsync([Summary("nr przedmiotu:ilość przedmiotu(0 traktowane jako wszystkie)")] params ItemCountPair[] items)
        {
            if (items.IsNullOrEmpty())
            {
                await ReplyAsync("", embed: $"{Context.User.Mention} lista przedmiotów jest niepoprawna.".ToEmbedMessage(EMType.Error).Build());
                return;
            }

            using (var db = new Database.DatabaseContext(Config))
            {
                var buser = await db.Users.AsQueryable().Where(x => x.Id == Context.User.Id).Include(x => x.GameDeck).ThenInclude(x => x.Items).FirstOrDefaultAsync();
                if (buser == null || buser.GameDeck.Items.IsNullOrEmpty())
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie masz żadnych przemiotów.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                long scrapes = 0;
                var errors = new List<string>();
                var userItems = buser.GetAllItems().ToArray();
                foreach (var it in items)
                {
                    var thisItem = userItems[it.Item - 1];
                    if (it.Count == 0)
                    {
                        it.Count = thisItem.Count;
                    }

                    if (thisItem.Type.IsProtected() && !it.Force)
                    {
                        errors.Add($"{thisItem.Name}x{it.Count}");
                        continue;
                    }

                    if (thisItem.Count < it.Count)
                    {
                        if (!it.Force)
                            errors.Add($"{thisItem.Name}x{it.Count - thisItem.Count}");

                        it.Count = thisItem.Count;
                    }

                    scrapes += it.Count * thisItem.Type.CValue();
                    thisItem.Count -= it.Count;

                    if (thisItem.Count < 1)
                        buser.GameDeck.Items.Remove(thisItem);
                }

                var tItem = buser.GameDeck.Items.FirstOrDefault(x => x.Type == ItemType.CardFragment);
                if (tItem == null)
                {
                    tItem = ItemType.CardFragment.ToItem(scrapes);
                    buser.GameDeck.Items.Add(tItem);
                }
                else tItem.Count += scrapes;

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"user-{buser.Id}" });

                string errorInfo = errors.Count > 0 ? $"\n\n❗ Nie udało się wymienić:\n\n{string.Join("\n", errors)}": "";
                await ReplyAsync("", embed: $"{Context.User.Mention} udało się zyskać **{scrapes}** fragmentów, masz ich w sumie już **{tItem.Count}**.{errorInfo}"
                    .ToEmbedMessage(errors.Count >= items.Length ? EMType.Warning : EMType.Success).Build());
            }
        }

        [Command("wyprawa status", RunMode = RunMode.Async)]
        [Alias("expedition status")]
        [Summary("wypisuje karty znajdujące się na wyprawach")]
        [Remarks(""), RequireWaifuFightChannel]
        public async Task ShowExpeditionStatusAsync()
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var botUser = await db.GetCachedFullUserAsync(Context.User.Id);
                var cardsOnExpedition = botUser.GameDeck.Cards.Where(x => x.Expedition != CardExpedition.None).ToList();

                if (cardsOnExpedition.Count < 1)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie posiadasz kart znajdujących się na wyprawie.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var expStrs = cardsOnExpedition.Select(x => $"{x.GetShortString(true)}:\n Od {x.ExpeditionDate.ToShortDateTime()} na {x.Expedition.GetName("ej")} wyprawie.\nTraci siły po {_expedition.GetMaxPossibleLengthOfExpedition(botUser, x):F} min.");
                await ReplyAsync("", embed: $"**Wyprawy[**{cardsOnExpedition.Count}/{botUser.GameDeck.LimitOfCardsOnExpedition()}**]** {Context.User.Mention}:\n\n{string.Join("\n\n", expStrs)}".ToEmbedMessage(EMType.Bot).WithUser(Context.User).Build());
            }
        }

        [Command("wyprawa koniec")]
        [Alias("expedition end")]
        [Summary("kończy wyprawę karty")]
        [Remarks("11321"), RequireWaifuFightChannel]
        public async Task EndCardExpeditionAsync([Summary("WID")] ulong wid)
        {
            if (_session.SessionExist(Context.User, typeof(ExchangeSession)))
            {
                await ReplyAsync("", embed: $"{Context.User.Mention} znajdujesz się obecnie w trakcie wymiany.".ToEmbedMessage(EMType.Error).Build());
                return;
            }

            using (var db = new Database.DatabaseContext(Config))
            {
                var botUser = await db.GetUserOrCreateAsync(Context.User.Id);
                var thisCard = botUser.GameDeck.Cards.FirstOrDefault(x => x.Id == wid);
                if (thisCard == null)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie odnaleziono karty.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (thisCard.Expedition == CardExpedition.None)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} ta karta nie jest na wyprawie.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var oldName = thisCard.Expedition;
                var message = _waifu.EndExpedition(botUser, thisCard);
                _ = thisCard.CalculateCardPower();

                string action = "";
                var trashTag = _tags.GetTag(Services.PocketWaifu.TagType.TrashBin);
                if (botUser.GameDeck.EndOfExpeditionAction != ActionAfterExpedition.Nothing
                    && thisCard.Curse == CardCurse.None
                    && _tags.HasTag(thisCard, trashTag)
                    && !message.Contains("Utrata karty")
                    && !_tags.HasTag(thisCard, Services.PocketWaifu.TagType.Favorite))
                {
                    var receivedCt = thisCard.DestroyOrRelease(botUser, botUser.GameDeck.EndOfExpeditionAction == ActionAfterExpedition.Release, 0.14);
                    var addMsg = receivedCt && botUser.GameDeck.EndOfExpeditionAction == ActionAfterExpedition.Release;

                    action = addMsg ? $"\n\n{trashTag.Icon} Wykonano akcje: **{botUser.GameDeck.EndOfExpeditionAction.ToName()}** na karcie powracającej z wyprawy.\nWieść o twoim okrucieństwie się rozeszła."
                        : $"\n\n{trashTag.Icon} Wykonano akcje: **{botUser.GameDeck.EndOfExpeditionAction.ToName()}** na karcie powracającej z wyprawy.";

                    _waifu.DeleteCardImageIfExist(thisCard);
                    botUser.GameDeck.Cards.Remove(thisCard);
                }

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"user-{botUser.Id}", "users" });

                _ = Task.Run(async () =>
                {
                    await ReplyAsync("", embed: $"Karta {thisCard.GetString(false, false, true)} wróciła z {oldName.GetName("ej")} wyprawy!\n\n{message}{action}".ToEmbedMessage(EMType.Success).WithUser(Context.User).Build());
                });
            }
        }

        [Command("wyprawa")]
        [Alias("expedition")]
        [Summary("wysyła karty na wyprawę")]
        [Remarks("n 11321 123112"), RequireWaifuFightChannel]
        public async Task SendCardToExpeditionAsync([Summary("typ wyprawy")] CardExpedition expedition, [Summary("WIDs")] params ulong[] wids)
        {
            if (expedition == CardExpedition.None)
            {
                await ReplyAsync("", embed: $"{Context.User.Mention} nie podałeś poprawnej nazwy wyprawy.".ToEmbedMessage(EMType.Error).Build());
                return;
            }

            if (_session.SessionExist(Context.User, typeof(ExchangeSession)))
            {
                await ReplyAsync("", embed: $"{Context.User.Mention} znajdujesz się obecnie w trakcie wymiany.".ToEmbedMessage(EMType.Error).Build());
                return;
            }

            using (var db = new Database.DatabaseContext(Config))
            {
                var botUser = await db.GetUserOrCreateAsync(Context.User.Id);
                var cardsSelected = botUser.GameDeck.Cards.Where(x => wids.Any(c => c == x.Id)).ToList();
                if (cardsSelected.Count < 1)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie odnaleziono kart.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var cardsOnExp = botUser.GameDeck.Cards.Count(x => x.Expedition != CardExpedition.None);
                if (cardsOnExp + cardsSelected.Count > botUser.GameDeck.LimitOfCardsOnExpedition())
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie możesz wysłać więcej kart na wyprawę.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                foreach (var card in cardsSelected)
                {
                    if (card.Fatigue > 0)
                    {
                        var breakFromExpedition = (_time.Now() - card.ExpeditionEndDate).TotalMinutes;
                        if (breakFromExpedition > 1)
                        {
                            var toRecover = Math.Min(0.173 * breakFromExpedition, 1000);
                            card.Fatigue = Math.Max(card.Fatigue - toRecover, 0);
                        }
                    }

                    var expeditionBlockadeType = _expedition.IsValidToGo(botUser, card, expedition, _tags);
                    if (expeditionBlockadeType != Expedition.BlockadeReason.None)
                    {
                        var reason = expeditionBlockadeType switch
                        {
                            Expedition.BlockadeReason.Fatigue       => "Karta jest zbyt zmęczona.",
                            Expedition.BlockadeReason.Expedition    => "Karta jest na wyprawie.",
                            Expedition.BlockadeReason.Curse         => "Karta posiada klątwę.",
                            Expedition.BlockadeReason.Cage          => "Karta znajduje się w klatce.",
                            Expedition.BlockadeReason.Affection     => "Karta ma zbyt niską relacje.",
                            Expedition.BlockadeReason.Tag           => "Karta ma oznaczenie blokujące wyprawę.",
                            Expedition.BlockadeReason.Rarity        => "Karta ma zbyt niską jakość.",
                            Expedition.BlockadeReason.Ultimate      => "Karta nie może być kartą ultimate.",
                            Expedition.BlockadeReason.Karma         => "Nie spełniasz wymogów karmy na wyprawę.",
                            _ => "????"
                        };

                        await ReplyAsync("", embed: $"{Context.User.Mention} {card.GetIdWithUrl()} ta karta nie może się udać na tę wyprawę.\n{reason}".ToEmbedMessage(EMType.Error).Build());
                        return;
                    }

                    card.Expedition = expedition;
                    card.ExpeditionDate = _time.Now();
                }

                var mission = botUser.TimeStatuses.FirstOrDefault(x => x.Type == Database.Models.StatusType.DExpeditions);
                if (mission == null)
                {
                    mission = Database.Models.StatusType.DExpeditions.NewTimeStatus();
                    botUser.TimeStatuses.Add(mission);
                }
                mission.Count(_time.Now(), cardsSelected.Count);

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"user-{botUser.Id}", "users" });

                _ = Task.Run(async () =>
                {
                    if (cardsSelected.Count == 1)
                    {
                        var thisCard = cardsSelected.FirstOrDefault();
                        var max = _expedition.GetMaxPossibleLengthOfExpedition(botUser, thisCard, expedition).ToString("F");
                        await ReplyAsync("", embed: $"{thisCard.GetString(false, false, true)} udała się na {expedition.GetName("ą")} wyprawę!\nZmęczy się za {max} min.".ToEmbedMessage(EMType.Success).WithUser(Context.User).Build());
                    }
                    else
                    {
                        await ReplyAsync("", embed: $"Wysłano **{cardsSelected.Count}** kart na {expedition.GetName("ą")} wyprawę!".ToEmbedMessage(EMType.Success).WithUser(Context.User).Build());
                    }
                });
            }
        }

        [Command("pojedynek")]
        [Alias("duel")]
        [Summary("stajesz do walki naprzeciw innemu graczowi")]
        [Remarks(""), RequireWaifuDuelChannel]
        public async Task MakeADuelAsync()
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var duser = await db.GetUserOrCreateAsync(Context.User.Id);
                if (duser.GameDeck.NeedToSetDeckAgain())
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} musisz na nowo ustawić swóją talie!".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var canFight = duser.GameDeck.CanFightPvP();
                if (canFight != DeckPowerStatus.Ok)
                {
                    var err = (canFight == DeckPowerStatus.TooLow) ? "słabą" : "silną";
                    await ReplyAsync("", embed: $"{Context.User.Mention} masz zbyt {err} talie ({duser.GameDeck.GetDeckPower():F}).".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var pvpDailyMax = duser.TimeStatuses.FirstOrDefault(x => x.Type == StatusType.Pvp);
                if (pvpDailyMax == null)
                {
                    pvpDailyMax = StatusType.Pvp.NewTimeStatus();
                    duser.TimeStatuses.Add(pvpDailyMax);
                }

                if (!pvpDailyMax.IsActive(_time.Now()))
                {
                    pvpDailyMax.EndsAt = _time.Now().Date.AddHours(3).AddDays(1);
                    duser.GameDeck.PVPDailyGamesPlayed = 0;
                }

                if (duser.GameDeck.ReachedDailyMaxPVPCount())
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} dziś już nie możesz rozegrać pojedynku.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if ((_time.Now() - duser.GameDeck.PVPSeasonBeginDate.AddMonths(1)).TotalSeconds > 1)
                {
                    duser.GameDeck.PVPSeasonBeginDate = new DateTime(_time.Now().Year, _time.Now().Month, 1);
                    duser.GameDeck.SeasonalPVPRank = 0;
                }

                var allPvpPlayers = await db.GetCachedPlayersForPVP(duser.Id);
                if (allPvpPlayers.Count < 10)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} zbyt mała liczba graczy ma utworzoną poprawną talię!".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                double toLong = 1;
                var pvpPlayersInRange = allPvpPlayers.Where(x => x.IsNearMMR(duser.GameDeck)).ToList();
                for (double mrr = 0.5; pvpPlayersInRange.Count < 10; mrr += (0.5 * toLong))
                {
                    pvpPlayersInRange = allPvpPlayers.Where(x => x.IsNearMMR(duser.GameDeck, mrr)).ToList();
                    toLong += 0.5;
                }

                var randEnemy = Services.Fun.GetOneRandomFrom(pvpPlayersInRange).UserId;
                var denemy = await db.GetUserOrCreateAsync(randEnemy);
                var euser = Context.Client.GetUser(denemy.Id);
                while (euser == null)
                {
                    randEnemy = Services.Fun.GetOneRandomFrom(pvpPlayersInRange).UserId;
                    denemy = await db.GetUserOrCreateAsync(randEnemy);
                    euser = Context.Client.GetUser(denemy.Id);
                }

                var players = new List<PlayerInfo>
                {
                    new PlayerInfo
                    {
                        Cards = duser.GameDeck.Cards.Where(x => x.Active).ToList(),
                        User = Context.User,
                        Dbuser = duser
                    },
                    new PlayerInfo
                    {
                        Cards = denemy.GameDeck.Cards.Where(x => x.Active).ToList(),
                        Dbuser = denemy,
                        User = euser
                    }
                };

                var fight = _waifu.MakeFightAsync(players);
                string deathLog = _waifu.GetDeathLog(fight, players);

                var res = FightResult.Lose;
                if (fight.Winner == null)
                    res = FightResult.Draw;
                else if (fight.Winner.User.Id == duser.Id)
                    res = FightResult.Win;

                duser.GameDeck.PvPStats.Add(new CardPvPStats
                {
                    Type = FightType.NewVersus,
                    Result = res
                });

                var mission = duser.TimeStatuses.FirstOrDefault(x => x.Type == StatusType.DPvp);
                if (mission == null)
                {
                    mission = StatusType.DPvp.NewTimeStatus();
                    duser.TimeStatuses.Add(mission);
                }
                mission.Count(_time.Now());

                var info = duser.GameDeck.CalculatePVPParams(denemy.GameDeck, res);
                await db.SaveChangesAsync();

                _ = Task.Run(async () =>
                {
                    string wStr = fight.Winner == null ? "Remis!" : $"Zwycięża {fight.Winner.User.Mention}!";
                    await ReplyAsync("", embed: $"⚔️ **Pojedynek**:\n{Context.User.Mention} vs. {euser.Mention}\n\n{deathLog.TrimToLength(2000)}\n{wStr}\n{info}".ToEmbedMessage(EMType.Bot).Build());
                });
            }
        }

        [Command("galerianka")]
        [Alias("premium waifu")]
        [Summary("serio?, przekupia kartę by została twoją waifu, zrobi to za skromne 1000 TC")]
        [Remarks("12321"), RequireWaifuCommandChannel]
        public async Task SetCardAsPremiumWaifuAsync([Summary("WID karty")] ulong wid)
        {
            var tcCost = 1000;
            using (var db = new Database.DatabaseContext(Config))
            {
                var botuser = await db.GetUserOrCreateAsync(Context.User.Id);
                if (botuser.TcCnt < tcCost)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie posiadasz wystarczającej liczby TC!".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var thisCard = botuser.GameDeck.Cards.FirstOrDefault(x => x.Id == wid);
                var prevCard = botuser.GameDeck.Cards.FirstOrDefault(x => x.Id == botuser.GameDeck.PremiumWaifu);
                if (thisCard == null)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie odnaleziono karty.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (prevCard != null && prevCard.Character != thisCard.Character)
                {
                    prevCard.Affection -= 50;
                    _ = prevCard.CalculateCardPower();
                }

                if (botuser.GameDeck.Waifu != 0 && botuser.GameDeck.Waifu != thisCard.Character)
                {
                    var prevWaifus = botuser.GameDeck.Cards.Where(x => x.Character == botuser.GameDeck.Waifu);
                    foreach (var card in prevWaifus)
                    {
                        card.Affection -= 15;
                        _ = card.CalculateCardPower();
                    }
                }

                botuser.TcCnt -= tcCost;
                botuser.GameDeck.PremiumWaifu = thisCard.Id;
                botuser.GameDeck.Waifu = thisCard.Character;

                await db.SaveChangesAsync();

                await ReplyAsync("", embed: $"Kupiłeś waifu, gratulacje {Context.User.Mention}!".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("waifu")]
        [Alias("husbando")]
        [Summary("pozwala ustawić sobie ulubioną postać na profilu (musisz posiadać jej kartę)")]
        [Remarks("451"), RequireWaifuCommandChannel]
        public async Task SetProfileWaifuAsync([Summary("WID")] ulong wid)
        {
            using (var db = new Database.DatabaseContext(Config))
            {
                var bUser = await db.GetUserOrCreateAsync(Context.User.Id);
                if (wid == 0)
                {
                    if (bUser.GameDeck.Waifu != 0)
                    {
                        var prevWaifus = bUser.GameDeck.Cards.Where(x => x.Character == bUser.GameDeck.Waifu);
                        foreach (var card in prevWaifus)
                        {
                            card.Affection -= 5;
                            if (bUser.GameDeck.PremiumWaifu != 0)
                                card.Affection -= 50;

                            _ = card.CalculateCardPower();
                        }

                        bUser.GameDeck.Waifu = 0;
                        bUser.GameDeck.PremiumWaifu = 0;
                        await db.SaveChangesAsync();
                    }

                    await ReplyAsync("", embed: $"{Context.User.Mention} zresetował ulubioną karte.".ToEmbedMessage(EMType.Success).Build());
                    return;
                }

                var thisCard = bUser.GameDeck.Cards.FirstOrDefault(x => x.Id == wid && !x.InCage);
                if (thisCard == null)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie posiadasz takiej karty lub znajduje się ona w klatce!".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (bUser.GameDeck.Waifu == thisCard.Character)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} masz już ustawioną tą postać!".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var allPrevWaifus = bUser.GameDeck.Cards.Where(x => x.Character == bUser.GameDeck.Waifu);
                foreach (var card in allPrevWaifus)
                {
                    card.Affection -= 5;
                    if (bUser.GameDeck.PremiumWaifu != 0)
                        card.Affection -= 50;

                    _ = card.CalculateCardPower();
                }

                bUser.GameDeck.Waifu = thisCard.Character;
                bUser.GameDeck.PremiumWaifu = 0;
                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"user-{bUser.Id}", "users" });

                await ReplyAsync("", embed: $"{Context.User.Mention} ustawił {thisCard.Name} jako ulubioną postać.".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("ofiaruj")]
        [Alias("donate")]
        [Summary("ofiaruj 3 krople krwi, aby przeistoczyć kartę w anioła lub demona lub 12 aby zamienić je w boga (wymagany odpowiedni poziom karmy)")]
        [Remarks("451"), RequireWaifuCommandChannel]
        public async Task ChangeCardAsync([Summary("WID")] ulong wid)
        {
            if (_session.SessionExist(Context.User, typeof(ExchangeSession)))
            {
                await ReplyAsync("", embed: $"{Context.User.Mention} znajdujesz się obecnie w trakcie wymiany.".ToEmbedMessage(EMType.Error).Build());
                return;
            }

            using (var db = new Database.DatabaseContext(Config))
            {
                var bUser = await db.GetUserOrCreateAsync(Context.User.Id);
                if (!bUser.GameDeck.CanCreateDemon() && !bUser.GameDeck.CanCreateAngel())
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie jesteś zły, ani dobry - po prostu nijaki.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var thisCard = bUser.GameDeck.Cards.FirstOrDefault(x => x.Id == wid);
                if (thisCard == null)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} nie odnaleziono karty.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (thisCard.InCage)
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} ta karta znajduje się w klatce.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                if (!thisCard.CanGiveBloodOrUpgradeToSSS())
                {
                    await ReplyAsync("", embed: $"{Context.User.Mention} ta karta ma zbyt niską relacje".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var bloodCost = (thisCard.Dere == Dere.Yami || thisCard.Dere == Dere.Raito) ? 12 : 3;
                var activity = new UserActivityBuilder(_time)
                    .WithUser(bUser, Context.User).WithCard(thisCard);

                if (bUser.GameDeck.CanCreateDemon())
                {
                    if (thisCard.Dere == Dere.Yami)
                    {
                        await ReplyAsync("", embed: $"{Context.User.Mention} ta karta została już przeistoczona wcześniej.".ToEmbedMessage(EMType.Error).Build());
                        return;
                    }

                    var blood = bUser.GameDeck.Items.FirstOrDefault(x => x.Type == ItemType.BloodOfYourWaifu);
                    if (blood == null)
                    {
                        await ReplyAsync("", embed: $"{Context.User.Mention} o dziwo nie posiadasz kropli krwi twojej waifu.".ToEmbedMessage(EMType.Error).Build());
                        return;
                    }

                    if (blood.Count < bloodCost)
                    {
                        await ReplyAsync("", embed: $"{Context.User.Mention} o dziwo posiadasz za mało kropli krwi twojej waifu.".ToEmbedMessage(EMType.Error).Build());
                        return;
                    }

                    if (blood.Count > bloodCost) blood.Count -= bloodCost;
                    else bUser.GameDeck.Items.Remove(blood);

                    if (thisCard.Dere == Dere.Raito)
                    {
                        thisCard.Dere = Dere.Yato;
                        bUser.Stats.YatoUpgrades += 1;
                        activity.WithType(Database.Models.ActivityType.CreatedYato);
                    }
                    else
                    {
                        thisCard.Dere = Dere.Yami;
                        bUser.Stats.YamiUpgrades += 1;
                        activity.WithType(Database.Models.ActivityType.CreatedYami);
                    }
                }
                else if (bUser.GameDeck.CanCreateAngel())
                {
                    if (thisCard.Dere == Dere.Raito)
                    {
                        await ReplyAsync("", embed: $"{Context.User.Mention} ta karta została już przeistoczona wcześniej.".ToEmbedMessage(EMType.Error).Build());
                        return;
                    }

                    var blood = bUser.GameDeck.Items.FirstOrDefault(x => x.Type == ItemType.BetterIncreaseUpgradeCnt);
                    if (blood == null)
                    {
                        await ReplyAsync("", embed: $"{Context.User.Mention} o dziwo nie posiadasz kropli twojej krwi.".ToEmbedMessage(EMType.Error).Build());
                        return;
                    }

                    if (blood.Count < bloodCost)
                    {
                        await ReplyAsync("", embed: $"{Context.User.Mention} o dziwo posiadasz za mało kropli twojej krwi.".ToEmbedMessage(EMType.Error).Build());
                        return;
                    }

                    if (blood.Count > bloodCost) blood.Count -= bloodCost;
                    else bUser.GameDeck.Items.Remove(blood);


                    if (thisCard.Dere == Dere.Yami)
                    {
                        thisCard.Dere = Dere.Yato;
                        bUser.Stats.YatoUpgrades += 1;
                        activity.WithType(Database.Models.ActivityType.CreatedYato);
                    }
                    else
                    {
                        thisCard.Dere = Dere.Raito;
                        bUser.Stats.RaitoUpgrades += 1;
                        activity.WithType(Database.Models.ActivityType.CreatedRaito);
                    }
                }

                _ = thisCard.CalculateCardPower();

                await db.UserActivities.AddAsync(activity.Build());

                await db.SaveChangesAsync();
                QueryCacheManager.ExpireTag(new string[] { $"user-{bUser.Id}", "users" });

                await ReplyAsync("", embed: $"{Context.User.Mention} nowy charakter to {thisCard.Dere}".ToEmbedMessage(EMType.Success).Build());
            }
        }

        [Command("karcianka-", RunMode = RunMode.Async)]
        [Alias("cpf-")]
        [Summary("wyświetla uproszczony profil PocketWaifu")]
        [Remarks("Karna"), RequireAnyCommandChannelOrLevel(60)]
        public async Task ShowSimpleProfileAsync([Summary("nazwa użytkownika")] SocketGuildUser usr = null)
        {
            var user = (usr ?? Context.User) as SocketGuildUser;
            if (user == null) return;

            using (var db = new Database.DatabaseContext(Config))
            {
                var bUser = await db.GetCachedFullUserAsync(user.Id);
                if (bUser == null)
                {
                    await ReplyAsync("", embed: "Ta osoba nie ma profilu bota.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var seasonString = "----";
                if (bUser.GameDeck.IsPVPSeasonalRankActive(_time.Now()))
                    seasonString = $"{bUser.GameDeck.GetRankName()} ({bUser.GameDeck.SeasonalPVPRank})";
                var globalString = $"{bUser.GameDeck.GetRankName(bUser.GameDeck.GlobalPVPRank)} ({bUser.GameDeck.GlobalPVPRank})";

                var embed = new EmbedBuilder()
                {
                    Color = EMType.Bot.Color(),
                    Author = new EmbedAuthorBuilder().WithUser(user),
                    Description = $"*{bUser.GameDeck.GetUserNameStatus()}*\n\n"
                        + $"**Skrzynia({(int)bUser.GameDeck.ExpContainer.Level})**: {bUser.GameDeck.ExpContainer.ExpCount:F}\n"
                        + $"**CT**: {bUser.GameDeck.CTCnt}\n**Karma**: {bUser.GameDeck.Karma:F}\n\n**Posiadane karty**: {bUser.GameDeck.Cards.Count}\n"
                        + $"**GR**: {globalString}\n**SR**: {seasonString}"
                };

                if (bUser.GameDeck?.Waifu != 0)
                {
                    var tChar = bUser.GameDeck.GetWaifuCard();
                    if (tChar != null)
                    {
                        embed.WithFooter(new EmbedFooterBuilder().WithText($"{tChar.Name}"));
                    }
                }

                await ReplyAsync("", embed: embed.Build());
            }
        }

        [Command("karcianka", RunMode = RunMode.Async)]
        [Alias("cpf")]
        [Summary("wyświetla profil PocketWaifu")]
        [Remarks("Karna"), RequireWaifuCommandChannel]
        public async Task ShowProfileAsync([Summary("nazwa użytkownika")] SocketGuildUser usr = null)
        {
            var user = (usr ?? Context.User) as SocketGuildUser;
            if (user == null) return;

            using (var db = new Database.DatabaseContext(Config))
            {
                var bUser = await db.GetCachedFullUserAsync(user.Id);
                if (bUser == null)
                {
                    await ReplyAsync("", embed: "Ta osoba nie ma profilu bota.".ToEmbedMessage(EMType.Error).Build());
                    return;
                }

                var sssCnt = bUser.GameDeck.Cards.Count(x => x.Rarity == Rarity.SSS);
                var ssCnt = bUser.GameDeck.Cards.Count(x => x.Rarity == Rarity.SS);
                var sCnt = bUser.GameDeck.Cards.Count(x => x.Rarity == Rarity.S);
                var aCnt = bUser.GameDeck.Cards.Count(x => x.Rarity == Rarity.A);
                var bCnt = bUser.GameDeck.Cards.Count(x => x.Rarity == Rarity.B);
                var cCnt = bUser.GameDeck.Cards.Count(x => x.Rarity == Rarity.C);
                var dCnt = bUser.GameDeck.Cards.Count(x => x.Rarity == Rarity.D);
                var eCnt = bUser.GameDeck.Cards.Count(x => x.Rarity == Rarity.E);

                var resetCnt = bUser.GameDeck.Cards.Sum(x => x.RestartCnt);

                var aPvp = bUser.GameDeck?.PvPStats?.Count(x => x.Type == FightType.NewVersus);
                var wPvp = bUser.GameDeck?.PvPStats?.Count(x => x.Result == FightResult.Win && x.Type == FightType.NewVersus);

                var seasonString = "----";
                if (bUser.GameDeck.IsPVPSeasonalRankActive(_time.Now()))
                    seasonString = $"{bUser.GameDeck.GetRankName()} ({bUser.GameDeck.SeasonalPVPRank})";

                var globalString = $"{bUser.GameDeck.GetRankName(bUser.GameDeck.GlobalPVPRank)} ({bUser.GameDeck.GlobalPVPRank})";

                var sssString = "";
                if (sssCnt > 0)
                    sssString = $"**SSS**: {sssCnt} ";

                var embed = new EmbedBuilder()
                {
                    Color = EMType.Bot.Color(),
                    Author = new EmbedAuthorBuilder().WithUser(user),
                    Description = $"*{bUser.GameDeck.GetUserNameStatus()}*\n\n"
                                + $"**Skrzynia({(int)bUser.GameDeck.ExpContainer.Level})**: {bUser.GameDeck.ExpContainer.ExpCount:F}\n"
                                + $"**Uwolnione**: {bUser.Stats.ReleasedCards}\n**Zniszczone**: {bUser.Stats.DestroyedCards}\n**Poświęcone**: {bUser.Stats.SacraficeCards}\n**Ulepszone**: {bUser.Stats.UpgaredCards}\n**Wyzwolone**: {bUser.Stats.UnleashedCards}\n\n"
                                + $"**Restartów na kartach**: {resetCnt}\n\n"
                                + $"**CT**: {bUser.GameDeck.CTCnt}\n**Karma**: {bUser.GameDeck.Karma:F}\n\n**Posiadane karty**: {bUser.GameDeck.Cards.Count}\n"
                                + $"{sssString}**SS**: {ssCnt} **S**: {sCnt} **A**: {aCnt} **B**: {bCnt} **C**: {cCnt} **D**: {dCnt} **E**:{eCnt}\n\n"
                                + $"**PVP** Rozegrane: {aPvp} Wygrane: {wPvp}\n**GR**: {globalString}\n**SR**: {seasonString}"
                };

                if (bUser.GameDeck?.Waifu != 0)
                {
                    var tChar = bUser.GameDeck.GetWaifuCard();
                    if (tChar != null)
                    {
                        var config = await db.GetCachedGuildFullConfigAsync(Context.Guild.Id);
                        var channel = Context.Guild.GetTextChannel(config.WaifuConfig.TrashCommandsChannel);

                        embed.WithImageUrl(await _waifu.GetWaifuProfileImageAsync(tChar, channel));
                        embed.WithFooter(new EmbedFooterBuilder().WithText($"{tChar.Name}"));
                    }
                }

                await ReplyAsync("", embed: embed.Build());
            }
        }
    }
}
