#pragma warning disable 1591

using System.Collections.Generic;
using System.Linq;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services.Time;

namespace Sanakan.Services.PocketWaifu
{
    public enum EventType
    {
        MoreItems, MoreExp, IncAtk, IncDef, AddReset, NewCard,     // +
        None, ChangeDere, DecAtk, DecDef, DecAff, LoseCard, Fight  // -
    }

    public class Events
    {
        private static List<ulong> _titles = new List<ulong>
        {
            7431, 50646, 10831, 54081, 53776, 12434, 44867, 51100, 4961, 55260, 53382, 53685, 35405, 54195, 2763, 43864, 52427, 52111, 53257, 45085
        };

        private static List<EventType> _normal = new List<(EventType, int)>
        {
            (EventType.MoreExp,    4),
            (EventType.MoreItems,  3),
            (EventType.IncAtk,     1),
            (EventType.IncDef,     1),
        }.ToRealList();

        private static List<EventType> _extreme = new List<(EventType, int)>
        {
            (EventType.MoreItems,  10),
            (EventType.MoreExp,    10),
            (EventType.IncAtk,     9),
            (EventType.IncDef,     9),
            (EventType.AddReset,   1),
            (EventType.NewCard,    1),
            (EventType.ChangeDere, 8),
            (EventType.DecAtk,     10),
            (EventType.DecDef,     10),
            (EventType.DecAff,     12),
            (EventType.LoseCard,   10),
            (EventType.Fight,      10),
        }.ToRealList();

        private static List<EventType> _dakrAndLightItemsWithExp = new List<(EventType, int)>
        {
            (EventType.MoreItems,  15),
            (EventType.MoreExp,    20),
            (EventType.IncAtk,     9),
            (EventType.IncDef,     5),
            (EventType.Fight,      23),
            (EventType.ChangeDere, 7),
            (EventType.DecAtk,     6),
            (EventType.DecDef,     5),
            (EventType.DecAff,     10),
        }.ToRealList();

        private static List<EventType> _dakrAndLightItems = new List<(EventType, int)>
        {
            (EventType.IncAtk,     12),
            (EventType.IncDef,     10),
            (EventType.Fight,      22),
            (EventType.ChangeDere, 10),
            (EventType.DecAtk,     12),
            (EventType.DecDef,     14),
            (EventType.DecAff,     20),
        }.ToRealList();

        private static List<EventType> _dakrAndLightExp = new List<(EventType, int)>
        {
            (EventType.IncAtk,     12),
            (EventType.IncDef,     9),
            (EventType.Fight,      23),
            (EventType.ChangeDere, 10),
            (EventType.DecAtk,     12),
            (EventType.DecDef,     14),
            (EventType.DecAff,     20),
        }.ToRealList();

        private static List<EventType> _ultimateMed = new List<(EventType, int)>
        {
            (EventType.IncAtk,     1),
            (EventType.IncDef,     1),
            (EventType.DecAtk,     1),
            (EventType.DecDef,     1),
        }.ToRealList();

        private static List<EventType> _ultimateHard = new List<(EventType, int)>
        {
            (EventType.DecAtk,     1),
            (EventType.DecDef,     1),
        }.ToRealList();

        private static List<EventType> _ultimateHardcore = new List<(EventType, int)>
        {
            (EventType.DecAtk,     2),
            (EventType.DecDef,     2),
            (EventType.DecAff,     5),
            (EventType.LoseCard,   1),
        }.ToRealList();

        private ISystemTime _time;

        public Events(ISystemTime time)
        {
            _time = time;
        }

        private EventType CheckChanceBasedOnTime(CardExpedition expedition, (double CalcTime, double RealTime) duration)
        {
            switch (expedition)
            {
                case CardExpedition.ExtremeItemWithExp:
                    if (duration.CalcTime > 60 || duration.RealTime > 360)
                    {
                        if (Fun.TakeATry(25d))
                            return EventType.LoseCard;
                    }
                    return EventType.None;

                default:
                    return EventType.None;
            }
        }

        public string GetChancesFromExpedition(CardExpedition expedition)
        {
            var chances = expedition switch
            {
                CardExpedition.NormalItemWithExp    => _normal,
                CardExpedition.ExtremeItemWithExp   => _extreme,
                CardExpedition.DarkItems            => _dakrAndLightItems,
                CardExpedition.LightItems           => _dakrAndLightItems,
                CardExpedition.DarkItemWithExp      => _dakrAndLightItemsWithExp,
                CardExpedition.LightItemWithExp     => _dakrAndLightItemsWithExp,
                CardExpedition.DarkExp              => _dakrAndLightExp,
                CardExpedition.LightExp             => _dakrAndLightExp,

                CardExpedition.UltimateMedium       => _ultimateMed,
                CardExpedition.UltimateHard         => _ultimateHard,
                CardExpedition.UltimateHardcore     => _ultimateHardcore,

                _ => null
            };

            return chances is null ? "brak" : string.Join("\n", chances.GetChances().OrderByDescending(x => x.Item2).Select(x => $"{x.Item1} - {x.Item2:F}%"));
        }

        public EventType RandomizeEvent(CardExpedition expedition, (double CalcTime, double RealTime) duration)
        {
            var timeBased = CheckChanceBasedOnTime(expedition, duration);
            if (timeBased != EventType.None) return timeBased;

            return expedition switch
            {
                CardExpedition.NormalItemWithExp    => Fun.GetOneRandomFrom(_normal),
                CardExpedition.ExtremeItemWithExp   => Fun.GetOneRandomFrom(_extreme),
                CardExpedition.DarkItems            => Fun.GetOneRandomFrom(_dakrAndLightItems),
                CardExpedition.LightItems           => Fun.GetOneRandomFrom(_dakrAndLightItems),
                CardExpedition.DarkItemWithExp      => Fun.GetOneRandomFrom(_dakrAndLightItemsWithExp),
                CardExpedition.LightItemWithExp     => Fun.GetOneRandomFrom(_dakrAndLightItemsWithExp),
                CardExpedition.DarkExp              => Fun.GetOneRandomFrom(_dakrAndLightExp),
                CardExpedition.LightExp             => Fun.GetOneRandomFrom(_dakrAndLightExp),

                CardExpedition.UltimateMedium       => Fun.GetOneRandomFrom(_ultimateMed),
                CardExpedition.UltimateHard         => Fun.GetOneRandomFrom(_ultimateHard),
                CardExpedition.UltimateHardcore     => Fun.GetOneRandomFrom(_ultimateHardcore),

                _ => EventType.None
            };
        }

        public bool ExecuteEvent(EventType e, User user, Card card, ref string msg)
        {
            var aVal = Fun.GetRandomValue(1, 4);
            msg += "**Wydarzenie**: ";

            switch (e)
            {
                case EventType.NewCard:
                {
                    var boosterPack = new BoosterPack
                    {
                        RarityExcludedFromPack = new List<RarityExcluded>(),
                        Title = Fun.GetOneRandomFrom(_titles),
                        Characters = new List<BoosterPackCharacter>(),
                        CardSourceFromPack = CardSource.Expedition,
                        Name = "Losowa karta z wyprawy",
                        IsCardFromPackTradable = true,
                        MinRarity = Rarity.E,
                        CardCnt = 1
                    };

                    user.GameDeck.BoosterPacks.Add(boosterPack);
                    msg += "Pakiet z kartą.\n";
                }
                break;

                case EventType.IncAtk:
                {
                    card.IncAttackBy(aVal);
                    msg += $"Zwiększenie ataku do {card.GetAttackWithBonus()}.\n";
                }
                break;

                case EventType.IncDef:
                {
                    card.IncDefenceBy(aVal);
                    msg += $"Zwiększenie obrony do {card.GetDefenceWithBonus()}.\n";
                }
                break;

                case EventType.MoreExp:
                {
                    var addExp = Fun.GetRandomValue(1, 8);
                    card.ExpCnt += addExp;

                    msg += $"Dodatkowe punkty doświadczenia. (+{addExp} exp)\n";
                }
                break;

                case EventType.MoreItems:
                {
                    msg += "Dodatkowe przedmioty.\n";
                }
                break;

                case EventType.AddReset:
                {
                    ++card.RestartCnt;
                    msg += "Zwiększenie ilości restartów karty.\n";
                }
                break;

                case EventType.ChangeDere:
                {
                    msg += "Zmiana dere na ";
                }
                break;

                case EventType.DecAff:
                {
                    card.Affection -= aVal;
                    msg += "Zmniejszenie relacji.\n";
                }
                break;

                case EventType.DecAtk:
                {
                    card.DecAttackBy(aVal);
                    msg += $"Zmniejszenie ataku do {card.GetAttackWithBonus()}.\n";
                }
                break;

                case EventType.DecDef:
                {
                    card.DecDefenceBy(aVal);
                    msg += $"Zmniejszenie obrony do {card.GetDefenceWithBonus()}.\n";
                }
                break;

                case EventType.Fight:
                {
                    var enemyCard = Waifu.GenerateNewCard("Miecu", "Bajeczka", null, Waifu.RandomizeRarity(), _time.Now());
                    var result = Waifu.GetFightWinner(card, enemyCard);

                    string resStr = result == FightWinner.Card1 ? "zwycięstwo!" : "przegrana!";
                    msg += $"Walka, wynik: {resStr}\n";

                    return result == FightWinner.Card1;
                }

                case EventType.LoseCard:
                {
                    user.GameDeck.Cards.Remove(card);
                    msg += "Utrata karty.\n";
                }
                return false;

                default:
                    return true;
            }

            return true;
        }

        public int GetMoreItems(EventType e) => e switch
        {
            EventType.MoreItems => Fun.GetRandomValue(2, 22),
            _ => 0,
        };
    }
}
