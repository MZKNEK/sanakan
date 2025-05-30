﻿#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Sanakan.Extensions;

namespace Sanakan.Database.Models
{
    public enum Rarity
    {
        SSS, SS, S, A, B, C, D, E
    }

    public enum Dere
    {
        Tsundere, Kamidere, Deredere, Yandere, Dandere, Kuudere, Mayadere, Bodere, Yami, Raito, Yato
    }

    public enum CardSource
    {
        Activity, Safari, Shop, GodIntervention, Api, Other, Migration, PvE, Daily, Crafting, PvpShop, Figure, Expedition, ActivityShop, Lottery, Tinkering
    }

    public enum StarStyle
    {
        Full, Empty
    }

    public enum MarketValue
    {
        Normal = 0,
        Low = -1,
        High = 1
    }

    public enum PreAssembledFigure
    {
        None, Megumin, Asuna, Gintoki
    }

    public enum CardCurse
    {
        None, LoweredStats, DereBlockade, BloodBlockade, InvertedItems, ExpeditionBlockade, LoweredExperience, FoodBlockade
    }

    public enum CardExpedition
    {
        None, NormalItemWithExp, ExtremeItemWithExp, DarkExp, DarkItems, DarkItemWithExp, LightExp, LightItems, LightItemWithExp,
        UltimateEasy, UltimateMedium, UltimateHard, UltimateHardcore
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
