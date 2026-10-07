#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Sanakan.Extensions;

namespace Sanakan.Database.Models
{
    public enum Rarity
    {
        SSS = 0,
        SS = 1,
        S = 2,
        A = 3,
        B = 4,
        C = 5,
        D = 6,
        E = 7
    }

    public enum Dere
    {
        Tsundere = 0,
        Kamidere = 1,
        Deredere = 2,
        Yandere = 3,
        Dandere = 4,
        Kuudere = 5,
        Mayadere = 6,
        Bodere = 7,
        Yami = 8,
        Raito = 9,
        Yato = 10
    }

    public enum CardSource
    {
        Activity = 0,
        Safari = 1,
        Shop = 2,
        GodIntervention = 3,
        Api = 4,
        Other = 5,
        Migration = 6,
        PvE = 7,
        Daily = 8,
        Crafting = 9,
        PvpShop = 10,
        Figure = 11,
        Expedition = 12,
        ActivityShop = 13,
        Lottery = 14,
        Tinkering = 15
    }

    public enum StarStyle
    {
        Full = 0,
        Empty = 1
    }

    public enum MarketValue
    {
        Normal = 0,
        Low = -1,
        High = 1
    }

    public enum PreAssembledFigure
    {
        None = 0,
        Megumin = 1,
        Asuna = 2,
        Gintoki = 3
    }

    public enum CardCurse
    {
        None = 0,
        LoweredStats = 1,
        DereBlockade = 2,
        BloodBlockade = 3,
        InvertedItems = 4,
        ExpeditionBlockade = 5,
        LoweredExperience = 6,
        FoodBlockade = 7
    }

    public enum CardExpedition
    {
        None = 0,
        NormalItemWithExp = 1,
        ExtremeItemWithExp = 2,
        DarkExp = 3,
        DarkItems = 4,
        DarkItemWithExp = 5,
        LightExp = 6,
        LightItems = 7,
        LightItemWithExp = 8,
        UltimateEasy = 9,
        UltimateMedium = 10,
        UltimateHard = 11,
        UltimateHardcore = 12
    }

    public class Card
    {
        public ulong Id { get; set; }
        public bool Active { get; set; }
        public bool InCage { get; set; }
        public bool IsTradable { get; set; }
        public double ExpCnt { get; set; }
        public double Affection { get; set; }
        public int UpgradesCnt { get; set; }
        public int RestartCnt { get; set; }
        public Rarity Rarity { get; set; }
        public Rarity RarityOnStart { get; set; }
        public Dere Dere { get; set; }
        public int Defence { get; set; }
        public int Attack { get; set; }
        public int Health { get; set; }
        public string Name { get; set; }
        public ulong Character { get; set; }
        public DateTime CreationDate { get; set; }
        public CardSource Source { get; set; }
        public string Title { get; set; }
        public string Image { get; set; }
        public string CustomImage { get; set; }
        public ulong FirstIdOwner { get; set; }
        public ulong LastIdOwner { get; set; }
        public bool Unique { get; set; }
        public StarStyle StarStyle { get; set; }
        public string CustomBorder { get; set; }
        public double MarketValue { get; set; }
        public CardCurse Curse { get; set; }
        public double CardPower { get; set; }
        public int WhoWantsCount {get; set; }
        public int AWhoWantsCount {get; set; }
        public DateTime CustomImageDate { get; set; }
        public int FixedCustomImageCnt { get; set; }
        public bool IsAnimatedImage { get; set; }
        public int RatePositive { get; set; }
        public int RateNegative { get; set; }

        public int EnhanceCnt { get; set; }
        public bool FromFigure { get; set; }
        public ulong FigureId { get; set; }
        public Quality Quality { get; set; }
        public int AttackBonus { get; set; }
        public int HealthBonus { get; set; }
        public int DefenceBonus { get; set; }
        public Quality QualityOnStart { get; set; }
        public PreAssembledFigure PAS { get; set; }
        public int BorderVariant { get; set; }
        public int BorderOverflow { get; set; }

        public CardExpedition Expedition { get; set; }
        public DateTime ExpeditionDate { get; set; }

        public DateTime ExpeditionEndDate { get; set; }
        public double Fatigue { get; set; }

        public virtual ICollection<Tag> Tags { get; set; }

        public ulong GameDeckId { get; set; }
        [JsonIgnore]
        public virtual GameDeck GameDeck { get; set; }

        [JsonIgnore]
        public virtual ICollection<TagCardRelation> Relation { get; set; }

        public override string ToString()
        {
            var marks = new[]
            {
                InCage ? "[C]" : "",
                Active ? "[A]" : "",
                Unique ? (FromFigure ? "[F]" : "[U]") : "",
                Expedition != CardExpedition.None ? "[W]" : "",
                this.IsBroken() ? "[B]" : (this.IsUnusable() ? "[N]" : ""),
            };

            string mark = marks.Any(x => x != "") ? $"**{string.Join("", marks)}** " : "";
            return $"{mark}{this.GetString(false, false, true)}";
        }
    }
}
