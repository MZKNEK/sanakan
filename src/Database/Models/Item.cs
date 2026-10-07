#pragma warning disable 1591

using Newtonsoft.Json;

namespace Sanakan.Database.Models
{
    public enum ItemType
    {
        AffectionRecoverySmall = 0,
        AffectionRecoveryNormal = 1,
        AffectionRecoveryBig = 2,
        IncreaseUpgradeCnt = 3,
        CardParamsReRoll = 4,
        DereReRoll = 5,
        RandomBoosterPackSingleE = 6,
        RandomNormalBoosterPackB = 7,
        RandomTitleBoosterPackSingleE = 8,
        RandomNormalBoosterPackA = 9,
        RandomNormalBoosterPackS = 10,
        RandomNormalBoosterPackSS = 11,
        AffectionRecoveryGreat = 12,
        BetterIncreaseUpgradeCnt = 13,
        CheckAffection = 14,
        SetCustomImage = 15,
        IncreaseExpSmall = 16,
        IncreaseExpBig = 17,
        ChangeStarType = 18,
        SetCustomBorder = 19,
        ChangeCardImage = 20,
        PreAssembledMegumin = 21,
        PreAssembledGintoki = 22,
        PreAssembledAsuna = 23,
        FigureSkeleton = 24,
        FigureUniversalPart = 25,
        FigureHeadPart = 26,
        FigureBodyPart = 27,
        FigureLeftArmPart = 28,
        FigureRightArmPart = 29,
        FigureLeftLegPart = 30,
        FigureRightLegPart = 31,
        FigureClothesPart = 32,
        BigRandomBoosterPackE = 33,
        ResetCardValue = 34,
        LotteryTicket = 35,
        IncreaseUltimateAttack = 36,
        IncreaseUltimateDefence = 37,
        IncreaseUltimateHealth = 38,
        IncreaseUltimateAll = 39,
        CardFragment = 40,
        BloodOfYourWaifu = 41,
        SetCustomAnimatedImage = 42,
        GiveTagSlot = 43,
        RemoveCurse = 44,
        CreationItemBase = 45,
        NotAnItem = 46,
        CheckCurse = 47,
        ChangeBorderVariant = 48
    }

    public class Item
    {
        public ulong Id { get; set; }
        public long Count { get; set; }
        public string Name { get; set; }
        public ItemType Type { get; set; }
        public Quality Quality { get; set; }

        public ulong GameDeckId { get; set; }
        [JsonIgnore]
        public virtual GameDeck GameDeck { get; set; }
    }
}
