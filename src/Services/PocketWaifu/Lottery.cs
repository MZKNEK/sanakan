#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services.Time;
using Shinden;

namespace Sanakan.Services.PocketWaifu
{
    public enum LotteryReward
    {
        // cards
        CardsFromSeason, CardsNormal, CardsBig,
        // items
        Scalpel, RandomPill, WaifuFood, FigurePart, Dogtag,
        // other
        ReverseKarma, ExpForChest, TC, CT,
        // helpers
        Quality, None, FigurePartNS
    }

    public class Lottery
    {
        private static CharacterPool<ulong> _currentSeason = new CharacterPool<ulong>();

        private static List<Quality> _figurePartsQuality = new List<(Quality, int)>
        {
            (Quality.Alpha,          800),
            (Quality.Beta,           400),
            (Quality.Gamma,          300),
            (Quality.Delta,          100),
            (Quality.Epsilon,        80),
            (Quality.Zeta,           60),
            (Quality.Theta,          30),
            (Quality.Jota,           20),
            (Quality.Lambda,         10),
            (Quality.Sigma,          5),
            (Quality.Omega,          1),
        }.ToRealList();

        private static List<ItemType> _figureParts = new List<(ItemType, int)>
        {
            (ItemType.FigureBodyPart,           1),
            (ItemType.FigureClothesPart,        1),
            (ItemType.FigureHeadPart,           1),
            (ItemType.FigureLeftArmPart,        1),
            (ItemType.FigureLeftLegPart,        1),
            (ItemType.FigureRightArmPart,       1),
            (ItemType.FigureRightLegPart,       1),
            (ItemType.FigureSkeleton,           15),
            (ItemType.FigureUniversalPart,      30),
        }.ToRealList();

        private static List<ItemType> _figurePartsNoSkeleton = new List<(ItemType, int)>
        {
            (ItemType.FigureBodyPart,           5),
            (ItemType.FigureClothesPart,        5),
            (ItemType.FigureHeadPart,           5),
            (ItemType.FigureLeftArmPart,        5),
            (ItemType.FigureLeftLegPart,        5),
            (ItemType.FigureRightArmPart,       5),
            (ItemType.FigureRightLegPart,       5),
            (ItemType.FigureUniversalPart,      10),
        }.ToRealList();

        private static List<ItemType> _pills = new List<(ItemType, int)>
        {
            (ItemType.IncreaseUltimateAttack,    25),
            (ItemType.IncreaseUltimateDefence,   15),
            (ItemType.IncreaseUltimateHealth,    7),
            (ItemType.IncreaseUltimateAll,       3),
        }.ToRealList();

        private static List<ItemType> _food = new List<(ItemType, int)>
        {
            (ItemType.AffectionRecoveryNormal,  30),
            (ItemType.IncreaseExpSmall,         20),
            (ItemType.AffectionRecoveryBig,     15),
            (ItemType.IncreaseExpBig,           10),
            (ItemType.AffectionRecoveryGreat,   5),
        }.ToRealList();

        private static List<LotteryReward> _rewardsPool = new List<(LotteryReward, int)>
        {
            (LotteryReward.ReverseKarma,    1),
            (LotteryReward.Dogtag,          2),
            (LotteryReward.Scalpel,         10),
            (LotteryReward.TC,              15),
            (LotteryReward.CardsFromSeason, 18),
            (LotteryReward.CT,              25),
            (LotteryReward.CardsBig,        26),
            (LotteryReward.ExpForChest,     30),
            (LotteryReward.FigurePart,      40),
            (LotteryReward.RandomPill,      45),
            (LotteryReward.WaifuFood,       55),
            (LotteryReward.CardsNormal,     115),
        }.ToRealList();

        private static List<int> _moneyRewards = new List<(int, int)>
        {
            (50,    60),
            (100,   40),
            (150,   10),
            (200,   5),
            (300,   1),
        }.ToRealList();

        private ShindenClient _shClient;
        private ISystemTime _time;

        public Lottery(ShindenClient client, ISystemTime time)
        {
            _shClient = client;
            _time = time;
        }

        public static List<(Quality, float)> GetPartQualityChances() => _figurePartsQuality.GetChances();
        public static List<(ItemType, float)> GetPartNSChances() => _figurePartsNoSkeleton.GetChances();
        public static List<(LotteryReward, float)> GetRewardChances() => _rewardsPool.GetChances();
        public static List<(int, float)> GetMoneyRewardChances() => _moneyRewards.GetChances();
        public static List<(ItemType, float)> GetPartChances() => _figureParts.GetChances();
        public static List<(ItemType, float)> GetPillsChances() => _pills.GetChances();
        public static List<(ItemType, float)> GetFoodChances() => _food.GetChances();

        public async Task<ulong> GetRandomTitleAsync()
        {
            if (_currentSeason.IsNeedForUpdate(_time.Now()))
            {
                try
                {
                    var res = await _shClient.Ex.GetAnimeFromSeasonAsync();
                    if (!res.IsSuccessStatusCode()) return 0;

                    _currentSeason.Update(res.Body, _time.Now());
                }
                catch (Exception)
                {
                    return 0;
                }
            }

            return _currentSeason.GetOneRandom();
        }

        public async Task<string> GetAndApplyRewardAsync(User user)
        {
            var reward = Fun.GetOneRandomFrom(_rewardsPool);
            switch (reward)
            {
                case LotteryReward.CardsBig:
                case LotteryReward.CardsNormal:
                case LotteryReward.CardsFromSeason:
                {
                    string addInfo = "";
                    ulong randomTitle = 0;
                    if (reward == LotteryReward.CardsFromSeason)
                    {
                        randomTitle = await GetRandomTitleAsync();
                        reward = randomTitle == 0 ? LotteryReward.CardsBig : LotteryReward.CardsFromSeason;
                        addInfo = randomTitle == 0 ? $"\n\nPowinien być sezonowy, ale wpadła 500 od shindena." : "";
                    }

                    var name = reward == LotteryReward.CardsFromSeason ? "Sezonowy"
                        : (reward == LotteryReward.CardsBig ? "Duży" : "Normalny");

                    var pack = new BoosterPack
                    {
                        Title = reward == LotteryReward.CardsFromSeason ? randomTitle : 0,
                        CardCnt = reward == LotteryReward.CardsBig ? 20 : 2,
                        RarityExcludedFromPack = new List<RarityExcluded>(),
                        Characters = new List<BoosterPackCharacter>(),
                        Name = $"Pakiet kart z loterii ({name})",
                        CardSourceFromPack = CardSource.Lottery,
                        IsCardFromPackTradable = true,
                        MinRarity = Rarity.E,
                    };
                    user.GameDeck.BoosterPacks.Add(pack);
                    return $"Pakiet kart z loterii ({name}){addInfo}!";
                }

                case LotteryReward.CT:
                {
                    var ctCnt = Fun.GetOneRandomFrom(_moneyRewards);
                    user.GameDeck.CTCnt += ctCnt;
                    return $"{ctCnt} CT!";
                }

                case LotteryReward.TC:
                {
                    var tcCnt = Fun.GetOneRandomFrom(_moneyRewards);
                    user.TcCnt += tcCnt;
                    return $"{tcCnt} TC!";
                }

                case LotteryReward.WaifuFood:
                {
                    var cnt = Fun.GetRandomValue(5, 20);
                    var food = Fun.GetOneRandomFrom(_food).ToItem(cnt);
                    user.GameDeck.AddItem(food);
                    return $"{food.Name} x{cnt}!";
                }

                case LotteryReward.ExpForChest:
                {
                    var exp = Fun.GetOneRandomFrom(_moneyRewards);
                    user.StoreExpIfPossible(exp);
                    return $"{exp} punktów doświadczenia!";
                }

                case LotteryReward.FigurePart:
                {
                    var max = 10;
                    var pool = _figureParts;
                    var quality = Fun.GetOneRandomFrom(_figurePartsQuality);
                    if (quality > Quality.Jota) max = 2;
                    else if (quality > Quality.Delta) max = 5;
                    if (quality > Quality.Lambda) pool = _figurePartsNoSkeleton;
                    var cnt = Fun.GetRandomValue(1, max);
                    var part = Fun.GetOneRandomFrom(pool).ToItem(cnt, quality);
                    user.GameDeck.AddItem(part);
                    return $"{part.Name} x{cnt}!";
                }

                case LotteryReward.RandomPill:
                {
                    var cnt = Fun.GetRandomValue(1, 5);
                    var pill = Fun.GetOneRandomFrom(_pills).ToItem(cnt);
                    user.GameDeck.AddItem(pill);
                    return $"{pill.Name} x{cnt}!";
                }

                case LotteryReward.Dogtag:
                {
                    var dogtag = ItemType.GiveTagSlot.ToItem();
                    user.GameDeck.AddItem(dogtag);
                    return $"{dogtag.Name}!";
                }

                case LotteryReward.ReverseKarma:
                {
                    user.Stats.ReversedKarmaCnt++;
                    user.GameDeck.Karma = -user.GameDeck.Karma;
                    foreach (var card in user.GameDeck.Cards.Where(x => x.Dere == Dere.Yami || x.Dere == Dere.Raito))
                    {
                        if (card.Dere == Dere.Yami)
                        {
                            card.Dere = Dere.Raito;
                        }
                        else if (card.Dere == Dere.Raito)
                        {
                            card.Dere = Dere.Yami;
                        }
                    }
                    foreach (var figure in user.GameDeck.Figures.Where(x => x.Dere == Dere.Yami || x.Dere == Dere.Raito))
                    {
                        if (figure.IsComplete)
                            continue;

                        if (figure.Dere == Dere.Yami)
                        {
                            figure.Dere = Dere.Raito;
                        }
                        else if (figure.Dere == Dere.Raito)
                        {
                            figure.Dere = Dere.Yami;
                        }
                    }
                    return "odwrócenie karmy!";
                }

                case LotteryReward.Scalpel:
                {
                    var scalp = ItemType.SetCustomImage.ToItem();
                    user.GameDeck.AddItem(scalp);
                    return $"{scalp.Name}!";
                }

                default:
                    return "Nic nie wgrywa?";
            }
        }
    }
}