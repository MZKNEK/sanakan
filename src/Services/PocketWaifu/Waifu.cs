#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services.PocketWaifu.Fight;
using Sanakan.Services.Time;
using AsyncKeyedLock;
using Shinden;
using Shinden.Logger;
using Shinden.Models;
using Z.EntityFramework.Plus;
using System.Threading;
using Sanakan.Config;

namespace Sanakan.Services.PocketWaifu
{
    public enum FightWinner
    {
        Card1, Card2, Draw
    }

    public enum HaremType
    {
        Rarity, Cage, Affection, Attack, Defence, Health, Tag, NoTag, Blocked, Broken, Picture, NoPicture, CustomPicture, Unique
    }

    public enum ShopType
    {
        Normal, Pvp, Activity
    }

    public enum CardImageType
    {
        Normal, Small, Profile
    }

    public enum ModifyTagActionType
    {
        Rename, Delete
    }

    public class Waifu
    {
        public const int FatigueFirstPhase = 550;
        public const int FatigueSecondPhase = 850;
        public const int FatigueThirdPhase = 1000;
        public const double FatigueRecoveryRate = 0.173;

        private AsyncNonKeyedLocker _semaphoreChars = new AsyncNonKeyedLocker(1);

        private const int DERE_TAB_SIZE = ((int)Dere.Yato) + 1;
        private static CharacterPool<ICharacterInfoTitle> _charIdAnime = new CharacterPool<ICharacterInfoTitle>();
        private static CharacterPool<ICharacterInfoTitle> _charIdManga = new CharacterPool<ICharacterInfoTitle>();
        private static CharacterPool<ICharacterInfoTitle> _charIdAll = new CharacterPool<ICharacterInfoTitle>();
        private DateTime _hiddeScalpelBeforeDate = new DateTime(2023, 5, 1);

        private static List<string> _qualityNamesList = Enum.GetNames(typeof(Quality)).ToList();

        private static Dictionary<string, Quality> _greekLetersAsQuality = new Dictionary<string, Quality>
        {
            { "alfa",       Quality.Alpha   },
            { "dzeta",      Quality.Zeta    },
            { "teta",       Quality.Theta   },
            { "iota",       Quality.Jota    },
            // { "my",         Quality.Mi      },
            // { "ny",         Quality.Ni      },
            // { "xi",         Quality.Ksi     },
            // { "ksy",        Quality.Ksi     },
            // { "ksey",       Quality.Ksi     },
            // { "omikron",    Quality.Omicron },
            // { "mikron",     Quality.Omicron },
            // { "pej",        Quality.Pi      },
            { "ipsilon",    Quality.Epsilon },
            { "ypsilon",    Quality.Epsilon },
            { "mega",       Quality.Omega   },
        };

        public static Dictionary<RecipeType, ItemRecipe> _recipes = new Dictionary<RecipeType, ItemRecipe>
        {
            { RecipeType.CrystalBall, new ItemRecipe(ItemType.CheckAffection.ToItem(),
                new List<Item>{ ItemType.CreationItemBase.ToItem(), ItemType.CardParamsReRoll.ToItem(), ItemType.DereReRoll.ToItem() },
                new List<CurrencyCost> { new CurrencyCost(5, CurrencyType.CT) })
            },
            { RecipeType.BloodyMarry, new ItemRecipe(ItemType.RemoveCurse.ToItem(),
                new List<Item>{ ItemType.CreationItemBase.ToItem(10), ItemType.BetterIncreaseUpgradeCnt.ToItem(5), ItemType.BloodOfYourWaifu.ToItem(5) })
            },
            { RecipeType.YourBlood, new ItemRecipe(ItemType.BetterIncreaseUpgradeCnt.ToItem(),
                new List<Item>{ ItemType.CreationItemBase.ToItem(), ItemType.BloodOfYourWaifu.ToItem(2) })
            },
            { RecipeType.WaifuBlood, new ItemRecipe(ItemType.BloodOfYourWaifu.ToItem(),
                new List<Item>{ ItemType.CreationItemBase.ToItem(), ItemType.BetterIncreaseUpgradeCnt.ToItem(2) })
            },
            { RecipeType.DereChange, new ItemRecipe(ItemType.DereReRoll.ToItem(5),
                new List<Item>{ ItemType.CreationItemBase.ToItem(), ItemType.CardParamsReRoll.ToItem(5) })
            },
            { RecipeType.StatsChange, new ItemRecipe(ItemType.CardParamsReRoll.ToItem(5),
                new List<Item>{ ItemType.CreationItemBase.ToItem(), ItemType.DereReRoll.ToItem(5) })
            },
            { RecipeType.CheckCurse, new ItemRecipe(ItemType.CheckCurse.ToItem(),
                new List<Item>{ ItemType.CreationItemBase.ToItem(), ItemType.CheckAffection.ToItem() })
            }
        };

        private static readonly AsyncKeyedLocker<ulong> _cardGenLocker = new AsyncKeyedLocker<ulong>(x =>
        {
            x.PoolSize = 100;
            x.PoolInitialFill = 5;
            x.MaxCount = 1;
        });

        private static string[] _imgExtWithAlpha = { "png", "webp", "gif" };

        private static List<Dere> _dereToRandomize = new List<Dere>
        {
            Dere.Tsundere,
            Dere.Kamidere,
            Dere.Deredere,
            Dere.Yandere,
            Dere.Dandere,
            Dere.Kuudere,
            Dere.Mayadere,
            Dere.Bodere
        };

        private static double[,] _dereDmgRelation = new double[DERE_TAB_SIZE, DERE_TAB_SIZE]
        {
            //Tsundere, Kamidere, Deredere, Yandere, Dandere, Kuudere, Mayadere, Bodere, Yami, Raito, Yato
            { 0.5,      2,        2,        2,       2,       2,       2,        2,      3,    3,     3     }, //Tsundere
            { 1,        0.5,      2,        0.5,     1,       1,       1,        1,      2,    1,     2     }, //Kamidere
            { 1,        1,        0.5,      2,       0.5,     1,       1,        1,      2,    1,     2     }, //Deredere
            { 1,        1,        1,        0.5,     2,       0.5,     1,        1,      2,    1,     2     }, //Yandere
            { 1,        1,        1,        1,       0.5,     2,       0.5,      1,      2,    1,     2     }, //Dandere
            { 1,        1,        1,        1,       1,       0.5,     2,        0.5,    2,    1,     2     }, //Kuudere
            { 1,        0.5,      1,        1,       1,       1,       0.5,      2,      2,    1,     2     }, //Mayadere
            { 1,        2,        0.5,      1,       1,       1,       1,        0.5,    2,    1,     2     }, //Bodere
            { 1,        1,        1,        1,       1,       1,       1,        1,      0.5,  3,     2     }, //Yami
            { 0.5,      0.5,      0.5,      0.5,     0.5,     0.5,     0.5,      0.5,    3,    0.5,   1     }, //Raito
            { 0.5,      0.5,      0.5,      0.5,     0.5,     0.5,     0.5,      0.5,    1,    0.5,   1     }, //Yato
        };

        private Timer _timer;
        private Events _events;
        private Helper _helper;
        private ILogger _logger;
        private IConfig _config;
        private TagHelper _tags;
        private Shinden _shinden;
        private ISystemTime _time;
        private ImageProcessing _img;
        private Expedition _expedition;
        private ShindenClient _shClient;
        private DiscordSocketClient _client;

        public Waifu(ImageProcessing img, ShindenClient client, Events events, ILogger logger, Expedition expedition,
            DiscordSocketClient discord, Helper helper, ISystemTime time, Shinden shinden, TagHelper tags, IConfig config)
        {
            _img = img;
            _time = time;
            _tags = tags;
            _config = config;
            _events = events;
            _logger = logger;
            _helper = helper;
            _client = discord;
            _shinden = shinden;
            _shClient = client;
            _expedition = expedition;

            _timer = new Timer(_ =>
            {
                try
                {
                    if (_config.Get().AutoCleanCards)
                    {
                        _ = CleanCards(5);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Log($"in waifu - clean cards: {ex}");
                }
            },
            null,
            TimeSpan.FromDays(1),
            TimeSpan.FromDays(5));
        }

        static public double GetDereDmgMultiplier(Card atk, Card def) => _dereDmgRelation[(int)def.Dere, (int)atk.Dere];

        public ItemRecipe GetItemRecipe(RecipeType type) => _recipes[type];

        public string GetItemRecipesList() => $"**Przepisy**:\n\n{string.Join("\n", _recipes.Select((x, i) => $"**[{i+1}]** {x.Value.Name}"))}";

        public List<string> CleanCards(int cardsOldInDays)
        {
            var deletedImages = new List<string>();
            var dateToCheck = _time.Now().AddDays(-cardsOldInDays);
            foreach (var filePath in Directory.GetFiles(Dir.Cards))
            {
                try
                {
                    var isInvalidPath = Path.GetExtension(filePath) switch
                    {
                        ".png" => false,
                        ".PNG" => false,
                        ".webp" => false,
                        ".WEBP" => false,
                        _ => true
                    };

                    if (isInvalidPath)
                        continue;

                    var createdAt = File.GetCreationTime(filePath);
                    if (createdAt < dateToCheck)
                    {
                        var fileName = Path.GetFileName(filePath);
                        var miniFilePath = $"{Dir.CardsMiniatures}/{fileName}";
                        var profileFilePath = $"{Dir.CardsInProfiles}/{fileName}";

                        File.Delete(filePath);
                        deletedImages.Add(filePath);
                        File.Delete(miniFilePath);
                        deletedImages.Add(miniFilePath);
                        File.Delete(profileFilePath);
                        deletedImages.Add(profileFilePath);

                    }
                }
                catch (Exception) { }
            }
            return deletedImages;
        }

        public List<Card> GetListInRightOrder(IEnumerable<Card> list, HaremType type, string tag)
        {
            switch (type)
            {
                case HaremType.Health:
                    return list.OrderByDescending(x => x.GetHealthWithPenalty()).ToList();

                case HaremType.Affection:
                    return list.OrderByDescending(x => x.Affection).ToList();

                case HaremType.Attack:
                    return list.OrderByDescending(x => x.GetAttackWithBonus()).ToList();

                case HaremType.Defence:
                    return list.OrderByDescending(x => x.GetDefenceWithBonus()).ToList();

                case HaremType.Unique:
                    return list.Where(x => x.Unique).ToList();

                case HaremType.Cage:
                    return list.Where(x => x.InCage).ToList();

                case HaremType.Blocked:
                    return list.Where(x => !x.IsTradable).ToList();

                case HaremType.Broken:
                    return list.Where(x => x.IsBroken()).ToList();

                case HaremType.Tag:
                    {
                        var nList = new List<Card>();
                        var tagList = tag.Split("|").First().Split(" ").ToList();
                        foreach (var t in tagList)
                        {
                            if (t.Length < 1)
                                continue;

                            nList = list.Where(x => x.Tags.Any(c => c.Name.Equals(t, StringComparison.CurrentCultureIgnoreCase))).ToList();
                        }
                        return nList;
                    }

                case HaremType.NoTag:
                    {
                        var nList = new List<Card>();
                        var tagList = tag.Split("|").First().Split(" ").ToList();
                        foreach (var t in tagList)
                        {
                            if (t.Length < 1)
                                continue;

                            nList = list.Where(x => !x.Tags.Any(c => c.Name.Equals(t, StringComparison.CurrentCultureIgnoreCase))).ToList();
                        }
                        return nList;
                    }

                case HaremType.Picture:
                    return list.Where(x => x.HasImage()).ToList();

                case HaremType.NoPicture:
                    return list.Where(x => x.Image == null).ToList();

                case HaremType.CustomPicture:
                    return list.Where(x => x.CustomImage != null).ToList();

                default:
                case HaremType.Rarity:
                    return list.OrderBy(x => x.Rarity).ToList();
            }
        }

        static public Rarity RandomizeRarity()
        {
            var num = Fun.GetRandomValue(1000);
            if (num < 5) return Rarity.SS;
            if (num < 25) return Rarity.S;
            if (num < 75) return Rarity.A;
            if (num < 175) return Rarity.B;
            if (num < 370) return Rarity.C;
            if (num < 620) return Rarity.D;
            return Rarity.E;
        }

        public Rarity RandomizeRarity(List<Rarity> rarityExcluded)
        {
            if (rarityExcluded == null) return RandomizeRarity();
            if (rarityExcluded.Count < 1) return RandomizeRarity();

            var list = new List<RarityChance>()
            {
                new RarityChance(5,    Rarity.SS),
                new RarityChance(25,   Rarity.S ),
                new RarityChance(75,   Rarity.A ),
                new RarityChance(175,  Rarity.B ),
                new RarityChance(370,  Rarity.C ),
                new RarityChance(650,  Rarity.D ),
                new RarityChance(1000, Rarity.E ),
            };

            var ex = list.Where(x => rarityExcluded.Any(c => c == x.Rarity)).ToList();
            foreach (var e in ex) list.Remove(e);

            var num = Fun.GetRandomValue(1000);
            foreach (var rar in list)
            {
                if (num < rar.Chance)
                    return rar.Rarity;
            }
            return list.Last().Rarity;
        }

        public ItemType RandomizeItemFromBlackMarket()
        {
            var num = Fun.GetRandomValue(1000);
            if (num < 2) return ItemType.IncreaseExpSmall;
            if (num < 8) return ItemType.BetterIncreaseUpgradeCnt;
            if (num < 22) return ItemType.IncreaseUpgradeCnt;
            if (num < 70) return ItemType.AffectionRecoveryGreat;
            if (num < 120) return ItemType.AffectionRecoveryBig;
            if (num < 180) return ItemType.CardParamsReRoll;
            if (num < 250) return ItemType.DereReRoll;
            if (num < 780) return ItemType.AffectionRecoveryNormal;
            return ItemType.AffectionRecoverySmall;
        }

        public ItemType RandomizeItemFromMarket()
        {
            var num = Fun.GetRandomValue(1000);
            if (num < 2) return ItemType.IncreaseExpSmall;
            if (num < 15) return ItemType.IncreaseUpgradeCnt;
            if (num < 80) return ItemType.AffectionRecoveryBig;
            if (num < 145) return ItemType.CardParamsReRoll;
            if (num < 230) return ItemType.DereReRoll;
            if (num < 480) return ItemType.AffectionRecoveryNormal;
            return ItemType.AffectionRecoverySmall;
        }

        public ItemWithCost[] GetItemsWithCost()
        {
            return new ItemWithCost[]
            {
                new ItemWithCost(3,     ItemType.AffectionRecoverySmall.ToItem()),
                new ItemWithCost(14,    ItemType.AffectionRecoveryNormal.ToItem()),
                new ItemWithCost(109,   ItemType.AffectionRecoveryBig.ToItem()),
                new ItemWithCost(29,    ItemType.DereReRoll.ToItem()),
                new ItemWithCost(79,    ItemType.CardParamsReRoll.ToItem()),
                new ItemWithCost(1099,  ItemType.IncreaseUpgradeCnt.ToItem()),
                new ItemWithCost(69,    ItemType.ChangeCardImage.ToItem()),
                new ItemWithCost(999,   ItemType.SetCustomImage.ToItem()),
                new ItemWithCost(659,   ItemType.SetCustomBorder.ToItem()),
                new ItemWithCost(149,   ItemType.ChangeStarType.ToItem()),
                new ItemWithCost(104,   ItemType.RandomBoosterPackSingleE.ToItem()),
                new ItemWithCost(989,   ItemType.BigRandomBoosterPackE.ToItem()),
                new ItemWithCost(1409,  ItemType.RandomTitleBoosterPackSingleE.ToItem()),
                new ItemWithCost(159,   ItemType.RandomNormalBoosterPackB.ToItem()),
                new ItemWithCost(199,   ItemType.RandomNormalBoosterPackA.ToItem()),
                new ItemWithCost(249,   ItemType.RandomNormalBoosterPackS.ToItem()),
                new ItemWithCost(399,   ItemType.RandomNormalBoosterPackSS.ToItem()),
                new ItemWithCost(369,   ItemType.ResetCardValue.ToItem()),
                new ItemWithCost(9999,  ItemType.SetCustomAnimatedImage.ToItem()),
                new ItemWithCost(444,   ItemType.GiveTagSlot.ToItem()),
                new ItemWithCost(99999, ItemType.NotAnItem.ToItem()),
            };
        }

        public ItemWithCost[] GetItemsWithCostForPVP()
        {
            return new ItemWithCost[]
            {
                new ItemWithCost(69,    ItemType.AffectionRecoveryNormal.ToItem()),
                new ItemWithCost(699,   ItemType.IncreaseExpBig.ToItem()),
                new ItemWithCost(1699,  ItemType.CheckAffection.ToItem()),
                new ItemWithCost(9999,  ItemType.IncreaseUpgradeCnt.ToItem()),
                new ItemWithCost(11999, ItemType.BetterIncreaseUpgradeCnt.ToItem()),
                new ItemWithCost(1699,  ItemType.ChangeCardImage.ToItem()),
                new ItemWithCost(32999, ItemType.SetCustomImage.ToItem()),
                new ItemWithCost(11999, ItemType.IncreaseUltimateAll.ToItem()),
                new ItemWithCost(1469,  ItemType.CreationItemBase.ToItem()),
                new ItemWithCost(11999, ItemType.BloodOfYourWaifu.ToItem()),
                new ItemWithCost(1599,  ItemType.ChangeStarType.ToItem()),
                new ItemWithCost(11999, ItemType.RandomBoosterPackSingleE.ToItem()),
            };
        }

        public ItemWithCost[] GetItemsWithCostForActivityShop()
        {
            return new ItemWithCost[]
            {
                new ItemWithCost(6,     ItemType.AffectionRecoveryBig.ToItem()),
                new ItemWithCost(35,    ItemType.IncreaseExpBig.ToItem()),
                new ItemWithCost(395,   ItemType.IncreaseUpgradeCnt.ToItem()),
                new ItemWithCost(1665,  ItemType.SetCustomImage.ToItem()),
                new ItemWithCost(165,   ItemType.RandomBoosterPackSingleE.ToItem()),
                new ItemWithCost(1500,  ItemType.BigRandomBoosterPackE.ToItem()),
                new ItemWithCost(125,   ItemType.CreationItemBase.ToItem()),
                new ItemWithCost(921,   ItemType.SetCustomBorder.ToItem()),
            };
        }

        public ItemWithCost[] GetItemsWithCostForShop(ShopType type)
        {
            switch (type)
            {
                case ShopType.Activity:
                    return GetItemsWithCostForActivityShop();

                case ShopType.Pvp:
                    return GetItemsWithCostForPVP();

                case ShopType.Normal:
                default:
                    return GetItemsWithCost();
            }
        }

        public CardSource GetBoosterpackSource(ShopType type)
        {
            switch (type)
            {
                case ShopType.Activity:
                    return CardSource.ActivityShop;

                case ShopType.Pvp:
                    return CardSource.PvpShop;

                default:
                case ShopType.Normal:
                    return CardSource.Shop;
            }
        }

        public string GetShopName(ShopType type)
        {
            switch (type)
            {
                case ShopType.Activity:
                    return "Kiosk";

                case ShopType.Pvp:
                    return "Koszary";

                case ShopType.Normal:
                default:
                    return "Sklepik";
            }
        }

        public string GetShopCurrencyName(ShopType type)
        {
            switch (type)
            {
                case ShopType.Activity:
                    return "AC";

                case ShopType.Pvp:
                    return "PC";

                case ShopType.Normal:
                default:
                    return "TC";
            }
        }

        public void IncreaseMoneySpentOnCookies(ShopType type, User user, int cost)
        {
            switch (type)
            {
                case ShopType.Normal:
                    user.Stats.WastedTcOnCookies += cost;
                    break;

                case ShopType.Pvp:
                    user.Stats.WastedPuzzlesOnCookies += cost;
                    break;

                case ShopType.Activity:
                    user.Stats.WastedActivityOnCookies += cost;
                    break;

                default:
                    break;
            }
        }

        public void IncreaseMoneySpentOnCards(ShopType type, User user, int cost)
        {
            switch (type)
            {
                case ShopType.Normal:
                    user.Stats.WastedTcOnCards += cost;
                    break;

                case ShopType.Pvp:
                    user.Stats.WastedPuzzlesOnCardsReal += cost;
                    break;

                case ShopType.Activity:
                    user.Stats.WastedActivityOnCards += cost;
                    break;

                default:
                    break;
            }
        }

        public void RemoveMoneyFromUser(ShopType type, User user, int cost)
        {
            switch (type)
            {
                case ShopType.Normal:
                    user.TcCnt -= cost;
                    break;

                case ShopType.Pvp:
                    user.GameDeck.PVPCoins -= cost;
                    break;

                case ShopType.Activity:
                    user.AcCnt -= cost;
                    break;

                default:
                    break;
            }
        }

        public bool CheckIfUserCanBuy(ShopType type, User user, int cost)
        {
            switch (type)
            {
                case ShopType.Normal:
                    return user.TcCnt >= cost;

                case ShopType.Pvp:
                    return user.GameDeck.PVPCoins >= cost;

                case ShopType.Activity:
                    return user.AcCnt >= cost;

                default:
                    return false;
            }
        }

        public async Task<Embed> ExecuteShopAsync(ShopType type, Config.IConfig config, IUser discordUser, int selectedItem, string specialCmd)
        {
            var itemsToBuy = GetItemsWithCostForShop(type);
            if (selectedItem <= 0)
            {
                return GetShopView(itemsToBuy, GetShopName(type), GetShopCurrencyName(type));
            }

            if (selectedItem > itemsToBuy.Length)
            {
                return $"{discordUser.Mention} nie odnaleznino takiego przedmiotu do zakupu.".ToEmbedMessage(EMType.Error).Build();
            }

            var thisItem = itemsToBuy[--selectedItem];
            if (specialCmd == "info")
            {
                return GetItemShopInfo(thisItem);
            }

            bool multipleParams = false;
            int itemCount = 0;
            int titleId = 0;

            if (specialCmd.Contains(":"))
            {
                multipleParams = true;
                var moreNumbers = specialCmd.Split(":");
                if (moreNumbers.Length > 1)
                {
                    if (!int.TryParse(moreNumbers[1], out itemCount))
                    {
                        return $"{discordUser.Mention} liczbę poproszę, a nie jakieś bohomazy.".ToEmbedMessage(EMType.Error).Build();
                    }

                    if (!int.TryParse(moreNumbers[0], out titleId))
                    {
                        return $"{discordUser.Mention} liczbę poproszę, a nie jakieś bohomazy.".ToEmbedMessage(EMType.Error).Build();
                    }
                }
            }
            else
            {
                if (!int.TryParse(specialCmd, out itemCount))
                {
                    return $"{discordUser.Mention} liczbę poproszę, a nie jakieś bohomazy.".ToEmbedMessage(EMType.Error).Build();
                }
                titleId = itemCount;
            }

            if (itemCount < 1)
                itemCount = 1;

            bool isWrongType = false;
            ulong boosterPackTitleId = 0;
            string boosterPackTitleName = "";
            const int minCharactersInPack = 8;
            switch (thisItem.Item.Type)
            {
                case ItemType.RandomTitleBoosterPackSingleE:
                    if (titleId < 1)
                    {
                        return $"{discordUser.Mention} niepoprawne id serii.".ToEmbedMessage(EMType.Error).Build();
                    }

                    if (!multipleParams)
                        itemCount = 1;

                    var titleInfo = await _shinden.GetInfoFromTitleAsync((ulong)titleId);
                    if (titleInfo == null)
                    {
                        return $"{discordUser.Mention} nie odnaleziono tytułu o podanym id.".ToEmbedMessage(EMType.Error).Build();
                    }
                    if (titleInfo.TagCategories.Any(x => x.Tags.Any(x => x.Name.Equals("Reklama", StringComparison.InvariantCultureIgnoreCase))))
                    {
                        return $"{discordUser.Mention} tytuł jest reklamą.".ToEmbedMessage(EMType.Error).Build();
                    }
                    if (titleInfo is IAnimeTitleInfo ati)
                    {
                        isWrongType = ati.Type switch
                        {
                            AnimeType.NotSpecified  => true,
                            AnimeType.Music         => true,
                            _ => false
                        };
                    }
                    if (isWrongType)
                    {
                        return $"{discordUser.Mention} tytuł posiada niepoprawny typ.".ToEmbedMessage(EMType.Error).Build();
                    }
                    var charsInTitle = await _shinden.GetCharactersFromTitleAsync(titleInfo.Id);
                    if (charsInTitle == null || charsInTitle.Count < 1)
                    {
                        return $"{discordUser.Mention} nie odnaleziono postaci pod podanym tytułem.".ToEmbedMessage(EMType.Error).Build();
                    }
                    if (charsInTitle.Select(x => x.CharacterId).Where(x => x.HasValue).Distinct().Count() < minCharactersInPack)
                    {
                        return $"{discordUser.Mention} nie można kupić pakietu z tytułu z mniejszą liczbą postaci jak {minCharactersInPack}.".ToEmbedMessage(EMType.Error).Build();
                    }
                    boosterPackTitleName = $" ({titleInfo.Title})";
                    boosterPackTitleId = titleInfo.Id;
                    break;

                case ItemType.PreAssembledAsuna:
                case ItemType.PreAssembledGintoki:
                case ItemType.PreAssembledMegumin:
                    if (itemCount > 1)
                    {
                        return $"{discordUser.Mention} można kupić tylko jeden taki przedmiot.".ToEmbedMessage(EMType.Error).Build();
                    }
                    break;

                default:
                    break;
            }

            var realCost = itemCount * thisItem.Cost;
            string count = (itemCount > 1) ? $" x{itemCount}" : "";

            using (var db = new Database.DatabaseContext(config))
            {
                var bUser = await db.GetUserOrCreateAsync(discordUser.Id);
                if (!CheckIfUserCanBuy(type, bUser, realCost))
                {
                    return $"{discordUser.Mention} nie posiadasz wystarczającej liczby {GetShopCurrencyName(type)}!".ToEmbedMessage(EMType.Error).Build();
                }

                if (thisItem.Item.Type.IsBoosterPack())
                {
                    for (int i = 0; i < itemCount; i++)
                    {
                        var booster = thisItem.Item.Type.ToBoosterPack();
                        if (boosterPackTitleId != 0)
                        {
                            booster.Title = boosterPackTitleId;
                            booster.Name += boosterPackTitleName;
                        }
                        if (booster != null)
                        {
                            booster.CardSourceFromPack = GetBoosterpackSource(type);
                            bUser.GameDeck.BoosterPacks.Add(booster);
                        }
                    }

                    IncreaseMoneySpentOnCards(type, bUser, realCost);
                }
                else if (thisItem.Item.Type.IsPreAssembledFigure())
                {
                    if (bUser.GameDeck.Figures.Any(x => x.PAS == thisItem.Item.Type.ToPASType()))
                    {
                        return $"{discordUser.Mention} masz już taką figurkę.".ToEmbedMessage(EMType.Error).Build();
                    }

                    var figure = thisItem.Item.Type.ToPAFigure(_time.Now());
                    if (figure != null) bUser.GameDeck.Figures.Add(figure);

                    IncreaseMoneySpentOnCards(type, bUser, realCost);
                }
                else
                {
                    bUser.GameDeck.AddItem(thisItem.Item.Type.ToItem(itemCount, thisItem.Item.Quality));
                    IncreaseMoneySpentOnCookies(type, bUser, realCost);
                }

                RemoveMoneyFromUser(type, bUser, realCost);

                await db.SaveChangesAsync();

                QueryCacheManager.ExpireTag(new string[] { $"user-{bUser.Id}", "users" });

                return $"{discordUser.Mention} zakupił: _{thisItem.Item.Name}{boosterPackTitleName}{count}_.".ToEmbedMessage(EMType.Success).Build();
            }
        }

        public double GetExpToUpgrade(Card toUp, Card toSac)
        {
            double rExp = 30f / 5f;

            if (toUp.Character == toSac.Character)
                rExp *= 10f;

            var sacVal = (int)toSac.Rarity;
            var upVal = (int)toUp.Rarity;
            var diff = upVal - sacVal;

            if (diff < 0)
            {
                diff = -diff;
                for (int i = 0; i < diff; i++) rExp /= 2;
            }
            else if (diff > 0)
            {
                for (int i = 0; i < diff; i++) rExp *= 1.5;
            }

            if (toUp.Curse == CardCurse.LoweredExperience || toSac.Curse == CardCurse.LoweredExperience)
                rExp /= 5;

            return rExp;
        }

        static public FightWinner GetFightWinner(Card card1, Card card2)
        {
            var FAcard1 = GetFA(card1, card2);
            var FAcard2 = GetFA(card2, card1);

            var c1Health = card1.GetHealthWithPenalty();
            var c2Health = card2.GetHealthWithPenalty();
            var atkTk1 = c1Health / FAcard2;
            var atkTk2 = c2Health / FAcard1;

            var winner = FightWinner.Draw;
            if (atkTk1 > atkTk2 + 0.3) winner = FightWinner.Card1;
            if (atkTk2 > atkTk1 + 0.3) winner = FightWinner.Card2;

            return winner;
        }

        static public double GetFA(Card target, Card enemy)
        {
            double atk1 = target.GetAttackWithBonus();
            if (!target.HasImage()) atk1 -= atk1 * 20 / 100;

            double def2 = enemy.GetDefenceWithBonus();
            if (!enemy.HasImage()) def2 -= def2 * 20 / 100;

            var realAtk1 = atk1 - def2;
            if (!target.FromFigure && !enemy.FromFigure)
            {
                if (def2 > 99) def2 = 99;
                realAtk1 = atk1 * (100 - def2) / 100;
            }
            if (realAtk1 < 1) realAtk1 = 1;

            realAtk1 *= GetDereDmgMultiplier(target, enemy);

            return realAtk1;
        }

        static public int RandomizeAttack(Rarity rarity)
            => Fun.GetRandomValue(rarity.GetAttackMin(), rarity.GetAttackMax() + 1);

        static public int RandomizeDefence(Rarity rarity)
            => Fun.GetRandomValue(rarity.GetDefenceMin(), rarity.GetDefenceMax() + 1);

        static public int RandomizeHealth(Card card)
            => Fun.GetRandomValue(Math.Min(card.Rarity.GetHealthMin(), card.GetHealthMax() + 1),
                Math.Max(card.Rarity.GetHealthMin(), card.GetHealthMax() + 1));

        static public Dere RandomizeDere() => Fun.GetOneRandomFrom(_dereToRandomize);

        static public Card GenerateNewCard(string name, string title, string image, Rarity rarity,
            DateTime creationTime)
        {
            var card = new Card
            {
                Defence = RandomizeDefence(rarity),
                Attack = RandomizeAttack(rarity),
                Expedition = CardExpedition.None,
                ExpeditionEndDate = creationTime,
                QualityOnStart = Quality.Broken,
                CustomImageDate = creationTime,
                ExpeditionDate = creationTime,
                PAS = PreAssembledFigure.None,
                CreationDate = creationTime,
                StarStyle = StarStyle.Full,
                Source = CardSource.Other,
                Quality = Quality.Broken,
                FixedCustomImageCnt = 0,
                IsAnimatedImage = false,
                Title = title ?? "????",
                Tags = new List<Tag>(),
                Dere = RandomizeDere(),
                Curse = CardCurse.None,
                RarityOnStart = rarity,
                CustomBorder = null,
                FromFigure = false,
                CustomImage = null,
                IsTradable = true,
                WhoWantsCount = 0,
                FirstIdOwner = 1,
                DefenceBonus = 0,
                RateNegative = 0,
                RatePositive = 0,
                HealthBonus = 0,
                AttackBonus = 0,
                UpgradesCnt = 2,
                LastIdOwner = 0,
                MarketValue = 1,
                Rarity = rarity,
                EnhanceCnt = 0,
                Unique = false,
                InCage = false,
                RestartCnt = 0,
                Active = false,
                CardPower = 0,
                Character = 0,
                Affection = 0,
                Image = null,
                FigureId = 0,
                Fatigue = 0,
                Name = name,
                Health = 0,
                ExpCnt = 0,
            };

            if (!string.IsNullOrEmpty(image))
                card.Image = image;

            card.Health = RandomizeHealth(card);

            _ = card.CalculateCardPower();

            return card;
        }

        public Card GenerateNewCard(IUser user, ICharacterInfo character, Rarity rarity, Quality quality)
        {
            var card = GenerateNewCard(user, character, (quality != Quality.Broken) ? Rarity.SSS : rarity);

            card.FromFigure = quality != Quality.Broken;
            card.QualityOnStart = quality;
            card.Quality = quality;

            return card;
        }

        public Card GenerateNewCard(IUser user, ICharacterInfoTitle character, Rarity rarity, Quality quality)
        {
            var card = GenerateNewCard(user, character, (quality != Quality.Broken) ? Rarity.SSS : rarity);

            card.FromFigure = quality != Quality.Broken;
            card.QualityOnStart = quality;
            card.Quality = quality;

            return card;
        }

        public Card GenerateNewCard(IUser user, ICharacterInfoTitle character, Rarity rarity)
        {
            var card = GenerateNewCard(character.ToString(), character.TitleName,
                character.HasImage ? character.PictureUrl : string.Empty, rarity, _time.Now());

            card.Character = character.Id;

            if (user != null)
                card.FirstIdOwner = user.Id;

            return card;
        }

        public Card GenerateNewCard(IUser user, ICharacterInfo character, Rarity rarity)
        {
            var card = GenerateNewCard(character.ToString(),
                character?.Relations?.OrderBy(x => x.Id)?.FirstOrDefault()?.Title ?? "????",
                character.HasImage ? character.PictureUrl : string.Empty, rarity, _time.Now());

            card.Character = character.Id;

            if (user != null)
                card.FirstIdOwner = user.Id;

            return card;
        }

        public Card GenerateNewCard(IUser user, string characterName, string titleName, ulong charId)
        {
            var card = GenerateNewCard(characterName, titleName, string.Empty, RandomizeRarity(), _time.Now());

            card.Character = charId;

            if (user != null)
                card.FirstIdOwner = user.Id;

            return card;
        }

        public Card GenerateNewCard(IUser user, ICharacterInfoTitle character)
            => GenerateNewCard(user, character, RandomizeRarity());

        public Card GenerateNewCard(IUser user, ICharacterInfo character)
            => GenerateNewCard(user, character, RandomizeRarity());

        public Card GenerateNewCard(IUser user, ICharacterInfoTitle character, List<Rarity> rarityExcluded)
            => GenerateNewCard(user, character, RandomizeRarity(rarityExcluded));

        public Card GenerateNewCard(IUser user, ICharacterInfo character, List<Rarity> rarityExcluded)
            => GenerateNewCard(user, character, RandomizeRarity(rarityExcluded));

        private int ScaleNumber(int oMin, int oMax, int nMin, int nMax, int value)
        {
            var m = (double)(nMax - nMin) / (double)(oMax - oMin);
            var c = (oMin * m) - nMin;

            return (int)((m * value) - c);
        }

        public int GetAttactAfterLevelUp(Rarity oldRarity, int oldAtk)
        {
            var newRarity = oldRarity - 1;
            var newMax = newRarity.GetAttackMax();
            var newMin = newRarity.GetAttackMin();
            var range = newMax - newMin;

            var oldMax = oldRarity.GetAttackMax();
            var oldMin = oldRarity.GetAttackMin();

            var relNew = ScaleNumber(oldMin, oldMax, newMin, newMax, oldAtk);
            var relMin = relNew - (range * 6 / 100);
            var relMax = relNew + (range * 8 / 100);

            var nAtk = Fun.GetRandomValue(relMin, relMax + 1);
            if (nAtk > newMax) nAtk = newMax;
            if (nAtk < newMin) nAtk = newMin;

            return nAtk;
        }

        public int GetDefenceAfterLevelUp(Rarity oldRarity, int oldDef)
        {
            var newRarity = oldRarity - 1;
            var newMax = newRarity.GetDefenceMax();
            var newMin = newRarity.GetDefenceMin();
            var range = newMax - newMin;

            var oldMax = oldRarity.GetDefenceMax();
            var oldMin = oldRarity.GetDefenceMin();

            var relNew = ScaleNumber(oldMin, oldMax, newMin, newMax, oldDef);
            var relMin = relNew - (range * 6 / 100);
            var relMax = relNew + (range * 8 / 100);

            var nDef = Fun.GetRandomValue(relMin, relMax + 1);
            if (nDef > newMax) nDef = newMax;
            if (nDef < newMin) nDef = newMin;

            return nDef;
        }

        private double GetDmgDeal(Card c1, Card c2)
        {
            return GetFA(c1, c2);
        }

        public string GetDeathLog(FightHistory fight, List<PlayerInfo> players)
        {
            string deathLog = "";
            for (int i = 0; i < fight.Rounds.Count; i++)
            {
                var dead = fight.Rounds[i].Cards.Where(x => x.Hp <= 0);
                if (dead.Count() > 0)
                {
                    deathLog += $"**Runda {i + 1}**:\n";
                    foreach (var d in dead)
                    {
                        var thisCard = players.First(x => x.Cards.Any(c => c.Id == d.CardId)).Cards.First(x => x.Id == d.CardId);
                        deathLog += $"❌ {thisCard.GetString(true, false, true, true)}\n";
                    }
                    deathLog += "\n";
                }
            }
            return deathLog;
        }

        public FightHistory MakeFightAsync(List<PlayerInfo> players, bool oneCard = false)
        {
            var totalCards = new List<CardWithHealth>();

            foreach (var player in players)
            {
                foreach (var card in player.Cards)
                    totalCards.Add(new CardWithHealth() { Card = card, Health = card.GetHealthWithPenalty() });
            }

            var rounds = new List<RoundInfo>();
            bool fight = true;

            while (fight)
            {
                var round = new RoundInfo();
                totalCards = totalCards.Shuffle().ToList();

                foreach (var card in totalCards)
                {
                    if (card.Health <= 0)
                        continue;

                    var enemies = totalCards.Where(x => x.Health > 0 && x.Card.GameDeckId != card.Card.GameDeckId).ToList();
                    if (enemies.Count() > 0)
                    {
                        var target = Fun.GetOneRandomFrom(enemies);
                        var dmg = GetDmgDeal(card.Card, target.Card);
                        target.Health -= dmg;

                        if (target.Health < 1)
                            target.Health = 0;

                        var hpSnap = round.Cards.FirstOrDefault(x => x.CardId == target.Card.Id);
                        if (hpSnap == null)
                        {
                            round.Cards.Add(new HpSnapshot
                            {
                                CardId = target.Card.Id,
                                Hp = target.Health
                            });
                        }
                        else hpSnap.Hp = target.Health;

                        round.Fights.Add(new AttackInfo
                        {
                            Dmg = dmg,
                            AtkCardId = card.Card.Id,
                            DefCardId = target.Card.Id
                        });
                    }
                }

                rounds.Add(round);

                if (oneCard)
                {
                    fight = totalCards.Count(x => x.Health > 0) > 1;
                }
                else
                {
                    var alive = totalCards.Where(x => x.Health > 0).Select(x => x.Card);
                    var one = alive.FirstOrDefault();
                    if (one == null) break;

                    fight = alive.Any(x => x.GameDeckId != one.GameDeckId);
                }
            }

            PlayerInfo winner = null;
            var win = totalCards.Where(x => x.Health > 0).Select(x => x.Card).FirstOrDefault();

            if (win != null)
                winner = players.FirstOrDefault(x => x.Cards.Any(c => c.GameDeckId == win.GameDeckId));

            return new FightHistory(winner) { Rounds = rounds };
        }

        public List<Embed> GetActiveList(IEnumerable<Card> list)
        {
            var page = 0;
            var msg = new List<Embed>();
            var cardsStrings = list.Select(x => $"**P:** {x.CardPower:F} {x.GetString(false, false, true)}");
            var perPage = cardsStrings.SplitList(10);

            foreach (var p in perPage)
            {
                msg.Add(new EmbedBuilder()
                {
                    Color = EMType.Info.Color(),
                    Footer = new EmbedFooterBuilder().WithText($"(S: {++page}) MOC {list.Sum(x => x.CalculateCardPower()):F}"),
                    Description = ("**Twoje aktywne karty to**:\n\n" + string.Join("\n", p)).TrimToLength(),
                }.Build());
            }

            return msg;
        }

        public ICharacterInfoTitle IsInPool(CharacterPoolType type, ulong id)
        {
            var idPool = type switch
            {
                CharacterPoolType.Anime => _charIdAnime,
                CharacterPoolType.Manga => _charIdManga,
                _ => _charIdAll,
            };

            if (idPool.Pool.IsNullOrEmpty())
                return null;

            return idPool.Pool.FirstOrDefault(x => x.Id == id);
        }

        public async Task<ICharacterInfoTitle> GetRandomCharacterAsync(CharacterPoolType type)
        {
            var idPool = type switch
            {
                CharacterPoolType.Anime => _charIdAnime,
                CharacterPoolType.Manga => _charIdManga,
                _ => _charIdAll,
            };

            if (idPool.IsNeedForUpdate(_time.Now()))
            {
                using (await _semaphoreChars.LockAsync().ConfigureAwait(false))
                {
                    if (idPool.IsNeedForUpdate(_time.Now()))
                    {
                        try
                        {
                            var characters = type switch
                            {
                                CharacterPoolType.Anime => await _shClient.Ex.GetAllCharactersFromAnimeFatAsync(),
                                CharacterPoolType.Manga => await _shClient.Ex.GetAllCharactersFromMangaFatAsync(),
                                _ => await _shClient.Ex.GetAllCharactersFatAsync(),
                            };

                            if (!characters.IsSuccessStatusCode()) return null;

                            idPool.Update(characters.Body, _time.Now());
                        }
                        catch (Exception)
                        {
                            return null;
                        }
                    }
                }
            }

            return idPool.GetOneRandom();
        }

        private bool FileIsTooBigToSend(string file)
        {
            return File.Exists(file) && (new FileInfo(file).Length / 1000 / 1000) > 25;
        }

        public async Task<string> GetWaifuProfileImageAsync(Card card, ITextChannel trashCh)
        {
            var url = Api.Models.CardFinalView.GetCardProfileInShindenUrl(card);
            var uri = await GetCardProfileUrlIfExistAsync(card);
            if (!card.IsAnimatedImage || (card.IsAnimatedImage && !FileIsTooBigToSend(uri)))
            {
                try
                {
                    var fs = await trashCh.SendFileAsync(uri);
                    url = fs.Attachments.First().Url;
                }
                catch (Exception ex)
                {
                    _logger.Log($"Sending file: {ex.Message}");
                }
            }
            return url;
        }

        public async Task<List<Embed>> GetWaifuFromCharacterSearchResult(string title, IEnumerable<Card> cards, bool mention, SocketGuild guild = null, bool shindenUrls = false, bool tldr = false)
        {
            var list = new List<Embed>();

            var contentString = new MessageContent(1).Append($"{title} **[{cards.Count()}]**\n\n");
            foreach (var card in cards.Select(async (x, i) => $"{i+1}: {await GetCardInfo(x, mention, guild, shindenUrls, tldr)}"))
            {
                AppendMessage(list, contentString, await card);
            }

            list.Add(new EmbedBuilder()
            {
                Color = EMType.Info.Color(),
                Description = contentString.ToString()
            }.Build());

            return list;
        }

        public async Task<List<Embed>> GetWaifuFromCharacterTitleSearchResultAsync(IEnumerable<Card> cards, bool mention, SocketGuild guild = null, bool shindenUrls = false, bool tldr = false)
        {
            var list = new List<Embed>();
            var characters = cards.GroupBy(x => x.Character);

            var contentString = new MessageContent();
            foreach (var cardsG in characters)
            {
                var fC = cardsG.First();
                if (tldr) AppendMessage(list, contentString, $"\n{fC.Name} ({fC.Character}) {fC.GetCharacterUrl()} ({fC.WhoWantsCount})\n");
                else AppendMessage(list, contentString, $"\n**{fC.GetNameWithUrl()}** (KC: {fC.WhoWantsCount}) **[{cardsG.Count()}]**\n");

                foreach (var card in cardsG.Select(async (x, i) => $"{i+1}: {await GetCardInfo(x, mention, guild, shindenUrls, tldr)}"))
                {
                    AppendMessage(list, contentString, await card);
                }
            }

            list.Add(new EmbedBuilder()
            {
                Color = EMType.Info.Color(),
                Description = contentString.ToString()
            }.Build());

            return list;
        }

        private async Task<string> GetCardInfo(Card card, bool mention, SocketGuild guild, bool shindenUrls, bool tldr)
        {
            if (mention)
            {
                var userId = card.GameDeckId == 1 ? (guild?.CurrentUser?.Id ?? 1) : card.GameDeckId;
                if (tldr) return $"{userId}: {card.Id} {card.GetCardRealRarity()} {card.GetStatusIcons(_tags)} {card.GetPocketUrl()}\n";
                return $"<@{userId}>: {card.GetIdWithUrl()} **{card.GetCardRealRarity()}** {card.GetStatusIcons(_tags)}\n";
            }

            var user = guild?.GetUser(card.GameDeckId) ?? await _client.GetUserAsync(card.GameDeckId);
            if (tldr) return $"{user?.GetUserNickInGuild()}: {card.Id} {card.GetCardRealRarity()} {card.GetStatusIcons(_tags)} {card.GetPocketUrl()}\n";

            if (!shindenUrls || card?.GameDeck?.User?.Shinden == 0 || card?.GameDeckId == 1)
                return $"{user?.GetUserNickInGuild() ?? "????"}: {card.GetIdWithUrl()} **{card.GetCardRealRarity()}** {card.GetStatusIcons(_tags)}\n";

            return $"[{user?.GetUserNickInGuild() ?? "????"}](https://shinden.pl/user/{card.GameDeck.User.Shinden}): {card.GetIdWithUrl()} **{card.GetCardRealRarity()}** {card.GetStatusIcons(_tags)}\n";
        }

        private void AppendMessage(List<Embed> embeds, MessageContent currentContent, string nextPart) =>
            _helper.AppendMessage(embeds, currentContent, nextPart);

        public Embed GetBoosterPackList(SocketUser user, List<BoosterPack> packs)
        {
            int groupCnt = 0;
            int startGroup = 1;
            string groupName = "";
            string packString = "";
            for (int i = 0; i < packs.Count + 1; i++)
            {
                if (i == packs.Count || groupName != packs[i].Name)
                {
                    if (groupName != "")
                    {
                        string count = groupCnt > 0 ? $"{startGroup}-{startGroup + groupCnt}" : $"{startGroup}";
                        packString += $"**[{count}]** {groupName}\n";
                    }
                    if (i != packs.Count)
                    {
                        groupName = packs[i].Name;
                        startGroup = i + 1;
                        groupCnt = 0;
                    }
                }
                else ++groupCnt;
            }

            return new EmbedBuilder
            {
                Color = EMType.Info.Color(),
                Description = $"{user.Mention} twoje pakiety:\n\n{packString.TrimToLength()}"
            }.Build();
        }

        public string NormalizeItemFilter(string filter)
        {
            var quality = _qualityNamesList.FirstOrDefault(x => x.Equals(filter, StringComparison.CurrentCultureIgnoreCase));
            if (!string.IsNullOrEmpty(quality))
                return ((Quality)Enum.Parse(typeof(Quality), quality)).ToName();

            if (_greekLetersAsQuality.TryGetValue(filter.ToLower(), out var qua))
                return qua.ToName();

            return filter;
        }

        public List<Embed> GetItemList(SocketUser user, IEnumerable<Item> items, string filter = "")
        {
            var pages = new List<Embed>();
            var list = items.ToItemList(filter).SplitList(50);

            for (int i = 0; i < list.Count; i++)
            {
                var embed = new EmbedBuilder
                {
                    Color = EMType.Info.Color(),
                    Description = $"{user.Mention} twoje przedmioty **({i + 1}/{list.Count})**:\n\n{string.Join("\n", list[i]).TrimToLength()}"
                };
                pages.Add(embed.Build());
            }

            return pages;
        }

        public async Task<List<ulong>> GetCharactersFromSeasonAsync()
        {
            return await _shClient.Ex.GetAnimeFromSeasonAsync(true);
        }

        public async Task<List<Card>> OpenBoosterPackAsync(IUser user, BoosterPack pack, CharacterPoolType poolType)
        {
            int errorCnt = 0;
            var cardsFromPack = new List<Card>();

            for (int i = 0; i < pack.CardCnt; i++)
            {
                ICharacterInfo chara = null;
                if (pack.Characters.Count > 0)
                {
                    var id = pack.Characters.First();
                    if (pack.Characters.Count > 1)
                        id = Fun.GetOneRandomFrom(pack.Characters);

                    chara = await _shinden.GetCharacterInfoAsync(id.Character);
                }
                else if (pack.Title != 0)
                {
                    var charsInTitle = await _shinden.GetCharactersFromTitleAsync(pack.Title);
                    if (charsInTitle != null && charsInTitle.Count > 0)
                    {
                        var id = Fun.GetOneRandomFrom(charsInTitle).CharacterId;
                        if (id.HasValue)
                        {
                            chara = await _shinden.GetCharacterInfoAsync(id.Value);
                        }
                    }
                    else if (pack.CardSourceFromPack == CardSource.Lottery)
                    {
                        var charsInSeason = await GetCharactersFromSeasonAsync();
                        if (!charsInSeason.IsNullOrEmpty())
                        {
                            var charId = Fun.GetOneRandomFrom(charsInSeason);
                            chara = await _shinden.GetCharacterInfoAsync(charId);
                        }
                    }
                }
                else
                {
                    var chart = await GetRandomCharacterAsync(poolType);
                    var newCard = GenerateNewCard(user, chart, pack.RarityExcludedFromPack.Select(x => x.Rarity).ToList());
                    if (pack.MinRarity != Rarity.E && i == pack.CardCnt - 1)
                        newCard = GenerateNewCard(user, chart, pack.MinRarity);

                    newCard.IsTradable = pack.IsCardFromPackTradable;
                    newCard.Source = pack.CardSourceFromPack;

                    cardsFromPack.Add(newCard);
                    continue;
                }

                if (chara != null)
                {
                    var newCard = GenerateNewCard(user, chara, pack.RarityExcludedFromPack.Select(x => x.Rarity).ToList());
                    if (pack.MinRarity != Rarity.E && i == pack.CardCnt - 1)
                        newCard = GenerateNewCard(user, chara, pack.MinRarity);

                    newCard.IsTradable = pack.IsCardFromPackTradable;
                    newCard.Source = pack.CardSourceFromPack;

                    cardsFromPack.Add(newCard);
                }
                else
                {
                    if (++errorCnt > 2)
                        break;
                }
            }

            return cardsFromPack;
        }

        public async Task<string> GenerateAndSaveCardAsync(Card card, CardImageType type = CardImageType.Normal, bool fromApi = false)
        {
            var ext = card.IsAnimatedImage ? "gif" : "webp";
            string imageLocation = $"{Dir.Cards}/{card.Id}.{ext}";
            string sImageLocation = $"{Dir.CardsMiniatures}/{card.Id}.{ext}";
            string pImageLocation = $"{Dir.CardsInProfiles}/{card.Id}.{ext}";

            var toReturn = type switch
            {
                CardImageType.Small => sImageLocation,
                CardImageType.Profile => pImageLocation,
                _ => imageLocation,
            };

            if (fromApi && _cardGenLocker.IsInUse(card.Id))
                return toReturn;

            using (await _cardGenLocker.LockAsync(card.Id))
            {
                if (File.Exists(toReturn))
                    return toReturn;

                try
                {
                    using (var image = await _img.GetWaifuCardAsync(card))
                    {
                        image.SaveToPath(imageLocation);
                        image.SaveToPath(sImageLocation, 133);
                    }

                    using (var cardImage = await _img.GetWaifuInProfileCardAsync(card))
                    {
                        cardImage.SaveToPath(pImageLocation);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Log($"Error while generating card {card.Id}: {ex.Message}");
                }
            }

            return toReturn;
        }

        public void DeleteCardImageIfExist(Card card)
        {
            var toRemove = new List<string>()
            {
                $"{Dir.Cards}/{card.Id}.png",
                $"{Dir.CardsMiniatures}/{card.Id}.png",
                $"{Dir.CardsInProfiles}/{card.Id}.png",
                $"{Dir.Cards}/{card.Id}.webp",
                $"{Dir.CardsMiniatures}/{card.Id}.webp",
                $"{Dir.CardsInProfiles}/{card.Id}.webp",
                $"{Dir.Cards}/{card.Id}.gif",
                $"{Dir.CardsMiniatures}/{card.Id}.gif",
                $"{Dir.CardsInProfiles}/{card.Id}.gif",
            };

            try
            {
                foreach (var tr in toRemove)
                {
                    if (File.Exists(tr))
                        File.Delete(tr);
                }
            }
            catch (Exception) { }
        }

        private async Task<string> GetCardUrlIfExistAsync(Card card, bool force = false)
        {
            string ext = card.IsAnimatedImage ? "gif" : "webp";
            string imageLocation = $"{Dir.Cards}/{card.Id}.{ext}";
            string sImageLocation = $"{Dir.CardsMiniatures}/{card.Id}.{ext}";
            bool generate = (!File.Exists(imageLocation) || !File.Exists(sImageLocation) || force) && card.Id != 0;
            return generate ? await GenerateAndSaveCardAsync(card) : imageLocation;
        }

        private async Task<string> GetCardProfileUrlIfExistAsync(Card card, bool force = false)
        {
            string ext = card.IsAnimatedImage ? "gif" : "webp";
            string imageLocation = $"{Dir.CardsInProfiles}/{card.Id}.{ext}";
            bool generate = (!File.Exists(imageLocation) || force) && card.Id != 0;
            return generate ? await GenerateAndSaveCardAsync(card, CardImageType.Profile) : imageLocation;
        }

        public SafariImage GetRandomSarafiImage()
        {
            SafariImage dImg = null;
            var reader = new Config.JsonFileReader(Dir.GetResource("Poke/List.json"));
            try
            {
                var images = reader.Load<List<SafariImage>>();
                dImg = Fun.GetOneRandomFrom(images);
            }
            catch (Exception) { }

            return dImg;
        }

        public async Task<string> GetSafariViewAsync(SafariImage info, Card card, ITextChannel trashChannel)
        {
            string uri = info != null ? info.Uri(SafariImage.Type.Truth) : SafariImage.DefaultUri(SafariImage.Type.Truth);

            using (var cardImage = await _img.GetWaifuCardAsync(card))
            {
                int posX = info != null ? info.GetX() : SafariImage.DefaultX();
                int posY = info != null ? info.GetY() : SafariImage.DefaultY();
                using (var pokeImage = _img.GetCatchThatWaifuImage(cardImage, uri, posX, posY))
                {
                    using (var stream = pokeImage.ToJpgStream())
                    {
                        var msg = await trashChannel.SendFileAsync(stream, $"poke.jpg");
                        return msg.Attachments.First().Url;
                    }
                }
            }
        }

        public async Task<string> GetSafariViewAsync(SafariImage info, ITextChannel trashChannel)
        {
            string uri = info != null ? info.Uri(SafariImage.Type.Mystery) : SafariImage.DefaultUri(SafariImage.Type.Mystery);
            var msg = await trashChannel.SendFileAsync(uri);
            return msg.Attachments.First().Url;
        }

        public async Task<string> GetWaifuCardImageAsync(Card card, ITextChannel trashCh)
        {
            var url = Api.Models.CardFinalView.GetCardBaseInShindenUrl(card);
            var uri = await GetCardUrlIfExistAsync(card);
            if (!card.IsAnimatedImage || (card.IsAnimatedImage && !FileIsTooBigToSend(uri)))
            {
                try
                {
                    var fs = await trashCh.SendFileAsync(uri);
                    url = fs.Attachments.First().Url;
                }
                catch (Exception ex)
                {
                    _logger.Log($"Sending file: {ex.Message}");
                }
            }
            return url;
        }

        public async Task<Embed> BuildCardImageAsync(Card card, ITextChannel trashChannel, SocketUser owner, bool showStats)
        {
            string imageUrl = showStats ? await GetWaifuCardImageAsync(card, trashChannel) : await GetWaifuProfileImageAsync(card, trashChannel);
            string ownerString = (((owner as SocketGuildUser)?.Nickname ?? owner?.GlobalName) ?? owner?.Username) ?? "????";

            return new EmbedBuilder
            {
                ImageUrl = imageUrl,
                Color = EMType.Info.Color(),
                Description = card.GetString(false, false, true, false, false),
                Footer = new EmbedFooterBuilder
                {
                    Text = $"Należy do: {ownerString}"
                },
            }.Build();
        }

        public async Task<Embed> BuildCardViewAsync(Card card, ITextChannel trashChannel, SocketUser owner)
        {
            string imageUrl = await GetWaifuCardImageAsync(card, trashChannel);
            string imgUrls = $"[_obrazek_]({imageUrl})\n[_możesz zmienić obrazek tutaj_]({card.GetCharacterUrl()}/edit_crossroad)";
            string ownerString = (((owner as SocketGuildUser)?.Nickname ?? owner?.GlobalName) ?? owner?.Username) ?? "????";
            bool hideScalpelInfo = card.CustomImageDate < _hiddeScalpelBeforeDate;

            return new EmbedBuilder
            {
                ImageUrl = imageUrl,
                Color = EMType.Info.Color(),
                Footer = new EmbedFooterBuilder
                {
                    Text = $"Należy do: {ownerString}"
                },
                Description = $"{card.GetDesc(hideScalpelInfo, _tags, _time)}{imgUrls}".TrimToLength(2500)
            }.Build();
        }

        public async Task<List<string>> GetWhoWantsCardsStringAsync(List<GameDeck> wishlists, bool showNames, SocketGuild guild)
        {
            if (!showNames)
            {
                return wishlists.Select((x, i) => $"{i+1}: <@{x.Id}>").ToList();
            }

            var str = new List<string>();
            foreach (var user in wishlists.Select(async (x, i) => $"{i+1}: {await GetUserNameInfoAsync(x, guild)}"))
            {
                str.Add(await user);
            }

            return str;
        }

        private async Task<string> GetUserNameInfoAsync(GameDeck deck, SocketGuild guild)
        {
            return (deck.User.Shinden == 0 && deck.User.Id == 1)
            ? (guild.GetUser(deck.UserId) ?? _client.GetUser(deck.UserId) ?? await _client.GetUserAsync(deck.UserId)).GetUserNickInGuild()
            : $"[{(guild.GetUser(deck.UserId) ?? _client.GetUser(deck.UserId) ?? await _client.GetUserAsync(deck.UserId)).GetUserNickInGuild()}](https://shinden.pl/user/{deck.User.Shinden})";
        }

        public Embed GetShopView(ItemWithCost[] items, string name = "Sklepik", string currency = "TC")
        {
            string embedString = "";
            for (int i = 0; i < items.Length; i++)
                embedString += $"**[{i + 1}]** _{items[i].Item.Name}_ - {items[i].Cost} {currency}\n";

            return new EmbedBuilder
            {
                Color = EMType.Info.Color(),
                Description = $"**{name}**:\n\n{embedString}".TrimToLength()
            }.Build();
        }

        public Embed GetItemShopInfo(ItemWithCost item)
        {
            return new EmbedBuilder
            {
                Color = EMType.Info.Color(),
                Description = $"**{item.Item.Name}**\n_{item.Item.Type.Desc()}_",
            }.Build();
        }

        public async Task<IEnumerable<Embed>> GetContentOfWishlistAsync(List<ulong> cardsId, List<ulong> charactersId, List<ulong> titlesId, bool tldr)
        {
            var contentTable = new List<string>();
            if (cardsId.Count > 0)contentTable.Add($"**Karty:** {string.Join(", ", cardsId)}");

            foreach (var character in charactersId)
            {
                var charInfo = await _shinden.GetCharacterInfoAsync(character);
                if (charInfo != null)
                {
                    if (tldr) contentTable.Add($"P[{charInfo.Id}] {charInfo} {charInfo.CharacterUrl}");
                    else contentTable.Add($"**P[{charInfo.Id}]** [{charInfo}]({charInfo.CharacterUrl})");
                }
                else
                {
                    contentTable.Add($"**P[{character}]** ????");
                }
            }

            foreach (var title in titlesId)
            {
                var titleInfo = await _shinden.GetInfoFromTitleAsync(title);
                if (titleInfo != null)
                {
                    var url = "https://shinden.pl/";
                    if (titleInfo is IAnimeTitleInfo ai) url = ai.AnimeUrl;
                    else if (titleInfo is IMangaTitleInfo mi) url = mi.MangaUrl;

                    if (tldr) contentTable.Add($"T[{titleInfo.Id}] {titleInfo} {url}");
                    else contentTable.Add($"**T[{titleInfo.Id}]** [{titleInfo}]({url})");
                }
                else
                {
                    contentTable.Add($"**T[{title}]** ????");
                }
            }

            string temp = "";
            var content = new List<Embed>();
            for (int i = 0; i < contentTable.Count; i++)
            {
                if (temp.Length + contentTable[i].Length > 3500)
                {
                    content.Add(new EmbedBuilder()
                    {
                        Color = EMType.Info.Color(),
                        Description = temp
                    }.Build());
                    temp = contentTable[i];
                }
                else temp += $"\n{contentTable[i]}";
            }

            content.Add(new EmbedBuilder()
            {
                Color = EMType.Info.Color(),
                Description = temp
            }.Build());

            return content;
        }

        public async Task<IEnumerable<Card>> GetCardsFromWishlistAsync(List<ulong> cardsId, List<ulong> charactersId, List<ulong> titlesId, Database.DatabaseContext db, IEnumerable<Card> userCards)
        {
            var cards = new List<Card>();
            if (cardsId != null)
            {
                var cds = await db.Cards.Include(x => x.Tags).Where(x => cardsId.Any(c => c == x.Id))
                    .Include(x => x.GameDeck).ThenInclude(x => x.User).AsNoTracking().ToListAsync();
                cards.AddRange(cds);
            }

            var characters = new List<ulong>();
            if (charactersId != null)
                characters.AddRange(charactersId);

            if (titlesId != null)
            {
                var charactersInTitles = new List<ulong>();
                foreach (var id in titlesId)
                {
                    var charsFromTitle = await _shinden.GetCharactersFromTitleAsync(id);
                    if (charsFromTitle != null && charsFromTitle.Count > 0)
                    {
                        charactersInTitles.AddRange(charsFromTitle.Where(x => x.CharacterId.HasValue).Select(x => x.CharacterId.Value));
                    }
                }
                characters.AddRange(charactersInTitles.Where(c => !userCards.Any(x => x.Character == c)));
            }

            if (characters.Count > 0)
            {
                characters = characters.Distinct().ToList();
                var cads = await db.Cards.Include(x => x.Tags).Where(x => characters.Any(c => c == x.Character))
                    .Include(x => x.GameDeck).ThenInclude(x => x.User).AsNoTracking().ToListAsync();
                cards.AddRange(cads);
            }

            return cards.Distinct().ToList();
        }

        public string EndExpedition(User user, Card card, bool showStats = false)
        {
            Dictionary<string, int> items = new Dictionary<string, int>();

            var duration = _expedition.GetLengthOfExpedition(user, card);
            if (duration.CalcTime < 0 || duration.RealTime < 0)
            {
                return "Coś poszło nie tak, wyprawa nie została zakończona.";
            }

            var multiplier = (duration.RealTime < 60) ? ((duration.RealTime < 30) ? 3d : 2d) : 1d;
            var reward = multiplier != 1 ? "Wyprawa? Chyba po bułki do sklepu.\n\n" : "";

            var karmaMod = 1d;
            var bonusFatigue = 0d;
            var allowItems = true;
            var totalExp = _expedition.GetExpFromExpedition(duration.CalcTime, card);
            var totalItemsCnt = _expedition.GetItemsCountFromExpedition(duration.CalcTime, card, user);

            if (CheckEventInExpedition(card.Expedition, duration))
            {
                var e = _events.RandomizeEvent(card.Expedition, duration);
                allowItems = _events.ExecuteEvent(e, user, card, ref reward);
                totalItemsCnt += _events.GetMoreItems(e);

                if (e == EventType.ChangeDere)
                {
                    if (card.Curse == CardCurse.DereBlockade)
                    {
                        reward += $"zmiana zablokowana przez klątwe.\n";
                    }
                    else
                    {
                        card.Dere = RandomizeDere();
                        reward += $"{card.Dere}\n";

                        duration = _expedition.GetLengthOfExpedition(user, card);
                        totalExp = _expedition.GetExpFromExpedition(duration.CalcTime, card);
                        totalItemsCnt = _expedition.GetItemsCountFromExpedition(duration.CalcTime, card, user);
                    }
                }
                else if (e == EventType.LoseCard)
                {
                    karmaMod = 0.5;
                    user.StoreExpIfPossible(totalExp);
                    if (Fun.TakeATry(6d))
                    {
                        user.GameDeck.AddItem(ItemType.GiveTagSlot.ToItem());
                        reward += $"Mimo wszystko coś po sobie zostawiła!\n";
                    }
                }
                else if (e == EventType.Fight && !allowItems)
                {
                    allowItems = true;
                    totalItemsCnt /= 2;
                    totalExp /= 2;
                }
            }

            var karmaCost = _expedition.GetKarmaCostOfExpedition(duration.CalcTime, card, user) * karmaMod;
            var rawAffectionCost = _expedition.GetAffectionCostOfExpedition(duration.CalcTime, card);
            var affectionCost = rawAffectionCost * multiplier;

            if (duration.CalcTime <= 5)
            {
                bonusFatigue += 400;
                affectionCost += 50;
                totalItemsCnt = 0;
                karmaCost = 0;
                totalExp = 0;
            }

            if (card.Fatigue >= FatigueSecondPhase)
            {
                totalItemsCnt -= (int)(totalItemsCnt * 0.3);
                affectionCost *= 1.2;
                totalExp *= 0.6;
            }
            else if (card.Fatigue >= FatigueFirstPhase)
            {
                totalItemsCnt -= (int)(totalItemsCnt * 0.1);
                affectionCost *= 1.1;
                totalExp *= 0.9;
            }

            card.ExpCnt += totalExp;

            if (card.Curse == CardCurse.None && duration.RealTime > 45)
            {
                card.Curse = _expedition.GetPotentialCurse(card.Expedition);
                if (card.Curse != CardCurse.None)
                    reward += $"**Karta została spaczona!**\n\n";
            }

            var minAff = 0d;
            var karmaItem = 0d;
            var missingItems = 0;
            reward += $"Zdobywa:\n+{totalExp:F} exp ({card.ExpCnt:F})\n";

            if (allowItems && totalItemsCnt > 0)
            {
                var affectionToReturn = _expedition.GetGuaranteedAffection(user, card, rawAffectionCost);
                while (affectionToReturn > 0)
                {
                    var newItem = _expedition.RandomizeItemFor(card.Expedition, Expedition.ItemDropType.Food);
                    karmaItem += newItem.Type.GetBaseKarmaChange();
                    var itemAff = newItem.GetBaseAffection();

                    minAff += itemAff;
                    affectionToReturn -= itemAff;

                    user.GameDeck.AddItem(newItem);

                    if (totalItemsCnt > 0)
                        totalItemsCnt--;
                    else
                        missingItems++;

                    if (!items.ContainsKey(newItem.Name))
                        items.Add(newItem.Name, 0);

                    ++items[newItem.Name];
                }

                for (int i = 0; i < totalItemsCnt; i++)
                {
                    var dropType = _expedition.RandomizeItemDropTypeFor(card.Expedition);
                    if (dropType == Expedition.ItemDropType.None)
                        continue;

                    var newItem = _expedition.RandomizeItemFor(card.Expedition, dropType);
                    if (newItem.Type == ItemType.NotAnItem)
                        continue;

                    minAff += newItem.GetBaseAffection();
                    karmaItem += newItem.Type.GetBaseKarmaChange();

                    user.GameDeck.AddItem(newItem);

                    if (!items.ContainsKey(newItem.Name))
                        items.Add(newItem.Name, 0);

                    ++items[newItem.Name];
                }
            }

            reward += string.Join("\n", items.Select(x => $"+{x.Key} x{x.Value}"));

            var fatigue = _expedition.GetFatigueFromExpedition(duration.RealTime, card);

            if (showStats)
            {
                reward += $"\n\nRT: {duration.CalcTime:F} E: {totalExp:F} AI: {minAff:F} A: {affectionCost:F} K: {karmaCost:F} KI: {karmaItem:F} MI: {totalItemsCnt} LI: {missingItems} F: {fatigue:F}";
            }

            card.DecAffectionOnExpeditionBy(affectionCost);
            card.Expedition = CardExpedition.None;
            card.ExpeditionEndDate = _time.Now();
            user.GameDeck.Karma -= karmaCost;
            card.Fatigue += fatigue + bonusFatigue;

            return reward;
        }

        private bool CheckEventInExpedition(CardExpedition expedition, (double CalcTime, double RealTime) duration)
        {
            if (duration.CalcTime <= 3)
                return false;

            switch (expedition)
            {
                case CardExpedition.NormalItemWithExp:
                    return Fun.TakeATry(10d);

                case CardExpedition.ExtremeItemWithExp:
                    if (duration.CalcTime > 72 || duration.RealTime > 228)
                        return true;
                    return Fun.TakeATry(80d);

                case CardExpedition.LightItemWithExp:
                case CardExpedition.DarkItemWithExp:
                    return Fun.TakeATry(10d);

                case CardExpedition.DarkItems:
                case CardExpedition.LightItems:
                case CardExpedition.LightExp:
                case CardExpedition.DarkExp:
                    return Fun.TakeATry(20d);

                case CardExpedition.UltimateMedium:
                    return Fun.TakeATry(25d);

                case CardExpedition.UltimateHard:
                    return Fun.TakeATry(40d);

                case CardExpedition.UltimateHardcore:
                    return Fun.TakeATry(65d);

                default:
                case CardExpedition.UltimateEasy:
                    return false;
            }
        }

        public List<string> GetItemChancesFromExpedition(CardExpedition expedition)
        {
            return _expedition.GetChancesFromExpedition(expedition);
        }

        public string GetEventChancesFromExpedition(CardExpedition expedition)
        {
            return _events.GetChancesFromExpedition(expedition);
        }

        public ExecutionResult DestroyOrReleaseCards(User user, ulong[] ids, bool release = false, ulong tagId = 0)
        {
            var cardsForDiscarding = ids.IsNullOrEmpty() ? user.GetCardsByTag(tagId).ToList() : user.GetCards(ids).ToList();
            if (cardsForDiscarding.IsNullOrEmpty())
                return ExecutionResult.FromError("nie posiadasz takich kart");

            var realDiscardedCount = 0;
            var ignored = new List<Card>();
            foreach (var card in cardsForDiscarding)
            {
                _ = card.RecoverFatigue(_time);
                if (card.IsProtectedFromDiscarding(_tags))
                {
                    ignored.Add(card);
                    continue;
                }

                realDiscardedCount++;
                card.DestroyOrRelease(user, release);

                user.GameDeck.Cards.Remove(card);
                DeleteCardImageIfExist(card);
            }

            string actionStr = release ? "uwolni" : "zniszczy";
            string lettery = ignored.Count > 1 ? "" : "y";

            if (ignored.Count == cardsForDiscarding.Count)
                return ExecutionResult.FromError($"nie udało się {actionStr}ć żadnej karty, najpewniej znajdują się one w klatce lub są oznaczone jako ulubione.");

            var response = new StringBuilder().Append($"{actionStr}ł ");
            response.Append(realDiscardedCount > 1 ? $"{realDiscardedCount} kart" : $"kartę: {cardsForDiscarding.Where(x => !ignored.Any(c => c.Id == x.Id)).First().GetString(false, false, true)}");

            if (ignored.Any())
                response.Append($"\n\n ❗ Nie udało się {actionStr}ć {ignored.Count} kart{lettery}!");

            return ExecutionResult.FromSuccess(response.ToString());
        }

        public async Task<ExecutionResult> UseItemAsync(User user, string userName, int itemNumber, ulong wid, string detail, bool itemToExp = false)
        {
            var itemList = user.GetAllItems().ToList();
            if (itemList.IsNullOrEmpty())
                return ExecutionResult.FromError("nie masz żadnych przedmiotów.");

            if (itemNumber <= 0 || itemNumber > itemList.Count)
                return ExecutionResult.FromError("nie masz aż tylu przedmiotów.");

            var item = itemList[itemNumber - 1];
            var parsedCount = int.TryParse(detail, out var itemCnt);
            if (!parsedCount || itemCnt < 1 || item.Type == ItemType.ChangeCardImage)
                itemCnt = 1;

            if (parsedCount && item.Type == ItemType.DereReRoll)
                detail = null;

            if (item.Count < itemCnt)
                return ExecutionResult.FromError("nie posiadasz tylu sztuk tego przedmiotu.");

            if (!item.Type.CanBeUsedWithNormalUseCommand())
                return ExecutionResult.FromError("tego przedmiotu nie można użyć za pomocą komendy `użyj`.");

            if (itemCnt != 1 && !item.Type.CanUseMoreThanOne(itemToExp))
                return ExecutionResult.FromError("możesz użyć tylko jeden przedmiot tego typu na raz!");

            var res = wid == 0 ? UseItem(item, user, itemCnt, itemToExp) : await UseItemOnCardAsync(item, user, userName, itemCnt, wid, detail);
            if (res.IsOk())
            {
                var mission = user.TimeStatuses.FirstOrDefault(x => x.Type == Database.Models.StatusType.DUsedItems);
                if (mission == null)
                {
                    mission = Database.Models.StatusType.DUsedItems.NewTimeStatus();
                    user.TimeStatuses.Add(mission);
                }
                mission.Count(_time.Now(), itemCnt);

                if (item.Count <= 0)
                    user.GameDeck.Items.Remove(item);
            }
            return res;
        }

        private ExecutionResult UseItem(Item item, User user, int itemCnt, bool itemToExp)
        {
            if (!item.Type.CanUseWithoutCard(itemToExp))
                return ExecutionResult.FromError("nie można użyć przedmiotu bez karty.");

            var activeFigure = user.GameDeck.Figures.FirstOrDefault(x => x.IsFocus);
            if (activeFigure == null && item.Type.IsFigureNeededToUse())
                return ExecutionResult.FromError("nie posiadasz aktywnej figurki!");

            var str = new StringBuilder().Append($"Użyto _{item.Name}_ {((itemCnt > 1) ? $"x{itemCnt}" : "")}\n\n");
            switch (item.Type)
            {
                case ItemType.GiveTagSlot:
                    user.GameDeck.MaxNumberOfTags += itemCnt;
                    break;

                case ItemType.FigureHeadPart:
                case ItemType.FigureBodyPart:
                case ItemType.FigureClothesPart:
                case ItemType.FigureLeftArmPart:
                case ItemType.FigureLeftLegPart:
                case ItemType.FigureRightArmPart:
                case ItemType.FigureRightLegPart:
                case ItemType.FigureUniversalPart:
                    if (itemToExp)
                    {
                        var itemPartType = item.Type.GetPartType();
                        if (activeFigure.FocusedPart != itemPartType && itemPartType != FigurePart.All)
                            return ExecutionResult.FromError("typy części się nie zgadzają.");

                        var expFromPart = item.ToExpForPart(activeFigure.SkeletonQuality);
                        var totalExp = expFromPart * itemCnt;
                        activeFigure.PartExp += totalExp;

                        str.Append($"Dodano do wybranej części figurki {totalExp:F} punktów konstrukcji. W sumie posiada ich {activeFigure.PartExp:F}.");
                        break;
                    }

                    if (!activeFigure.CanAddPart(item))
                        return ExecutionResult.FromError("część, którą próbujesz dodać ma zbyt niską jakość.");

                    if (!activeFigure.HasEnoughPointsToAddPart(item))
                        return ExecutionResult.FromError($"aktywowana część ma zbyt małą ilość punktów konstrukcji, wymagana to {activeFigure.ConstructionPointsToInstall(item)}.");

                    if (!activeFigure.AddPart(item))
                        return ExecutionResult.FromError("coś poszło nie tak.");

                    str.Append("Dodano część do figurki.");
                    break;

                case ItemType.FigureSkeleton:
                    if (itemToExp)
                    {
                        var expFromPart = item.ToExpForPart(activeFigure.SkeletonQuality);
                        activeFigure.PartExp += expFromPart * itemCnt;

                        str.Append($"Dodano do wybranej części figurki {expFromPart:F} punktów konstrukcji. W sumie posiada ich {activeFigure.PartExp:F}.");
                        break;
                    }
                    return ExecutionResult.FromError("nie możesz użyć szkieletu bez karty, chyba, że chcesz przerobić go na exp.");

                default:
                    return ExecutionResult.FromError($"tego przedmiotu ({item.Name}) nie powinno tutaj być!");
            }

            str.Append(item.Type.Info());
            item.Count -= itemCnt;

            return ExecutionResult.FromSuccess(str.ToString());
        }

        private async Task<ExecutionResult> UseItemOnCardAsync(Item item, User user, string userName, int itemCnt, ulong wid, string detail)
        {
             if (!item.Type.CanUseWithCard())
                return ExecutionResult.FromError("nie można użyć przedmiotu z kartą.");

            var card = user.GetCard(wid);
            if (card == null)
                return ExecutionResult.FromError("nie posiadasz takiej karty!");

            if (card.Expedition != CardExpedition.None)
                return ExecutionResult.FromError("ta karta jest na wyprawie!");

            switch (item.Type)
            {
                case ItemType.FigureSkeleton:
                case ItemType.CardParamsReRoll:
                case ItemType.IncreaseUpgradeCnt:
                case ItemType.BetterIncreaseUpgradeCnt:
                case ItemType.BloodOfYourWaifu:
                    if (card.FromFigure)
                        return ExecutionResult.FromError("tego przedmiotu nie można użyć na tej karcie.");
                    break;

                case ItemType.IncreaseUltimateAttack:
                case ItemType.IncreaseUltimateDefence:
                case ItemType.IncreaseUltimateHealth:
                case ItemType.IncreaseUltimateAll:
                    var res = card.CanUpgradePower(itemCnt);
                    if (res.Status == ExecutionResult.EStatus.Error)
                        return res;
                    break;

                default:
                    break;
            }

            var consumeItem = true;
            var embedColor = EMType.Bot;
            UserActivityBuilder activity = null;
            var textRelation = card.GetAffectionString();
            double karmaChange = item.Type.GetBaseKarmaChange() * itemCnt;
            double affectionInc = item.Type.GetBaseAffection() * itemCnt;
            var expMod = card.Curse == CardCurse.LoweredExperience ? 0.2 : (card.FromFigure ? 0.3 : 1);
            var str = new StringBuilder().Append($"Użyto _{item.Name}_ {((itemCnt > 1) ? $"x{itemCnt}" : "")}{(" na " + card.GetString(false, false, true))}\n\n");

            switch (item.Type)
            {
                case ItemType.AffectionRecoveryBig:
                case ItemType.AffectionRecoveryGreat:
                case ItemType.AffectionRecoveryNormal:
                case ItemType.AffectionRecoverySmall:
                    if (card.Curse == CardCurse.FoodBlockade)
                        return ExecutionResult.FromError("karta nie wie, co ma z tym zrobić!");
                    break;

                case ItemType.CheckAffection:
                    break;

                case ItemType.CheckCurse:
                    if (card.Curse == CardCurse.None)
                        return ExecutionResult.FromError("karta nie wie, co ma z tym zrobić!");
                    break;

                case ItemType.IncreaseExpBig:
                    card.ExpCnt += 5d * itemCnt * expMod;
                    break;

                case ItemType.IncreaseExpSmall:
                    card.ExpCnt += 1.5 * itemCnt * expMod;
                    break;

                case ItemType.ResetCardValue:
                    card.MarketValue = 1;
                    break;

                case ItemType.CardParamsReRoll:
                    card.Attack = RandomizeAttack(card.Rarity);
                    card.Defence = RandomizeDefence(card.Rarity);
                    break;

                case ItemType.IncreaseUltimateAttack:
                    card.AttackBonus += itemCnt * 5;
                    break;

                case ItemType.IncreaseUltimateDefence:
                    card.DefenceBonus += itemCnt * 3;
                    break;

                case ItemType.IncreaseUltimateHealth:
                    card.HealthBonus += itemCnt * 5;
                    break;

                case ItemType.IncreaseUltimateAll:
                    card.AttackBonus += itemCnt * 4;
                    card.HealthBonus += itemCnt * 4;
                    card.DefenceBonus += itemCnt * 4;
                    break;

                case ItemType.ChangeStarType:
                    if (StarStyle.Full.TryParse(detail, out var newStyle))
                    {
                        card.StarStyle = newStyle;
                        break;
                    }
                    return ExecutionResult.FromError("Nie rozpoznano typu gwiazdki!");

                case ItemType.ChangeCardImage:
                    var charIf = await _shinden.GetCharacterInfoAsync(card.Character);
                    if (charIf == null)
                        return ExecutionResult.FromError("Nie odnaleziono postaci na shinden!");

                    int tidx = 0;
                    var urls = charIf.Pictures.GetPicList();
                    if (string.Equals(detail, "lista", StringComparison.CurrentCultureIgnoreCase))
                        return ExecutionResult.FromSuccess("Obrazki: \n" + string.Join("\n", urls.Select(x => $"{++tidx}: {x}")), EMType.Info);

                    if (!int.TryParse(detail, out var urlIdx) || urlIdx <= 0 || urlIdx > urls.Count)
                        return ExecutionResult.FromError("Nie odnaleziono obrazka!");

                    var turl = urls[urlIdx - 1];
                    if (card.GetImage() == turl)
                        return ExecutionResult.FromError("Taki obrazek jest już ustawiony!");

                    card.CustomImage = turl;
                    break;

                case ItemType.SetCustomAnimatedImage:
                case ItemType.SetCustomImage:
                    var imgCheck = await _img.CheckImageUrlAsync(detail);
                    if (imgCheck.IsError())
                        return ExecutionResult.From(imgCheck);

                    if (card.Image is null && !card.FromFigure && !card.Unique)
                        return ExecutionResult.FromError("Aby ustawić własny obrazek, karta musi posiadać wcześniej ustawiony główny (na stronie)!");

                    var (isUrlToImage, imageExt) = await _img.IsUrlToImageAsync(imgCheck.Url);
                    if (!isUrlToImage)
                        return ExecutionResult.FromError("Nie został podany bezpośredni adres do obrazka!");

                    if (card.FromFigure && !_imgExtWithAlpha.Any(x => x.Equals(imageExt, StringComparison.CurrentCultureIgnoreCase)))
                        return ExecutionResult.FromError("Format obrazka nie pozwala na przeźroczystość, która jest wymagana do kart ultimate!");

                    bool isAnim = item.Type == ItemType.SetCustomAnimatedImage;
                    _ = await _img.SaveCardImageFromUrlAsync(imgCheck.Url, card, isAnim);

                    card.IsAnimatedImage = isAnim;
                    card.CustomImageDate = _time.Now();
                    consumeItem = isAnim || !card.FromFigure;
                    activity = new UserActivityBuilder(_time).WithUser(user).WithCard(card).WithType(Database.Models.ActivityType.UsedScalpel);
                    break;

                case ItemType.SetCustomBorder:
                    var imgCheck2 = await _img.CheckImageUrlAsync(detail);
                    if (imgCheck2.IsError())
                        return ExecutionResult.From(imgCheck2);

                    if (card.Image is null)
                        return ExecutionResult.FromError("Aby ustawić ramkę, karta musi posiadać wcześniej ustawiony obrazek na stronie!");

                    _ = await _img.SaveCardBorderImageFromUrlAsync(imgCheck2.Url, card);
                    break;

                case ItemType.BloodOfYourWaifu:
                    if (card.Curse == CardCurse.BloodBlockade)
                        return ExecutionResult.FromError("na tej karcie ciąży klątwa!");

                    if (card.Dere == Dere.Yami || card.Dere == Dere.Yato)
                    {
                        if (card.AttackBonus >= 1000)
                            return ExecutionResult.FromError("nie możesz bardziej ulepszyć tej karty!");

                        affectionInc = 1.2 * itemCnt;
                        karmaChange = 0.1 * itemCnt;
                        card.AttackBonus += 2 * itemCnt;
                        if (card.AttackBonus > 1000)
                        {
                            card.AttackBonus = 1000;
                        }
                        card.RateNegative += 1;
                        str.Append($"Zwiększyła się siła karty!");
                        break;
                    }

                    if (card.Dere == Dere.Raito)
                    {
                        if (card.Curse == CardCurse.None)
                        {
                            karmaChange = -1.2;
                            card.Curse = CardCurse.LoweredStats;
                        }

                        affectionInc = -10 * itemCnt;
                        str.Append($"Karta została spaczona!");
                        break;
                    }

                    if (card.CanGiveBloodOrUpgradeToSSS())
                    {
                        if (card.Rarity == Rarity.SSS)
                            return ExecutionResult.FromError("karty **SSS** nie można już ulepszyć!");

                        karmaChange = 0.2 * itemCnt;
                        affectionInc = 0.8 * itemCnt;
                        card.UpgradesCnt += 2 * itemCnt;
                        str.Append($"Zwiększono liczbę ulepszeń do {card.UpgradesCnt}!");
                        break;
                    }

                    if (card.CanGiveRing())
                    {
                        affectionInc = 0.6 * itemCnt;
                        karmaChange = 0.1 * itemCnt;
                        str.Append("Bardzo powiększyła się relacja z kartą!");
                        break;
                    }

                    affectionInc = -5 * itemCnt;
                    karmaChange = -0.01 * itemCnt;
                    embedColor = EMType.Error;
                    str.Append($"Karta się przeraziła!");
                    break;

                case ItemType.BetterIncreaseUpgradeCnt:
                    if (card.Curse == CardCurse.BloodBlockade)
                        return ExecutionResult.FromError("na tej karcie ciąży klątwa!");

                    if (card.Dere == Dere.Raito || card.Dere == Dere.Yato)
                    {
                        if (card.AttackBonus >= 1000)
                            return ExecutionResult.FromError("nie możesz bardziej ulepszyć tej karty!");

                        affectionInc = 1.2 * itemCnt;
                        karmaChange = 0.1 * itemCnt;
                        card.AttackBonus += 2 * itemCnt;
                        if (card.AttackBonus > 1000)
                        {
                            card.AttackBonus = 1000;
                        }
                        card.RatePositive += 1;
                        str.Append($"Zwiększyła się siła karty!");
                        break;
                    }

                    if (card.Dere == Dere.Yami)
                    {
                        if (card.Curse == CardCurse.None)
                        {
                            karmaChange = 1.2;
                            card.Curse = CardCurse.LoweredStats;
                        }

                        affectionInc = -10 * itemCnt;
                        str.Append($"Karta została spaczona!");
                        break;
                    }

                    if (card.CanGiveBloodOrUpgradeToSSS())
                    {
                        if (card.Rarity == Rarity.SSS)
                            return ExecutionResult.FromError("karty **SSS** nie można już ulepszyć!");

                        karmaChange = 0.2 * itemCnt;
                        affectionInc = 0.8 * itemCnt;
                        card.UpgradesCnt += 2 * itemCnt;
                        str.Append($"Zwiększono liczbę ulepszeń do {card.UpgradesCnt}!");
                        break;
                    }

                    if (card.CanGiveRing())
                    {
                        affectionInc = 0.6 * itemCnt;
                        karmaChange = 0.1 * itemCnt;
                        str.Append("Bardzo powiększyła się relacja z kartą!");
                        break;
                    }

                    affectionInc = -5 * itemCnt;
                    karmaChange = -0.01 * itemCnt;
                    embedColor = EMType.Error;
                    str.Append($"Karta się przeraziła!");
                    break;

                case ItemType.IncreaseUpgradeCnt:
                    if (!card.CanGiveRing())
                        return ExecutionResult.FromError("karta musi mieć min. poziom relacji: *Miłość*.");

                    if (card.Rarity == Rarity.SSS)
                        return ExecutionResult.FromError("karty **SSS** nie można już ulepszyć!");

                    if (card.UpgradesCnt + itemCnt > 5)
                        return ExecutionResult.FromError("nie można mieć więcej jak pięć ulepszeń dostępnych na karcie.");

                    card.UpgradesCnt += itemCnt;
                    break;

                case ItemType.RemoveCurse:
                    if (card.Curse == CardCurse.None)
                        return ExecutionResult.FromError("karta nie wie, co ma z tym zrobić!");

                    card.Curse = CardCurse.None;
                    break;

                case ItemType.DereReRoll:
                    if (card.Curse == CardCurse.DereBlockade)
                        return ExecutionResult.FromError("na tej karcie ciąży klątwa!");

                    if (!string.IsNullOrEmpty(detail) && detail.Length > 1)
                    {
                        detail = detail[0].ToString().ToUpper() + detail[1..];
                    }

                    if (itemCnt == 1 && Enum.TryParse<Dere>(detail, out var targetDere))
                    {
                        if (targetDere == Dere.Yato || targetDere == Dere.Yami || targetDere == Dere.Raito)
                            return ExecutionResult.FromError("nie można zmienić charaketru na ten który został wybrany!");

                        int usedItems = 1;
                        for (; usedItems < item.Count; usedItems++)
                        {
                            card.Dere = RandomizeDere();
                            if (card.Dere == targetDere)
                                break;
                        }

                        itemCnt = usedItems;
                        karmaChange *= itemCnt;
                        affectionInc *= itemCnt;
                        str.Append($"Użyto {itemCnt} przedmiotów by osiągnać cel!\n");
                    }
                    else
                    {
                        card.Dere = RandomizeDere();
                    }
                    break;

                case ItemType.FigureSkeleton:
                    if (card.Rarity != Rarity.SSS)
                        return ExecutionResult.FromError("karta musi być rangi **SSS**.");

                    var figure = item.ToFigure(card, _time.Now());
                    if (figure != null)
                    {
                        user.GameDeck.Figures.Add(figure);
                        user.GameDeck.Cards.Remove(card);
                        str.Append($"Rozpoczęto tworzenie figurki.");
                        break;
                    }
                    return ExecutionResult.FromError("coś poszło nie tak.");

                default:
                    return ExecutionResult.FromError($"tego przedmiotu ({item.Name}) nie powinno tutaj być!");
            }

            DeleteCardImageIfExist(card);

            str.Append(item.Type.Info(card));

            if (card.Character == user.GameDeck.Waifu)
                affectionInc *= 1.15;

            var charInfo = await _shinden.GetCharacterInfoAsync(card.Character);
            if (charInfo != null)
            {
                if (charInfo?.Points != null)
                {
                    if (charInfo.Points.Any(x => x.Name.Equals(userName)))
                        affectionInc *= 1.1;
                }
            }

            if (card.Dere == Dere.Tsundere)
                affectionInc *= 1.25;

            if (consumeItem)
                item.Count -= itemCnt;

            if (card.Curse == CardCurse.InvertedItems)
            {
                affectionInc = -affectionInc;
                karmaChange = -karmaChange;
            }

            if (card.Dere == Dere.Kamidere)
                karmaChange *= 1.3;

            if (card.Dere == Dere.Yandere)
                karmaChange *= 0.6;

            user.GameDeck.Karma += karmaChange;
            card.Affection += affectionInc;

            _ = card.CalculateCardPower();

            if (textRelation != card.GetAffectionString())
                str.Append($"\nNowa relacja to *{card.GetAffectionString()}*.");

            return activity == null ? ExecutionResult.FromSuccess(str.ToString(), embedColor) : ExecutionResult.FromSuccessWithActivity(str.ToString(), activity, embedColor);
        }

        public async Task<ExecutionResult> CheckWishlistAndSendToDMAsync(Database.DatabaseContext db, IUser discordUser, User user,
            bool hideFavs = true, bool hideBlocked = true, bool hideNames = true, bool showShindenUrl = false, SocketGuild guild = null,
            bool showContentOnly = false, ulong filrerById = 0, bool ignoreTitles = false, bool tldr = false)
        {
            if (user == null)
                return ExecutionResult.FromError("ta osoba nie ma profilu bota.");

            if (discordUser.Id != user.Id && user.GameDeck.WishlistIsPrivate)
                return ExecutionResult.FromError("lista życzeń tej osoby jest prywatna!");

            if (user.GameDeck.Wishes.Count < 1)
                return ExecutionResult.FromError("ta osoba nie ma nic na liście życzeń.");

            var t = ignoreTitles ? new List<ulong>() : user.GameDeck.GetTitlesWishList();
            var p = user.GameDeck.GetCharactersWishList();
            var c = user.GameDeck.GetCardsWishList();

            if (showContentOnly)
                return await _helper.SendEmbedsOnDMAsync(discordUser, await GetContentOfWishlistAsync(c, p, t, tldr), tldr);

            var cards = await GetCardsFromWishlistAsync(c, p, t, db, user.GameDeck.Cards);

            if (filrerById != 0)
                cards = cards.Where(x => x.GameDeckId == filrerById);
            else
                cards = cards.Where(x => x.GameDeckId != user.Id);

            if (hideFavs)
            {
                var favId = _tags.GetTagId(TagType.Favorite);
                cards = cards.Where(x => !x.Tags.Any(t => t.Id == favId));
            }

            if (hideBlocked)
                cards = cards.Where(x => x.IsTradable);

            if (cards.IsNullOrEmpty())
                return ExecutionResult.FromError("nie odnaleziono kart.");

            return await _helper.SendEmbedsOnDMAsync(discordUser, await GetWaifuFromCharacterTitleSearchResultAsync(cards, hideNames, guild, showShindenUrl, tldr), tldr);
        }
    }
}