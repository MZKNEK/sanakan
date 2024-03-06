#pragma warning disable 1591

using System;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services.Time;

namespace Sanakan.Services.PocketWaifu
{
    public class Expedition
    {
        public enum ItemDropType
        {
            Food, Common, Rare, Legendary
        }

        private readonly ISystemTime _time;

        public Expedition(ISystemTime time)
        {
            _time = time;
        }

        public double GetExpFromExpedition(double length, Card card)
        {
            var expPerHour = card.Expedition switch
            {
                CardExpedition.NormalItemWithExp    => 4,
                CardExpedition.ExtremeItemWithExp   => 8.5,
                CardExpedition.LightItemWithExp     => 5.5,
                CardExpedition.DarkItemWithExp      => 5.5,
                CardExpedition.LightExp             => 25,
                CardExpedition.DarkExp              => 25,
                _ => 0
            };

            return expPerHour / 60 * length;
        }

        public int GetItemsCountFromExpedition(double length, Card card)
        {
            bool yamiOrRaito = card.Dere == Dere.Yami || card.Dere == Dere.Raito;

            var itemsPerHour = card.Expedition switch
            {
                CardExpedition.NormalItemWithExp    => 5,
                CardExpedition.ExtremeItemWithExp   => yamiOrRaito ? 44 : 22.5,
                CardExpedition.LightItemWithExp     => yamiOrRaito ? 10 : 8,
                CardExpedition.DarkItemWithExp      => yamiOrRaito ? 10 : 8,
                CardExpedition.LightItems           => yamiOrRaito ? 20 : 16,
                CardExpedition.DarkItems            => yamiOrRaito ? 20 : 16,
                CardExpedition.UltimateEasy         => yamiOrRaito ? 8 : 4,
                CardExpedition.UltimateMedium       => yamiOrRaito ? 8 : 4,
                CardExpedition.UltimateHard         => yamiOrRaito ? 12 : 6,
                CardExpedition.UltimateHardcore     => yamiOrRaito ? 4 : 2,
                _ => 0
            };

            itemsPerHour *= card.Dere == Dere.Yato ? 1.3 : 1;

            return (int) (itemsPerHour / 60 * length);
        }

        public double GetKarmaCostOfExpedition(double length, Card card)
        {
            var karmaCostPerMinute = card.Expedition switch
            {
                CardExpedition.NormalItemWithExp    => 0.00225,
                CardExpedition.ExtremeItemWithExp   => 0.07,
                CardExpedition.LightItemWithExp     => card.Dere == Dere.Yato ? 0.003 : 0.008,
                CardExpedition.LightItems           => card.Dere == Dere.Yato ? 0.003 : 0.008,
                CardExpedition.LightExp             => card.Dere == Dere.Yato ? 0.003 : 0.008,
                CardExpedition.DarkItemWithExp      => card.Dere == Dere.Yato ? 0.007 : 0.0045,
                CardExpedition.DarkItems            => card.Dere == Dere.Yato ? 0.007 : 0.0045,
                CardExpedition.DarkExp              => card.Dere == Dere.Yato ? 0.007 : 0.0045,
                _ => 0
            };

            return karmaCostPerMinute * length;
        }

        public double GetAffectionCostOfExpedition(double length, Card card)
        {
            var qualityMod = card.Quality.ValueModifierReverse();

            var affectionCostPerMinute = card.Expedition switch
            {
                CardExpedition.NormalItemWithExp    => 0.02,
                CardExpedition.ExtremeItemWithExp   => 0.375,
                CardExpedition.LightExp             => 0.155,
                CardExpedition.DarkExp              => 0.155,
                CardExpedition.DarkItems            => 0.155 * qualityMod,
                CardExpedition.LightItems           => 0.155 * qualityMod,
                CardExpedition.LightItemWithExp     => 0.125,
                CardExpedition.DarkItemWithExp      => 0.125,
                CardExpedition.UltimateEasy         => 2.5 * qualityMod,
                CardExpedition.UltimateMedium       => 2.5 * qualityMod,
                CardExpedition.UltimateHard         => 5 * qualityMod,
                CardExpedition.UltimateHardcore     => 1.5 * qualityMod,
                _ => 0
            };

            affectionCostPerMinute *= card.Rarity.ValueModifierReverse();

            return affectionCostPerMinute * length;
        }

        public double GetMaxPossibleLengthOfExpedition(User user, Card card, CardExpedition expedition = CardExpedition.None)
        {
            expedition = expedition == CardExpedition.None ? card.Expedition : expedition;
            var costOffset = user.GameDeck.Karma.IsKarmaNeutral() ? 23d : 6d;
            var costPerMinute = GetAffectionCostOfExpedition(1, card);
            var karmaBonus = user.GameDeck.Karma / 200d;
            var fuel = card.Affection;

            switch (expedition)
            {
                case CardExpedition.LightExp:
                case CardExpedition.LightItems:
                case CardExpedition.LightItemWithExp:
                    karmaBonus = Math.Min(7, karmaBonus);
                    break;

                case CardExpedition.DarkItems:
                case CardExpedition.DarkExp:
                case CardExpedition.DarkItemWithExp:
                    karmaBonus = Math.Min(12, Math.Abs(karmaBonus));
                    break;

                case CardExpedition.UltimateEasy:
                case CardExpedition.UltimateMedium:
                case CardExpedition.UltimateHard:
                case CardExpedition.UltimateHardcore:
                    fuel *= (int)card.Quality + 1;
                    costOffset = 0;
                    karmaBonus = 0;
                    break;

                default:
                    break;
            }

            costPerMinute *= card.HasImage() ? 1 : 2;
            fuel += costOffset + karmaBonus;
            var time = fuel / costPerMinute;

            time = time > 10080 ? 10080 : time;
            time = time < 0.1 ? 0.1 : time;
            return time;
        }

        public (double CalcTime, double RealTime) GetLengthOfExpedition(User user, Card card)
        {
            var maxTimeBasedOnCardParamsInMinutes = GetMaxPossibleLengthOfExpedition(user, card);
            var realTimeInMinutes = (_time.Now() - card.ExpeditionDate).TotalMinutes;
            var timeToCalculateFrom = realTimeInMinutes;

            if (maxTimeBasedOnCardParamsInMinutes < timeToCalculateFrom)
                timeToCalculateFrom = maxTimeBasedOnCardParamsInMinutes;

            return (timeToCalculateFrom, realTimeInMinutes);
        }

        public bool IsValidToGo(User user, Card card, CardExpedition expedition, TagHelper helper)
        {
            if (card.Expedition != CardExpedition.None)
                return false;

            if (card.Curse == CardCurse.ExpeditionBlockade)
                return false;

            if (card.InCage || !card.CanFightOnPvEGMwK())
                return false;

            if (GetMaxPossibleLengthOfExpedition(user, card, expedition) < 1)
                return false;

            switch (expedition)
            {
                case CardExpedition.ExtremeItemWithExp:
                    return !card.FromFigure && !helper.HasTag(card, TagType.Favorite);

                case CardExpedition.NormalItemWithExp:
                    return !card.FromFigure;

                case CardExpedition.UltimateEasy:
                case CardExpedition.UltimateHard:
                case CardExpedition.UltimateMedium:
                    return card.Rarity == Rarity.SSS;

                case CardExpedition.UltimateHardcore:
                    return card.Rarity == Rarity.SSS && !helper.HasTag(card, TagType.Favorite);

                case CardExpedition.LightItems:
                    return user.GameDeck.Karma > 1000;
                case CardExpedition.LightExp:
                    return (user.GameDeck.Karma > 1000) && !card.FromFigure;
                case CardExpedition.LightItemWithExp:
                    return (user.GameDeck.Karma > 400) && !card.FromFigure;

                case CardExpedition.DarkItems:
                    return user.GameDeck.Karma < -1000;
                case CardExpedition.DarkExp:
                    return (user.GameDeck.Karma < -1000) && !card.FromFigure;
                case CardExpedition.DarkItemWithExp:
                    return (user.GameDeck.Karma < -400) && !card.FromFigure;

                default:
                case CardExpedition.None:
                    return false;
            }
        }
    }
}