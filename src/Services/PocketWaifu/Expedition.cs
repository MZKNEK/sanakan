#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.Linq;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services.Time;

namespace Sanakan.Services.PocketWaifu
{
    public class Expedition
    {
        public enum ItemDropType
        {
            None, Food, Common, Rare, Legendary
        }

        private static readonly List<CardCurse> _curseChances = new List<(CardCurse, int)>
        {
            (CardCurse.BloodBlockade,       1),
            (CardCurse.DereBlockade,        1),
            (CardCurse.ExpeditionBlockade,  1),
            (CardCurse.FoodBlockade,        1),
            (CardCurse.InvertedItems,       1),
            (CardCurse.LoweredExperience,   1),
            (CardCurse.LoweredStats,        1),
        }.ToRealList();

        private static readonly Dictionary<ItemDropType, List<ItemType>> _ultimateExpeditionItems = new Dictionary<ItemDropType, List<ItemType>>
        {
            {ItemDropType.None, new List<ItemType>()},
            {ItemDropType.Food, new List<ItemType>()},
            {ItemDropType.Common, new List<(ItemType, int)>
                {
                    (ItemType.FigureBodyPart,          1),
                    (ItemType.FigureClothesPart,       1),
                    (ItemType.FigureHeadPart,          1),
                    (ItemType.FigureLeftArmPart,       1),
                    (ItemType.FigureLeftLegPart,       1),
                    (ItemType.FigureRightArmPart,      1),
                    (ItemType.FigureRightLegPart,      1),
                }.ToRealList()
            },
            {ItemDropType.Rare, new List<(ItemType, int)>
                {
                    (ItemType.FigureSkeleton,          2),
                    (ItemType.FigureUniversalPart,     1),
                }.ToRealList()
            },
            {ItemDropType.Legendary, new List<(ItemType, int)>
                {
                    (ItemType.NotAnItem,                13),
                    (ItemType.LotteryTicket,            1),
                }.ToRealList()
            },
        };

        private static readonly Dictionary<ItemDropType, List<ItemType>> _ultimateExpeditionHardcoreItems = new Dictionary<ItemDropType, List<ItemType>>
        {
            {ItemDropType.None, new List<ItemType>()},
            {ItemDropType.Food, new List<ItemType>()},
            {ItemDropType.Common, new List<(ItemType, int)>
                {
                    (ItemType.FigureBodyPart,          1),
                    (ItemType.FigureClothesPart,       1),
                    (ItemType.FigureHeadPart,          1),
                    (ItemType.FigureLeftArmPart,       1),
                    (ItemType.FigureLeftLegPart,       1),
                    (ItemType.FigureRightArmPart,      1),
                    (ItemType.FigureRightLegPart,      1),
                }.ToRealList()
            },
            {ItemDropType.Rare, new List<(ItemType, int)>
                {
                    (ItemType.FigureSkeleton,          4),
                    (ItemType.FigureUniversalPart,     3),
                }.ToRealList()
            },
            {ItemDropType.Legendary, new List<(ItemType, int)>
                {
                    (ItemType.LotteryTicket,           2),
                    (ItemType.IncreaseUltimateAttack,  4),
                    (ItemType.IncreaseUltimateDefence, 3),
                    (ItemType.IncreaseUltimateHealth,  1),
                }.ToRealList()
            },
        };

        private static readonly Dictionary<ItemDropType, List<ItemType>> _itemsFromNormalExpedition = new Dictionary<ItemDropType, List<ItemType>>
        {
            {ItemDropType.None, new List<ItemType>()},
            {ItemDropType.Food, new List<(ItemType, int)>
                {
                    (ItemType.AffectionRecoverySmall,   15),
                    (ItemType.AffectionRecoveryNormal,  10),
                    (ItemType.AffectionRecoveryBig,     3),
                    (ItemType.AffectionRecoveryGreat,   1),
                }.ToRealList()
            },
            {ItemDropType.Common, new List<(ItemType, int)>
                {
                    (ItemType.DereReRoll,               3),
                    (ItemType.CardParamsReRoll,         3),
                    (ItemType.IncreaseExpSmall,         1),
                }.ToRealList()
            },
            {ItemDropType.Rare, new List<(ItemType, int)>
                {
                    (ItemType.IncreaseUpgradeCnt,       1),
                }.ToRealList()
            },
            {ItemDropType.Legendary, new List<(ItemType, int)>
                {
                    (ItemType.CreationItemBase,         1),
                }.ToRealList()
            },
        };

        private static readonly Dictionary<ItemDropType, List<ItemType>> _itemsFromExtremeExpedition = new Dictionary<ItemDropType, List<ItemType>>
        {
            {ItemDropType.None, new List<ItemType>()},
            {ItemDropType.Food, new List<(ItemType, int)>
                {
                    (ItemType.AffectionRecoveryNormal,  7),
                    (ItemType.AffectionRecoveryBig,     5),
                    (ItemType.AffectionRecoveryGreat,   2),
                }.ToRealList()
            },
            {ItemDropType.Common, new List<(ItemType, int)>
                {
                    (ItemType.IncreaseExpSmall,         3),
                    (ItemType.IncreaseExpBig,           1),
                }.ToRealList()
            },
            {ItemDropType.Rare, new List<(ItemType, int)>
                {
                    (ItemType.IncreaseUpgradeCnt,       35),
                    (ItemType.NotAnItem,                65),
                }.ToRealList()
            },
            {ItemDropType.Legendary, new List<(ItemType, int)>
                {
                    (ItemType.BetterIncreaseUpgradeCnt, 39),
                    (ItemType.BloodOfYourWaifu,         39),
                    (ItemType.CreationItemBase,         12),
                    (ItemType.NotAnItem,                10),
                }.ToRealList()
            },
        };

        private static readonly Dictionary<ItemDropType, List<ItemType>> _itemsFromDarkAndLightExpeditionAndExp = new Dictionary<ItemDropType, List<ItemType>>
        {
            {ItemDropType.None, new List<ItemType>()},
            {ItemDropType.Food, new List<(ItemType, int)>
                {
                    (ItemType.AffectionRecoveryNormal,  6),
                    (ItemType.AffectionRecoveryBig,     3),
                    (ItemType.AffectionRecoveryGreat,   1),
                }.ToRealList()
            },
            {ItemDropType.Common, new List<(ItemType, int)>
                {
                    (ItemType.DereReRoll,               4),
                    (ItemType.CardParamsReRoll,         4),
                    (ItemType.IncreaseExpBig,           2),
                }.ToRealList()
            },
            {ItemDropType.Rare, new List<ItemType>()},
            {ItemDropType.Legendary, new List<(ItemType, int)>
                {
                    (ItemType.IncreaseUpgradeCnt,       8),
                    (ItemType.CreationItemBase,         2),
                }.ToRealList()
            },
        };

        private static readonly Dictionary<ItemDropType, List<ItemType>> _itemsFromDarkExpedition = new Dictionary<ItemDropType, List<ItemType>>
        {
            {ItemDropType.None, new List<ItemType>()},
            {ItemDropType.Food, new List<(ItemType, int)>
                {
                    (ItemType.AffectionRecoverySmall,   4),
                    (ItemType.AffectionRecoveryNormal,  3),
                    (ItemType.AffectionRecoveryBig,     2),
                    (ItemType.AffectionRecoveryGreat,   1),
                }.ToRealList()
            },
            {ItemDropType.Common, new List<(ItemType, int)>
                {
                    (ItemType.IncreaseExpSmall,         1),
                }.ToRealList()
            },
            {ItemDropType.Rare, new List<(ItemType, int)>
                {
                    (ItemType.IncreaseUpgradeCnt,       1),
                }.ToRealList()
            },
            {ItemDropType.Legendary, new List<(ItemType, int)>
                {
                    (ItemType.BetterIncreaseUpgradeCnt, 1),
                }.ToRealList()
            },
        };

        private static readonly Dictionary<ItemDropType, List<ItemType>> _itemsFromLightExpedition = new Dictionary<ItemDropType, List<ItemType>>
        {
            {ItemDropType.None, new List<ItemType>()},
            {ItemDropType.Food, new List<(ItemType, int)>
                {
                    (ItemType.AffectionRecoverySmall,   4),
                    (ItemType.AffectionRecoveryNormal,  3),
                    (ItemType.AffectionRecoveryBig,     2),
                    (ItemType.AffectionRecoveryGreat,   1),
                }.ToRealList()
            },
            {ItemDropType.Common, new List<(ItemType, int)>
                {
                    (ItemType.IncreaseExpSmall,         1),
                }.ToRealList()
            },
            {ItemDropType.Rare, new List<(ItemType, int)>
                {
                    (ItemType.IncreaseUpgradeCnt,       1),
                }.ToRealList()
            },
            {ItemDropType.Legendary, new List<(ItemType, int)>
                {
                    (ItemType.BloodOfYourWaifu,         1),
                }.ToRealList()
            },
        };

        private static readonly Dictionary<CardExpedition, List<ItemDropType>> _itemChanceOfItemTypeOnExpedition = new Dictionary<CardExpedition, List<ItemDropType>>
        {
            {CardExpedition.NormalItemWithExp, new List<(ItemDropType, int)>
                {
                    (ItemDropType.Food,       150),
                    (ItemDropType.Common,     100),
                    (ItemDropType.Rare,       60),
                    (ItemDropType.Legendary,  1),
                }.ToRealList()
            },
            {CardExpedition.ExtremeItemWithExp, new List<(ItemDropType, int)>
                {
                    (ItemDropType.Common,     46),
                    (ItemDropType.Rare,       20),
                    (ItemDropType.Legendary,  17),
                }.ToRealList()
            },
            {CardExpedition.DarkItems, new List<(ItemDropType, int)>
                {
                    (ItemDropType.Common,     63),
                    (ItemDropType.Rare,       24),
                    (ItemDropType.Legendary,  13),
                }.ToRealList()
            },
            {CardExpedition.DarkItemWithExp, new List<(ItemDropType, int)>
                {
                    (ItemDropType.None,       10),
                    (ItemDropType.Common,     84),
                    (ItemDropType.Legendary,  6),
                }.ToRealList()
            },
            {CardExpedition.LightItems, new List<(ItemDropType, int)>
                {
                    (ItemDropType.Common,     63),
                    (ItemDropType.Rare,       24),
                    (ItemDropType.Legendary,  13),
                }.ToRealList()
            },
            {CardExpedition.LightItemWithExp, new List<(ItemDropType, int)>
                {
                    (ItemDropType.None,       10),
                    (ItemDropType.Common,     84),
                    (ItemDropType.Legendary,  6),
                }.ToRealList()
            },
            {CardExpedition.UltimateEasy, new List<(ItemDropType, int)>
                {
                    (ItemDropType.Common,     82),
                    (ItemDropType.Rare,       16),
                    (ItemDropType.Legendary,  2),
                }.ToRealList()
            },
            {CardExpedition.UltimateMedium, new List<(ItemDropType, int)>
                {
                    (ItemDropType.Common,     76),
                    (ItemDropType.Rare,       21),
                    (ItemDropType.Legendary,  3),
                }.ToRealList()
            },
            {CardExpedition.UltimateHard, new List<(ItemDropType, int)>
                {
                    (ItemDropType.Common,     70),
                    (ItemDropType.Rare,       22),
                    (ItemDropType.Legendary,  8),
                }.ToRealList()
            },
            {CardExpedition.UltimateHardcore, new List<(ItemDropType, int)>
                {
                    (ItemDropType.Common,     65),
                    (ItemDropType.Rare,       22),
                    (ItemDropType.Legendary,  13),
                }.ToRealList()
            }
        };

        private static readonly Dictionary<CardExpedition, List<Quality>> _qualityOnExpedition = new Dictionary<CardExpedition, List<Quality>>
        {
            {CardExpedition.NormalItemWithExp,  new List<Quality> { Quality.Broken }},
            {CardExpedition.ExtremeItemWithExp, new List<Quality> { Quality.Broken }},
            {CardExpedition.DarkItems,          new List<Quality> { Quality.Broken }},
            {CardExpedition.DarkItemWithExp,    new List<Quality> { Quality.Broken }},
            {CardExpedition.LightItems,         new List<Quality> { Quality.Broken }},
            {CardExpedition.LightItemWithExp,   new List<Quality> { Quality.Broken }},
            {CardExpedition.UltimateEasy, new List<(Quality, int)>
                {
                    (Quality.Alpha,    50),
                    (Quality.Beta,     25),
                    (Quality.Gamma,    22),
                    (Quality.Delta,    3),
                }.ToRealList()
            },
            {CardExpedition.UltimateMedium, new List<(Quality, int)>
                {
                    (Quality.Alpha,    45),
                    (Quality.Beta,     30),
                    (Quality.Gamma,    20),
                    (Quality.Delta,    5),
                    (Quality.Epsilon,  1),
                    (Quality.Zeta,     1),
                }.ToRealList()
            },
            {CardExpedition.UltimateHard, new List<(Quality, int)>
                {
                    (Quality.Alpha,    550),
                    (Quality.Beta,     200),
                    (Quality.Gamma,    130),
                    (Quality.Delta,    60),
                    (Quality.Epsilon,  40),
                    (Quality.Zeta,     10),
                    (Quality.Theta,    6),
                    (Quality.Jota,     3),
                    (Quality.Lambda,   1),
                }.ToRealList()
            },
            {CardExpedition.UltimateHardcore, new List<(Quality, int)>
                {
                    (Quality.Alpha,    2000),
                    (Quality.Beta,     3000),
                    (Quality.Gamma,    2000),
                    (Quality.Delta,    1000),
                    (Quality.Epsilon,  1000),
                    (Quality.Zeta,     500),
                    (Quality.Theta,    350),
                    (Quality.Jota,     130),
                    (Quality.Lambda,   15),
                    (Quality.Sigma,    4),
                    (Quality.Omega,    1),
                }.ToRealList()
            }
        };

        private readonly ISystemTime _time;

        public Expedition(ISystemTime time)
        {
            _time = time;
        }

        public CardCurse GetPotentialCurse(CardExpedition expedition)
        {
            var checkCurse = expedition switch
            {
                CardExpedition.ExtremeItemWithExp   => Fun.TakeATry(5d),
                CardExpedition.UltimateMedium       => Fun.TakeATry(15d),
                CardExpedition.UltimateHard         => Fun.TakeATry(25d),
                CardExpedition.UltimateHardcore     => Fun.TakeATry(35d),
                _ => false
            };

            if (checkCurse)
                return Fun.GetOneRandomFrom(_curseChances);

            return CardCurse.None;
        }

        public List<string> GetChancesFromExpedition(CardExpedition expedition)
        {
            var itemDropTypeChances = _itemChanceOfItemTypeOnExpedition[expedition].GetChances();
            var drop = expedition switch
            {
                CardExpedition.UltimateHardcore     => _ultimateExpeditionHardcoreItems,
                CardExpedition.UltimateEasy         => _ultimateExpeditionItems,
                CardExpedition.UltimateMedium       => _ultimateExpeditionItems,
                CardExpedition.UltimateHard         => _ultimateExpeditionItems,
                CardExpedition.NormalItemWithExp    => _itemsFromNormalExpedition,
                CardExpedition.ExtremeItemWithExp   => _itemsFromExtremeExpedition,
                CardExpedition.DarkItemWithExp      => _itemsFromDarkAndLightExpeditionAndExp,
                CardExpedition.DarkItems            => _itemsFromDarkExpedition,
                CardExpedition.LightItemWithExp     => _itemsFromDarkAndLightExpeditionAndExp,
                CardExpedition.LightItems           => _itemsFromLightExpedition,
                _ => new Dictionary<ItemDropType, List<ItemType>>()
            };

            var output = new List<string>();
            if (expedition.HasDifferentQualitiesOnExpedition())
            {
                output.Add($"**Quality**:\n{string.Join("\n", _qualityOnExpedition[expedition].GetChances().OrderByDescending(x => x.Item2).Select(x => $"{x.Item1} - {x.Item2:F}%"))}\n");
            }

            foreach (var type in itemDropTypeChances.OrderByDescending(x => x.Item2))
            {
                output.Add($"**{type.Item1} ({type.Item2:F}%)**:\n{string.Join("\n", drop[type.Item1].GetChances().OrderByDescending(x => x.Item2).Select(x => $"{x.Item1} - {x.Item2:F}%"))}\n");
            }

            return output;
        }

        public Item RandomizeItemFor(CardExpedition expedition, ItemDropType dropType)
        {
            var itemType = expedition switch
            {
                CardExpedition.UltimateHardcore     => Fun.GetOneRandomFrom(_ultimateExpeditionHardcoreItems[dropType]),
                CardExpedition.UltimateEasy         => Fun.GetOneRandomFrom(_ultimateExpeditionItems[dropType]),
                CardExpedition.UltimateMedium       => Fun.GetOneRandomFrom(_ultimateExpeditionItems[dropType]),
                CardExpedition.UltimateHard         => Fun.GetOneRandomFrom(_ultimateExpeditionItems[dropType]),
                CardExpedition.NormalItemWithExp    => Fun.GetOneRandomFrom(_itemsFromNormalExpedition[dropType]),
                CardExpedition.ExtremeItemWithExp   => Fun.GetOneRandomFrom(_itemsFromExtremeExpedition[dropType]),
                CardExpedition.DarkItemWithExp      => Fun.GetOneRandomFrom(_itemsFromDarkAndLightExpeditionAndExp[dropType]),
                CardExpedition.DarkItems            => Fun.GetOneRandomFrom(_itemsFromDarkExpedition[dropType]),
                CardExpedition.LightItemWithExp     => Fun.GetOneRandomFrom(_itemsFromDarkAndLightExpeditionAndExp[dropType]),
                CardExpedition.LightItems           => Fun.GetOneRandomFrom(_itemsFromLightExpedition[dropType]),

                _ => ItemType.AffectionRecoverySmall
            };

            if (itemType.HasDifferentQualities() && expedition.HasDifferentQualitiesOnExpedition())
            {
                return itemType.ToItem(1, RandomizeItemQualityFromExpedition(expedition));
            }

            return itemType.ToItem();
        }

        public ItemDropType RandomizeItemDropTypeFor(CardExpedition expedition) => expedition switch
        {
            CardExpedition.DarkExp  => ItemDropType.None,
            CardExpedition.LightExp => ItemDropType.None,
            _ => Fun.GetOneRandomFrom(_itemChanceOfItemTypeOnExpedition[expedition])
        };

        private Quality RandomizeItemQualityFromExpedition(CardExpedition type)
            => Fun.GetOneRandomFrom(_qualityOnExpedition[type]);

        public double GetExpFromExpedition(double length, Card card)
        {
            if (card.FromFigure)
                return 0;

            var expPerHour = card.Expedition switch
            {
                CardExpedition.NormalItemWithExp    => 2,
                CardExpedition.ExtremeItemWithExp   => 2.2,
                CardExpedition.LightItemWithExp     => 2.5,
                CardExpedition.DarkItemWithExp      => 2.5,
                CardExpedition.LightExp             => 16,
                CardExpedition.DarkExp              => 16,
                _ => 0
            };

            var curseMod = card.Curse == CardCurse.LoweredExperience ? 0.2 : 1;

            return expPerHour / 60 * length * curseMod;
        }

        public double GetFatigueFromExpedition(double length, Card card)
        {
            var perMinute = card.Expedition switch
            {
                CardExpedition.NormalItemWithExp    => 0.034,
                CardExpedition.ExtremeItemWithExp   => 0.099,
                CardExpedition.UltimateEasy         => 0.076,
                CardExpedition.UltimateMedium       => 0.080,
                CardExpedition.UltimateHard         => 0.090,
                CardExpedition.UltimateHardcore     => 0.124,
                _ => 0.049
            };

            var dereMod = card.Dere switch
            {
                Dere.Dandere => 0.7,
                _ => 1
            };

            return perMinute * length * dereMod;
        }

        public int GetItemsCountFromExpedition(double length, Card card, User user)
        {
            bool specialDere = card.Dere == Dere.Yami || card.Dere == Dere.Raito || card.Dere == Dere.Yato;

            var itemsPerHour = card.Expedition switch
            {
                CardExpedition.NormalItemWithExp    => user.GameDeck.IsNeutral() ? 7.5 : 5,
                CardExpedition.ExtremeItemWithExp   => specialDere ? 44 : 22.5,
                CardExpedition.LightItemWithExp     => specialDere ? 10 : 8,
                CardExpedition.DarkItemWithExp      => specialDere ? 10 : 8,
                CardExpedition.LightItems           => specialDere ? 20 : 16,
                CardExpedition.DarkItems            => specialDere ? 20 : 16,
                CardExpedition.UltimateEasy         => specialDere ? 8 : 4,
                CardExpedition.UltimateMedium       => specialDere ? 8 : 4,
                CardExpedition.UltimateHard         => specialDere ? 9 : 5,
                CardExpedition.UltimateHardcore     => specialDere ? 4 : 2,
                _ => 0
            };

            return (int) (itemsPerHour / 60 * length);
        }

        public double GetKarmaCostOfExpedition(double length, Card card, User user)
        {
            var karmaCostPerMinute = card.Expedition switch
            {
                CardExpedition.NormalItemWithExp    => user.GameDeck.IsGood() ? 0.025 : 0.00225,
                CardExpedition.ExtremeItemWithExp   => 0.0385,
                CardExpedition.LightItemWithExp     => 0.008,
                CardExpedition.LightItems           => 0.008,
                CardExpedition.LightExp             => 0.008,
                CardExpedition.DarkItemWithExp      => 0.0045,
                CardExpedition.DarkItems            => 0.0045,
                CardExpedition.DarkExp              => 0.0045,
                _ => 0
            };

            var dereMod = card.Dere switch
            {
                Dere.Kamidere => 0.7,
                Dere.Yandere  => 1.3,
                _ => 1
            };

            return karmaCostPerMinute * length * dereMod;
        }

        public double GetAffectionCostOfExpedition(double length, Card card)
        {
            var qualityMod = card.Quality switch
            {
                Quality.Omega   => 0.51,
                Quality.Sigma   => 0.55,
                Quality.Lambda  => 0.59,
                Quality.Jota    => 0.64,
                Quality.Theta   => 0.68,
                Quality.Zeta    => 0.71,
                Quality.Epsilon => 0.75,
                Quality.Delta   => 0.78,
                Quality.Gamma   => 0.82,
                Quality.Beta    => 0.88,
                Quality.Alpha   => 0.93,
                _ => 1
            };

            var dereUltMod = card.Dere switch
            {
                Dere.Yami   => 0.75,
                Dere.Raito  => 0.75,
                Dere.Yato   => 0.70,
                _ => 1
            };
            dereUltMod = qualityMod < 1 ? 0.6 : dereUltMod;

            var affectionCostPerMinute = card.Expedition switch
            {
                CardExpedition.NormalItemWithExp    => 0.02,
                CardExpedition.ExtremeItemWithExp   => 0.375,
                CardExpedition.LightExp             => 0.155,
                CardExpedition.DarkExp              => 0.155,
                CardExpedition.DarkItems            => 0.132 * qualityMod,
                CardExpedition.LightItems           => 0.132 * qualityMod,
                CardExpedition.LightItemWithExp     => 0.125,
                CardExpedition.DarkItemWithExp      => 0.125,
                CardExpedition.UltimateEasy         => 2 * dereUltMod * qualityMod,
                CardExpedition.UltimateMedium       => 2 * dereUltMod * qualityMod,
                CardExpedition.UltimateHard         => 4 * dereUltMod * qualityMod,
                CardExpedition.UltimateHardcore     => 1 * dereUltMod * qualityMod,
                _ => 0
            };

            var rarityMod = card.Rarity switch
            {
                Rarity.SSS => 0.88,
                Rarity.SS  => 0.97,
                Rarity.D   => 1.05,
                Rarity.E   => 1.1,
                _ => 1
            };

            var dereMod = card.Dere switch
            {
                Dere.Tsundere => 2,
                Dere.Dandere  => 1.15,
                Dere.Kamidere => 1.1,
                Dere.Yandere  => 1.1,
                _ => 1
            };

            return affectionCostPerMinute * length * rarityMod * dereMod;
        }

        public double GetMaxPossibleLengthOfExpedition(User user, Card card, CardExpedition expedition = CardExpedition.None)
        {
            expedition = expedition == CardExpedition.None ? card.Expedition : expedition;
            var costPerMinute = GetAffectionCostOfExpedition(1, card);
            var costOffset = user.GameDeck.IsNeutral() ? 23d : 6d;
            var karmaBonus = user.GameDeck.Karma / 200d;
            var fuel = card.Affection;

            switch (expedition)
            {
                case CardExpedition.NormalItemWithExp:
                    karmaBonus = -Math.Abs(karmaBonus);
                    break;

                case CardExpedition.ExtremeItemWithExp:
                    karmaBonus = 0;
                    break;

                case CardExpedition.LightExp:
                case CardExpedition.LightItems:
                case CardExpedition.LightItemWithExp:
                    karmaBonus = Math.Min(12, karmaBonus);
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
                    fuel *= ((int)card.Quality * 0.7) + 1.4;
                    costOffset = 0;
                    karmaBonus = 0;
                    break;

                default:
                    break;
            }

            costPerMinute *= card.HasImage() ? 1 : 4;
            fuel += costOffset + karmaBonus;
            var time = fuel / costPerMinute;

            time = time > 10080 ? 10080 : time;
            time = time < 0.1 ? 0.1 : time;
            return time;
        }

        public double GetGuaranteedAffection(User user, Card card, double affectionCost)
        {
            var affectionBaseReturn = card.Expedition switch
            {
                CardExpedition.NormalItemWithExp    => user.GameDeck.IsNeutral() ? 1 : 0.7,
                CardExpedition.ExtremeItemWithExp   => 1.18,
                CardExpedition.DarkItems            => 1.12,
                CardExpedition.LightItems           => 1.12,
                CardExpedition.LightItemWithExp     => 0.65,
                CardExpedition.DarkItemWithExp      => 0.65,
                _ => 0
            };

            var dereMod = card.Dere switch
            {
                Dere.Tsundere   => 0.5,
                Dere.Kamidere   => 0.9,
                Dere.Yandere    => 0.9,
                Dere.Bodere     => 0.75,
                Dere.Yami       => user.GameDeck.CanCreateDemon()  ? 1.2 : 1.1,
                Dere.Raito      => user.GameDeck.CanCreateAngel()  ? 1.2 : 1.1,
                Dere.Yato       => user.GameDeck.IsNeutral()       ? 1.3 : 1.2,
                _ => 1
            };

            return affectionBaseReturn * affectionCost * dereMod;
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
            if (card.Fatigue >= 1000)
                return false;

            if (card.Expedition != CardExpedition.None)
                return false;

            if (card.Curse == CardCurse.ExpeditionBlockade)
                return false;

            if (card.InCage || !card.CanFightOnPvEGMwK())
                return false;

            if (GetMaxPossibleLengthOfExpedition(user, card, expedition) <= 2)
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
                    return user.GameDeck.Karma > 400;

                case CardExpedition.DarkItems:
                    return user.GameDeck.Karma < -1000;
                case CardExpedition.DarkExp:
                    return (user.GameDeck.Karma < -1000) && !card.FromFigure;
                case CardExpedition.DarkItemWithExp:
                    return user.GameDeck.Karma < -400;

                default:
                case CardExpedition.None:
                    return false;
            }
        }
    }
}