#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services.Cache;

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

        private static List<Quality> _figurePartsQuality = new List<(Quality, int)>
        {
            (Quality.Alpha,          800),
            (Quality.Beta,           400),
            (Quality.Gamma,          300),
            (Quality.Delta,          90),
            (Quality.Epsilon,        70),
            (Quality.Zeta,           55),
            (Quality.Eta,            30),
            (Quality.Theta,          25),
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

        // TC is the premium currency, so it comes in much smaller amounts than CT
        private static List<int> _tcRewards = new List<(int, int)>
        {
            (5,     60),
            (10,    40),
            (15,    10),
            (20,    5),
            (30,    1),
        }.ToRealList();

        public struct LotteryRewardInfo
        {
            public string Text;
            public LotteryReward Type;
            public string SubType;
            public int Count;
        };

        private Shinden _shinden;

        public Lottery(Shinden shinden)
        {
            _shinden = shinden;
        }

        // the experience goes to the chest, which may be disabled or almost full
        public static string ExpRewardText(int won, int stored)
        {
            if (stored <= 0)
                return $"{Polish.Count(won, "punkt", "punkty", "punktów")} doświadczenia, ale skrzynia jest nieaktywna lub pełna i przepadły!";

            var text = $"{Polish.Count(stored, "punkt", "punkty", "punktów")} doświadczenia do skrzyni";
            return stored < won ? $"{text} (z {won}, skrzynia jest pełna)!" : $"{text}!";
        }

        public static List<LotteryRewardInfo> Simplify(List<LotteryRewardInfo> rewards)
        {
            var newRewards = new List<LotteryRewardInfo>();
            foreach (var group in rewards.GroupBy(x => x.Type))
            {
                foreach (var sGroup in group.GroupBy(x => x.SubType))
                {
                    var res = sGroup.Select(x => $"{x.SubType}: {sGroup.Sum(x => x.Count)}").First();
                    newRewards.Add(new LotteryRewardInfo { Text = res });
                }
            }
            return newRewards;
        }

        // both ends included, Fun.GetRandomValue leaves the upper one out
        public static int RollCount(int min, int max) => Fun.GetRandomValue(min, max + 1);

        // about a quarter of what a hardcore expedition drops, so the lottery adds to it without replacing it
        public const int MinPills = 5;
        public const int MaxPills = 15;

        public static int MaxFigureParts(Quality quality) => quality > Quality.Jota ? 2 : quality > Quality.Delta ? 5 : 10;

        public static List<(Quality, float)> GetPartQualityChances() => _figurePartsQuality.GetChances();
        public static List<(ItemType, float)> GetPartNSChances() => _figurePartsNoSkeleton.GetChances();
        public static List<(LotteryReward, float)> GetRewardChances() => _rewardsPool.GetChances();
        public static List<(int, float)> GetMoneyRewardChances() => _moneyRewards.GetChances();
        public static List<(int, float)> GetTcRewardChances() => _tcRewards.GetChances();
        public static List<(ItemType, float)> GetPartChances() => _figureParts.GetChances();
        public static List<(ItemType, float)> GetPillsChances() => _pills.GetChances();
        public static List<(ItemType, float)> GetFoodChances() => _food.GetChances();

        public async Task<LotteryRewardInfo> GetAndApplyRewardAsync(User user)
        {
            var reward = Fun.GetOneRandomFrom(_rewardsPool);
            var rewardInfo = new LotteryRewardInfo { Count = 1, Type = reward };
            switch (reward)
            {
                case LotteryReward.CardsBig:
                case LotteryReward.CardsNormal:
                case LotteryReward.CardsFromSeason:
                {
                    string addInfo = "";
                    SeasonSnapshot season = null;
                    if (reward == LotteryReward.CardsFromSeason)
                    {
                        season = await _shinden.GetCurrentSeasonAsync();
                        if (season == null || season.Characters.Count == 0)
                        {
                            reward = LotteryReward.CardsBig;
                            rewardInfo.Type = LotteryReward.CardsBig;
                            addInfo = $"\n\nMiał być sezonowy, ale nie udało się pobrać sezonu z Shindena, więc wpadł zwykły pakiet";
                        }
                    }

                    var isBig = Fun.TakeATry(35d);
                    var name = reward switch
                    {
                        LotteryReward.CardsFromSeason => $"Sezon: {season.Name()}",
                        LotteryReward.CardsBig => isBig ? "Duży" : "Średni",
                        _ => isBig ? "Normalny" : "Mały"
                    };

                    var cnt = reward switch
                    {
                        LotteryReward.CardsFromSeason => 2,
                        LotteryReward.CardsBig => isBig ? 20 : 10,
                        _ => isBig ? 2 : 1
                    };

                    var pack = new BoosterPack
                    {
                        CardCnt = cnt,
                        Season = reward == LotteryReward.CardsFromSeason ? season.Key : null,
                        RarityExcludedFromPack = new List<RarityExcluded>(),
                        Characters = new List<BoosterPackCharacter>(),
                        Name = $"Pakiet kart z loterii ({name})",
                        CardSourceFromPack = CardSource.Lottery,
                        IsCardFromPackTradable = true,
                        MinRarity = Rarity.E,
                    };
                    user.GameDeck.BoosterPacks.Add(pack);

                    rewardInfo.Text = $"Pakiet kart z loterii ({name}){addInfo}!";
                    rewardInfo.SubType = $"Pakiet kart z loterii ({name})";
                    break;
                }

                case LotteryReward.CT:
                {
                    var ctCnt = Fun.GetOneRandomFrom(_moneyRewards);
                    user.GameDeck.CTCnt += ctCnt;

                    rewardInfo.Count = ctCnt;
                    rewardInfo.Text = $"{ctCnt} CT!";
                    rewardInfo.SubType = $"CT";
                    break;
                }

                case LotteryReward.TC:
                {
                    var tcCnt = Fun.GetOneRandomFrom(_tcRewards);
                    user.TcCnt += tcCnt;

                    rewardInfo.Count = tcCnt;
                    rewardInfo.Text = $"{tcCnt} TC!";
                    rewardInfo.SubType = $"TC";
                    break;
                }

                case LotteryReward.WaifuFood:
                {
                    var cnt = RollCount(5, 20);
                    var food = Fun.GetOneRandomFrom(_food).ToItem(cnt);
                    user.GameDeck.AddItem(food);

                    rewardInfo.Count = cnt;
                    rewardInfo.Text = $"{food.Name} x{cnt}!";
                    rewardInfo.SubType = food.Name;
                    break;
                }

                case LotteryReward.ExpForChest:
                {
                    var exp = Fun.GetOneRandomFrom(_moneyRewards);
                    var stored = (int)user.StoreExpIfPossible(exp);

                    rewardInfo.Count = stored;
                    rewardInfo.Text = ExpRewardText(exp, stored);
                    rewardInfo.SubType = "Punkty doświadczenia do skrzyni";
                    break;
                }

                case LotteryReward.FigurePart:
                {
                    var pool = _figureParts;
                    var quality = Fun.GetOneRandomFrom(_figurePartsQuality);
                    if (quality > Quality.Lambda) pool = _figurePartsNoSkeleton;
                    var cnt = RollCount(1, MaxFigureParts(quality));
                    var part = Fun.GetOneRandomFrom(pool).ToItem(cnt, quality);
                    user.GameDeck.AddItem(part);

                    rewardInfo.Count = cnt;
                    rewardInfo.Text = $"{part.Name} x{cnt}!";
                    rewardInfo.SubType = part.Name;
                    break;
                }

                case LotteryReward.RandomPill:
                {
                    var cnt = RollCount(MinPills, MaxPills);
                    var pill = Fun.GetOneRandomFrom(_pills).ToItem(cnt);
                    user.GameDeck.AddItem(pill);

                    rewardInfo.Count = cnt;
                    rewardInfo.Text = $"{pill.Name} x{cnt}!";
                    rewardInfo.SubType = pill.Name;
                    break;
                }

                case LotteryReward.Dogtag:
                {
                    var dogtag = ItemType.GiveTagSlot.ToItem();
                    user.GameDeck.AddItem(dogtag);

                    rewardInfo.Text = $"{dogtag.Name}!";
                    rewardInfo.SubType = dogtag.Name;
                    break;
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

                    rewardInfo.Text = "Odwrócenie karmy!";
                    rewardInfo.SubType = "Odwrócenie karmy";
                    break;
                }

                case LotteryReward.Scalpel:
                {
                    var scalp = ItemType.SetCustomImage.ToItem();
                    user.GameDeck.AddItem(scalp);

                    rewardInfo.Text = $"{scalp.Name}!";
                    rewardInfo.SubType = scalp.Name;
                    break;
                }

                default:
                    rewardInfo.Text = "Nic nie wygrywa?";
                    rewardInfo.SubType = "Nic";
                    break;
            }
            return rewardInfo;
        }
    }
}