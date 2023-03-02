#pragma warning disable 1591

using System.Collections.Generic;
using System.Linq;
using Sanakan.Database.Models;
using Sanakan.Extensions;

namespace Sanakan.Services.PocketWaifu
{
    public enum LotteryReward
    {
        // cards
        CardsFromSeason, CardsNormal, CardsBig,
        // items
        Scalpel, RandomPill, WaifuFood, FigurePart,
        // other
        ReverseKarma, ExpForChest, TC, CT,
        // helpers
        Quality, None
    }

    public class Lottery
    {
        private static List<ulong> _currentSeason = new List<ulong>
        {
            58089, 59500, 59830, 59686, 58578, 59213, 60965, 60289, 61318, 59274, 59368, 58928, 56308, 61773,
            60753, 59114, 59479, 59440, 60234, 60551, 58776, 57629, 59551, 59559, 59895, 59783, 60425, 60286,
            60251, 59302, 61380, 60742
        };

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

        private static List<ItemType> _pills = new List<(ItemType, int)>
        {
            (ItemType.IncreaseUltimateAttack,    25),
            (ItemType.IncreaseUltimateDefence,   15),
            (ItemType.IncreaseUltimateAll,       5),
            (ItemType.IncreaseUltimateHealth,    2),
        }.ToRealList();

        private static List<ItemType> _food = new List<(ItemType, int)>
        {
            (ItemType.AffectionRecoveryNormal,  30),
            (ItemType.AffectionRecoveryBig,     20),
            (ItemType.IncreaseExpSmall,         20),
            (ItemType.AffectionRecoveryGreat,   5),
            (ItemType.IncreaseExpBig,           5),
        }.ToRealList();

        private static List<LotteryReward> _rewardsPool = new List<(LotteryReward, int)>
        {
            (LotteryReward.ReverseKarma,    1),
            (LotteryReward.Scalpel,         2),
            (LotteryReward.TC,              15),
            (LotteryReward.CT,              25),
            (LotteryReward.ExpForChest,     30),
            (LotteryReward.CardsBig,        30),
            (LotteryReward.CardsFromSeason, 35),
            (LotteryReward.FigurePart,      40),
            (LotteryReward.RandomPill,      45),
            (LotteryReward.WaifuFood,       55),
            (LotteryReward.CardsNormal,     125),
        }.ToRealList();

        private static List<int> _moneyRewards = new List<(int, int)>
        {
            (10,    60),
            (50,    40),
            (100,   10),
            (200,   5),
            (250,   1),
        }.ToRealList();

        public static List<(Quality, float)> GetPartQualityChances() => _figurePartsQuality.GetChances();
        public static List<(LotteryReward, float)> GetRewardChances() => _rewardsPool.GetChances();
        public static List<(int, float)> GetMoneyRewardChances() => _moneyRewards.GetChances();
        public static List<(ItemType, float)> GetPartChances() => _figureParts.GetChances();
        public static List<(ItemType, float)> GetPillsChances() => _pills.GetChances();
        public static List<(ItemType, float)> GetFoodChances() => _food.GetChances();

        public static string GetAndApplyReward(User user)
        {
            var reward = Fun.GetOneRandomFrom(_rewardsPool);
            switch (reward)
            {
                case LotteryReward.CardsBig:
                case LotteryReward.CardsNormal:
                case LotteryReward.CardsFromSeason:
                {
                    var pack = new BoosterPack
                    {
                        Title = reward == LotteryReward.CardsFromSeason ? Fun.GetOneRandomFrom(_currentSeason): 0,
                        CardCnt = reward == LotteryReward.CardsBig ? 20 : 2,
                        RarityExcludedFromPack = new List<RarityExcluded>(),
                        Characters = new List<BoosterPackCharacter>(),
                        CardSourceFromPack = CardSource.Lottery,
                        Name = "Pakiet kart z loterii",
                        IsCardFromPackTradable = true,
                        MinRarity = Rarity.E,
                    };
                    user.GameDeck.BoosterPacks.Add(pack);
                    return "Pakiet kart z loterii!";
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
                    var cnt = Fun.GetRandomValue(1, 10);
                    var quality = Fun.GetOneRandomFrom(_figurePartsQuality);
                    var part = Fun.GetOneRandomFrom(_figureParts).ToItem(cnt, quality);
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

                case LotteryReward.ReverseKarma:
                {
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