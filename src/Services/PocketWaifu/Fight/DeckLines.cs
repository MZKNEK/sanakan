#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.Linq;
using Sanakan.Database.Models;
using Sanakan.Extensions;

namespace Sanakan.Services.PocketWaifu.Fight
{
    // front cards are hit more often and do not hit critically
    public static class DeckLines
    {
        public const int MaxFrontCards = 4;
        public const int FrontTargetWeight = 2;
        // Auto keeps cards with this much precision at the back, where their critical hits count
        public const double AutoBackPrecision = 10;

        // at most half of the deck, so a small deck keeps a back line
        public static int FrontSlots(int cards) => Math.Min(MaxFrontCards, cards / 2);

        public static HashSet<ulong> GetFrontCards(IEnumerable<Card> deck)
        {
            var cards = deck.ToList();
            var slots = FrontSlots(cards.Count);
            var front = cards.Where(x => x.Line == CardLine.Front)
                .OrderByDescending(x => x.GetHealthWithPenalty()).ThenBy(x => x.Id).Take(slots).ToList();
            front.AddRange(cards.Where(x => x.Line == CardLine.Auto)
                .OrderBy(x => x.GetCritChance() >= AutoBackPrecision ? 1 : 0)
                .ThenByDescending(x => x.GetHealthWithPenalty()).ThenBy(x => x.Id)
                .Take(slots - front.Count));
            return front.Select(x => x.Id).ToHashSet();
        }

        public static T PickTarget<T>(IReadOnlyList<T> enemies, Func<T, bool> isFront)
        {
            if (!enemies.Any(isFront))
                return Fun.GetOneRandomFrom(enemies);

            var roll = Fun.GetRandomValue(enemies.Sum(x => isFront(x) ? FrontTargetWeight : 1));
            foreach (var enemy in enemies)
            {
                roll -= isFront(enemy) ? FrontTargetWeight : 1;
                if (roll < 0)
                    return enemy;
            }
            return enemies[^1];
        }

        public static string Name(this CardLine line) => line switch
        {
            CardLine.Front => "przód",
            CardLine.Back => "tył",
            _ => "auto",
        };

        public static bool TryParse(string text, out CardLine line)
        {
            switch (text?.Trim().ToLowerInvariant())
            {
                case "przód": case "przod": case "front": case "p":
                    line = CardLine.Front; return true;
                case "tył": case "tyl": case "back": case "t":
                    line = CardLine.Back; return true;
                case "auto": case "a":
                    line = CardLine.Auto; return true;
                default:
                    line = CardLine.Auto; return false;
            }
        }
    }
}
