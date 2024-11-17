#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Sanakan.Database;
using Sanakan.Database.Models;
using Sanakan.Services;
using Sanakan.Services.PocketWaifu;
using Sanakan.Services.Time;

namespace Sanakan.Extensions
{
    public static class CardExtension
    {
        private static Dictionary<string, StarStyle> _starStyleParsingDic = new Dictionary<string, StarStyle>
        {
            {"waz",     StarStyle.Snek},
            {"snek",    StarStyle.Snek},
            {"snake",   StarStyle.Snek},
            {"pig",     StarStyle.Pig},
            {"swinka",  StarStyle.Pig},
            {"white",   StarStyle.White},
            {"biala",   StarStyle.White},
            {"full",    StarStyle.Full},
            {"pelna",   StarStyle.Full},
            {"empty",   StarStyle.Empty},
            {"pusta",   StarStyle.Empty},
            {"black",   StarStyle.Black},
            {"czarna",  StarStyle.Black},
        };

        public static string GetString(this Card card, bool withoutId = false, bool withUpgrades = false,
            bool nameAsUrl = false, bool allowZero = false, bool showBaseHp = false, bool hideStats = false) => new StringBuilder()
                    .Append(withoutId ? "" : $"{card.GetIdWithUrl()} ")
                    .Append(nameAsUrl ? card.GetNameWithUrl() : card.Name)
                    .Append($" **{card.GetCardRealRarity()}** ")
                    .Append(hideStats ? "" : (card.GetCardParams(showBaseHp, allowZero)))
                    .Append((withUpgrades && !card.FromFigure) ? $"_(U:{card.UpgradesCnt})_" : "")
                    .ToString();

        public static string GetShortString(this Card card, bool nameAsUrl = false) =>
             $"{card.GetIdWithUrl()} {(nameAsUrl ? card.GetNameWithUrl() : card.Name)} **{card.GetCardRealRarity()}**";

        public static string GetCardRealRarity(this Card card) =>
            card.FromFigure ? card.Quality.ToName() : card.Rarity.ToString();

        public static string GetCardParams(this Card card, bool showBaseHp = false, bool allowZero = false, bool inNewLine = false)
        {
            string hp = showBaseHp ? $"**({card.Health})**{card.GetHealthWithPenalty(allowZero)}" : $"{card.GetHealthWithPenalty(allowZero)}";
            var param = new string[] { $"❤{hp}", $"🔥{card.GetAttackWithBonus()}", $"🛡{card.GetDefenceWithBonus()}" };
            return string.Join(inNewLine ? "\n" : " ", param);
        }

        public static string ToName(this CardCurse curse) => curse switch
        {
            CardCurse.BloodBlockade      => "blokada używania krwii",
            CardCurse.DereBlockade       => "blokada zmiany dere",
            CardCurse.ExpeditionBlockade => "blokada wypraw",
            CardCurse.InvertedItems      => "odwrócenie działania przedmiotów",
            CardCurse.LoweredExperience  => "obniżenie zdobywanego doświadczenia",
            CardCurse.LoweredStats       => "obniżone statystyki",
            CardCurse.FoodBlockade       => "blokada używania przedmiotów zwiększających relacje",
            _ => "brak"
        };

        public static string GetNameWithUrl(this Card card) => $"[{card.Name}]({card.GetCharacterUrl()})";

        public static string GetCharacterUrl(this Card card) => Shinden.API.Url.GetCharacterURL(card.Character);

        public static int GetValue(this Card card)
        {
            switch (card.Rarity)
            {
                case Rarity.SSS: return 50;
                case Rarity.SS: return 25;
                case Rarity.S: return 15;
                case Rarity.A: return 10;
                case Rarity.B: return 7;
                case Rarity.C: return 5;
                case Rarity.D: return 3;

                default:
                case Rarity.E: return 1;
            }
        }

        public static double GetMaxExpToChest(this Card card, ExpContainerLevel lvl)
        {
            double exp = 0;

            switch (card.Rarity)
            {
                case Rarity.SSS:
                    exp = 14d;
                    break;

                case Rarity.SS:
                    exp = 7d;
                    break;

                case Rarity.S:
                    exp = 5.2;
                    break;

                case Rarity.A:
                    exp = 3.9;
                    break;

                case Rarity.B:
                    exp = 3.1;
                    break;

                case Rarity.C:
                    exp = 2.3;
                    break;

                case Rarity.D:
                    exp = 1.5;
                    break;

                default:
                case Rarity.E:
                    exp = 1.2;
                    break;
            }

            switch (lvl)
            {
                case ExpContainerLevel.Level4:
                    exp *= 5d;
                    break;
                case ExpContainerLevel.Level3:
                    exp *= 2d;
                    break;
                case ExpContainerLevel.Level2:
                    exp *= 1.5;
                    break;

                default:
                case ExpContainerLevel.Level1:
                case ExpContainerLevel.Disabled:
                    break;
            }

            return exp;
        }

        public static bool HasImage(this Card card) => card.GetImage() != null;

        public static bool HasCustomBorder(this Card card) => card.CustomBorder != null;

        public static double CalculateCardPower(this Card card)
        {
            var cardPower = card.GetHealthWithPenalty() * 0.018;
            cardPower += card.GetAttackWithBonus() * 0.019;

            var normalizedDef = card.GetDefenceWithBonus();
            if (normalizedDef > 99)
            {
                normalizedDef = 99;
                if (card.FromFigure)
                {
                    cardPower += (card.GetDefenceWithBonus() - normalizedDef) * 0.019;
                }
            }

            cardPower += normalizedDef * 2.76;

            switch (card.Dere)
            {
                case Dere.Yami:
                case Dere.Raito:
                    cardPower += 20;
                    break;

                case Dere.Yato:
                    cardPower += 30;
                    break;

                case Dere.Tsundere:
                    cardPower -= 20;
                    break;

                default:
                    break;
            }

            if (cardPower < 1)
                cardPower = 1;

            card.CardPower = cardPower;

            return cardPower;
        }

        public static MarketValue GetThreeStateMarketValue(this Card card)
        {
            if (card.MarketValue < 0.3) return MarketValue.Low;
            if (card.MarketValue > 5.8) return MarketValue.High;
            return MarketValue.Normal;
        }

        public static string GetStatusIcons(this Card card, Services.PocketWaifu.TagHelper tags)
        {
            var icons = new List<string>();
            if (card.Active) icons.Add("☑️");
            if (card.Unique) icons.Add("💠");
            if (card.FromFigure) icons.Add("🎖️");
            if (!card.IsTradable) icons.Add("⛔");
            if (card.IsBroken()) icons.Add("💔");
            if (card.InCage) icons.Add("🔒");
            if (card.Curse != CardCurse.None) icons.Add("💀");
            if (card.Expedition != CardExpedition.None) icons.Add("✈️");
            if (!string.IsNullOrEmpty(card.CustomImage)) icons.Add("🖼️");
            if (!string.IsNullOrEmpty(card.CustomBorder)) icons.Add("✂️");

            var value = card.GetThreeStateMarketValue();
            if (value == MarketValue.Low) icons.Add("♻️");
            if (value == MarketValue.High) icons.Add("💰");

            if (!card.Tags.IsNullOrEmpty())
                icons.AddRange(tags.GetAllIcons(card));

            return string.Join(" ", icons);
        }

        public static string GetPocketUrl(this Card card) => card.Id == 0 ? "": $"https://waifu.sanakan.pl/#/card/{card.Id}";

        public static string GetIdWithUrl(this Card card) => card.Id == 0 ? "~~**[0]**~~": $"**[[{card.Id}](https://waifu.sanakan.pl/#/card/{card.Id})]**";

        public static string GetDescSmall(this Card card, Services.PocketWaifu.TagHelper tags, ISystemTime time)
        {
            var kcs = Fun.IsAF() ? Fun.GetRandomValue(0, 123) : card.WhoWantsCount;
            return $"{card.GetIdWithUrl()} *({card.Character}) KC: {kcs} PWR: {card.CalculateCardPower():F}*\n"
                + $"{card.GetString(true, true, true, false, true)}\n"
                + $"_{card.Title}_\n\n"
                + $"{card.Dere}\n"
                + $"{card.GetAffectionString()}\n"
                + $"{card.GetFatigueString(time)}\n"
                + $"{card.ExpCnt:F}/{card.ExpToUpgrade():F} exp\n\n"
                + $"{(card.Tags.IsNullOrEmpty() ? "---" : string.Join(" ", card.Tags.Select(x => x.Name)))}\n"
                + $"{card.GetStatusIcons(tags)}";
        }

        public static string GetDesc(this Card card, bool hideScalelInfo, Services.PocketWaifu.TagHelper tags, ISystemTime time)
        {
            var kcs = Fun.IsAF() ? Fun.GetRandomValue(0, 123) : card.WhoWantsCount;
            string scalpelInfo = (!string.IsNullOrEmpty(card.CustomImage) && !hideScalelInfo)
                ? $"**Ustawiono obrazek:** {card.CustomImageDate.ToShortDateTime()}\n**Animacja:** {card.IsAnimatedImage.GetYesNo()}\n" : "";

            return $"{card.GetNameWithUrl()} **{card.GetCardRealRarity()}**\n"
                + $"*{card.Title ?? "????"}*\n\n"
                + $"*{card.GetCardParams(true, false, true)}*\n\n"
                + $"**Relacja:** {card.GetAffectionString()}\n"
                + $"**Zmęczenie:** {card.GetFatigueString(time)}\n"
                + $"**Doświadczenie:** {card.ExpCnt:F}/{card.ExpToUpgrade():F}\n"
                + $"**Dostępne ulepszenia:** {card.UpgradesCnt}\n\n"
                + $"**W klatce:** {card.InCage.GetYesNo()}\n"
                + $"**Aktywna:** {card.Active.GetYesNo()}\n"
                + $"**Możliwość wymiany:** {card.IsTradable.GetYesNo()}\n\n"
                + $"**WID:** {card.GetIdWithUrl()} *({card.Character})*\n"
                + $"**Restarty:** {card.RestartCnt}\n"
                + $"**Pochodzenie:** {card.Source.GetString()}\n"
                + $"**Moc:** {card.CalculateCardPower():F}\n"
                + $"**Charakter:** {card.Dere}\n"
                + $"**Utworzona:** {card.CreationDate.ToShortDateTime()}\n{scalpelInfo}"
                + $"**KC:** {kcs}\n"
                + $"**Tagi:** {(card.Tags.IsNullOrEmpty() ? "---" : string.Join(" ", card.Tags.Select(x => x.Name)))}\n"
                + $"{card.GetStatusIcons(tags)}\n\n";
        }

        public static int GetHealthWithPenalty(this Card card, bool allowZero = false)
        {
            var percent = card.Affection * 5d / 100d;
            var maxHealth = card.FromFigure ? 99999 : 999;
            var bonusFromFood = Math.Min((int)(card.Health * percent), 2000);

            var newHealth = card.Health + bonusFromFood;
            newHealth += card.FromFigure ? card.HealthBonus : 0;

            newHealth = Math.Min(newHealth, maxHealth);
            return allowZero ? Math.Max(newHealth, 0) : Math.Max(newHealth, 10);
        }

        public static int GetCardStarType(this Card card)
        {
            var max = card.MaxStarType() - 1;
            var maxRestartsPerType = card.GetMaxStarsPerType() * card.GetRestartCntPerStar();
            var type = (card.RestartCnt - 1) / maxRestartsPerType;
            if (type > 0)
            {
                var ths = card.RestartCnt - (maxRestartsPerType + ((type - 1) * maxRestartsPerType));
                if (ths < card.GetRestartCntPerStar()) --type;
            }
            return Math.Min(type, max);
        }

        public static int GetMaxCardsRestartsOnStarType(this Card card)
        {
            return card.GetMaxStarsPerType() * card.GetRestartCntPerStar() * card.GetCardStarType();
        }

        public static int GetCardStarCount(this Card card)
        {
            var max = card.GetMaxStarsPerType();
            var starCnt = (card.RestartCnt - card.GetMaxCardsRestartsOnStarType()) / card.GetRestartCntPerStar();
            return Math.Min(starCnt, max);
        }

        public static int GetTotalCardStarCount(this Card card)
        {
            var max = card.GetMaxStarsPerType() * card.MaxStarType();
            var stars = card.RestartCnt / card.GetRestartCntPerStar();
            return Math.Min(stars, max);
        }

        public static int MaxStarType(this Card _) => 10;

        public static int GetRestartCntPerStar(this Card _) => 2;

        public static int GetMaxStarsPerType(this Card _) => 5;

        public static int GetAttackWithBonus(this Card card)
        {
            var maxAttack = card.FromFigure ? 9999 : 999;
            var newAttack = card.Attack + (card.RestartCnt * 4) + (card.GetTotalCardStarCount() * 20);
            newAttack += card.FromFigure ? card.AttackBonus : card.AttackBonus / 3;

            if (card.Curse == CardCurse.LoweredStats)
            {
                newAttack -= newAttack * 5 / 10;
            }

            return Math.Min(newAttack, maxAttack);
        }

        public static int GetDefenceWithBonus(this Card card)
        {
            var maxDefence = card.FromFigure ? 9999 : 99;
            var newDefence = card.Defence + (card.RestartCnt * 2) + (card.GetTotalCardStarCount() * 5);
            newDefence += card.FromFigure ? card.DefenceBonus : 0;

            if (card.Curse == CardCurse.LoweredStats)
            {
                newDefence -= newDefence * 5 / 10;
            }

            return Math.Min(newDefence, maxDefence);
        }

        public static string GetString(this CardSource source)
        {
            switch (source)
            {
                case CardSource.Activity: return "Aktywność";
                case CardSource.Safari: return "Safari";
                case CardSource.Shop: return "Sklepik";
                case CardSource.GodIntervention: return "Czity";
                case CardSource.Tinkering: return "Druciarstwo";
                case CardSource.Api: return "Strona";
                case CardSource.Migration: return "Stara baza";
                case CardSource.PvE: return "Walki na boty";
                case CardSource.Daily: return "Karta+";
                case CardSource.Crafting: return "Tworzenie";
                case CardSource.PvpShop: return "Koszary";
                case CardSource.Figure: return "Figurka";
                case CardSource.Expedition: return "Wyprawa";
                case CardSource.ActivityShop: return "Kiosk";
                case CardSource.Lottery: return "Loteria";

                default:
                case CardSource.Other: return "Inne";
            }
        }

        public static string GetYesNo(this bool b) => b ? "Tak" : "Nie";

        public static bool CanFightOnPvEGMwK(this Card card) => card.Affection > -80;

        public static bool CanGiveRing(this Card card) => card.Affection >= 5;

        public static bool CanGiveBloodOrUpgradeToSSS(this Card card) => card.Affection >= 50;

        public static bool IsBroken(this Card card) => card.Affection <= -50;

        public static bool IsUnusable(this Card card) => card.Affection <= -5;

        public static string GetAffectionString(this Card card)
        {
            if (card.Affection <= -2000) return "Pogarda (Ω)";
            if (card.Affection <= -800) return "Pogarda (Δ)";
            if (card.Affection <= -400) return "Pogarda (γ)";
            if (card.Affection <= -200) return "Pogarda (β)";
            if (card.Affection <= -100) return "Pogarda (α)";
            if (card.Affection <= -50) return "Pogarda";
            if (card.Affection <= -5) return "Nienawiść";
            if (card.Affection <= -4) return "Zawiść";
            if (card.Affection <= -3) return "Wrogość";
            if (card.Affection <= -2) return "Złośliwość";
            if (card.Affection <= -1) return "Chłodność";
            if (card.Affection >= 2000) return "Obsesyjna miłość (Ω)";
            if (card.Affection >= 800) return "Obsesyjna miłość (Δ)";
            if (card.Affection >= 400) return "Obsesyjna miłość (γ)";
            if (card.Affection >= 200) return "Obsesyjna miłość (β)";
            if (card.Affection >= 100) return "Obsesyjna miłość (α)";
            if (card.Affection >= 50) return "Obsesyjna miłość";
            if (card.Affection >= 5) return "Miłość";
            if (card.Affection >= 4) return "Zauroczenie";
            if (card.Affection >= 3) return "Przyjaźń";
            if (card.Affection >= 2) return "Fascynacja";
            if (card.Affection >= 1) return "Zaciekawienie";
            return "Obojętność";
        }

        public static bool IsBlockadeFromFatigue(this Card card) => card.Fatigue >= Waifu.FatigueThirdPhase;

        public static bool RecoverFatigue(this Card card, ISystemTime time)
        {
            var newFatigue = card.GetCurrentFatigue(time);
            if (time != null && newFatigue != card.Fatigue)
            {
                card.ExpeditionEndDate = time.Now();
                card.Fatigue = newFatigue;
                return true;
            }
            return false;
        }

        public static double GetCurrentFatigue(this Card card, ISystemTime time)
        {
            if (time != null && card.Fatigue > 0)
            {
                var breakFromExpedition = (time.Now() - card.ExpeditionEndDate).TotalMinutes;
                if (breakFromExpedition > 1)
                {
                    var toRecover = Math.Min(Waifu.FatigueRecoveryRate * breakFromExpedition, Waifu.FatigueThirdPhase);
                    return Math.Max(card.Fatigue - toRecover, 0);
                }
            }
            return card.Fatigue;
        }

        public static string GetFatigueString(this Card card, ISystemTime time)
        {
            var fatigue = card.GetCurrentFatigue(time);
            if (fatigue >= 1500) return "Stan agonalny";
            if (fatigue >= 1000) return "Przepracowanie";
            if (fatigue >= 800) return "Wysokie";
            if (fatigue >= 600) return "Średnie";
            if (fatigue >= 400) return "Lekkie";
            return "Brak";
        }

        public static string GetName(this CardExpedition expedition, string end = "a")
        {
            switch (expedition)
            {
                case CardExpedition.NormalItemWithExp:
                    return $"normaln{end}";

                case CardExpedition.ExtremeItemWithExp:
                    return $"niemożliw{end}";

                case CardExpedition.DarkExp:
                case CardExpedition.DarkItems:
                case CardExpedition.DarkItemWithExp:
                    return $"nikczemn{end}";

                case CardExpedition.LightExp:
                case CardExpedition.LightItems:
                case CardExpedition.LightItemWithExp:
                    return $"heroiczn{end}";

                case CardExpedition.UltimateEasy:
                    return $"niezwykł{end} (E)";
                case CardExpedition.UltimateMedium:
                    return $"niezwykł{end} (M)";
                case CardExpedition.UltimateHard:
                    return $"niezwykł{end} (H)";
                case CardExpedition.UltimateHardcore:
                    return $"niezwykł{end} (HH)";

                default:
                case CardExpedition.None:
                    return "-";
            }
        }

        public static double ExpToUpgrade(this Rarity r, bool fromFigure = false, Quality q = Quality.Broken)
        {
            switch (r)
            {
                case Rarity.SSS:
                    if (fromFigure)
                    {
                        return 1000 + (120 * (int)q);
                    }
                    return 1000;
                case Rarity.SS:
                    return 100;

                default:
                    return 30 + (4 * (7 - (int)r));
            }
        }

        public static double ExpToUpgrade(this Card card)
        {
            return card.Rarity.ExpToUpgrade(card.FromFigure, card.Quality);
        }

        public static int GetAttackMin(this Rarity rarity)
        {
            switch (rarity)
            {
                case Rarity.SSS: return 100;
                case Rarity.SS: return 90;
                case Rarity.S: return 80;
                case Rarity.A: return 65;
                case Rarity.B: return 50;
                case Rarity.C: return 32;
                case Rarity.D: return 20;

                case Rarity.E:
                default: return 1;
            }
        }

        public static int GetDefenceMin(this Rarity rarity)
        {
            switch (rarity)
            {
                case Rarity.SSS: return 88;
                case Rarity.SS: return 77;
                case Rarity.S: return 68;
                case Rarity.A: return 60;
                case Rarity.B: return 50;
                case Rarity.C: return 32;
                case Rarity.D: return 15;

                case Rarity.E:
                default: return 1;
            }
        }

        public static int GetHealthMin(this Rarity rarity)
        {
            switch (rarity)
            {
                case Rarity.SSS: return 100;
                case Rarity.SS: return 90;
                case Rarity.S: return 80;
                case Rarity.A: return 70;
                case Rarity.B: return 60;
                case Rarity.C: return 50;
                case Rarity.D: return 40;

                case Rarity.E:
                default: return 30;
            }
        }

        public static int GetHealthMax(this Card card)
        {
            return 300 - (card.Attack + card.Defence);
        }

        public static int GetAttackMax(this Rarity rarity)
        {
            switch (rarity)
            {
                case Rarity.SSS: return 130;
                case Rarity.SS: return 100;
                case Rarity.S: return 96;
                case Rarity.A: return 87;
                case Rarity.B: return 84;
                case Rarity.C: return 68;
                case Rarity.D: return 50;

                case Rarity.E:
                default: return 35;
            }
        }

        public static int GetDefenceMax(this Rarity rarity)
        {
            switch (rarity)
            {
                case Rarity.SSS: return 96;
                case Rarity.SS: return 91;
                case Rarity.S: return 79;
                case Rarity.A: return 75;
                case Rarity.B: return 70;
                case Rarity.C: return 65;
                case Rarity.D: return 53;

                case Rarity.E:
                default: return 38;
            }
        }

        public static void DecAffectionOnExpeditionBy(this Card card, double value)
        {
            card.Affection -= value;

            switch (card.Expedition)
            {
                case CardExpedition.UltimateEasy:
                {
                    if (card.Affection < -10)
                        card.Affection = -10;
                }
                break;

                case CardExpedition.UltimateMedium:
                {
                    if (card.Affection < -100)
                        card.Affection = -100;
                }
                break;

                default:
                break;
            }
        }

        public static void IncAttackBy(this Card card, int value)
        {
            if (card.FromFigure)
            {
                card.AttackBonus += value;
            }
            else
            {
                var max = card.Rarity.GetAttackMax();
                card.Attack += value;

                if (card.Attack > max)
                    card.Attack = max;
            }
        }

        public static void DecAttackBy(this Card card, int value)
        {
            if (card.FromFigure)
            {
                card.AttackBonus -= value;
            }
            else
            {
                var min = card.Rarity.GetAttackMin();
                card.Attack -= value;

                if (card.Attack < min)
                    card.Attack = min;
            }
        }

        public static void IncDefenceBy(this Card card, int value)
        {
            if (card.FromFigure)
            {
                card.DefenceBonus += value;
            }
            else
            {
                var max = card.Rarity.GetDefenceMax();
                card.Defence += value;

                if (card.Defence > max)
                    card.Defence = max;
            }
        }

        public static void DecDefenceBy(this Card card, int value)
        {
            if (card.FromFigure)
            {
                card.DefenceBonus -= value;
            }
            else
            {
                var min = card.Rarity.GetDefenceMin();
                card.Defence -= value;

                if (card.Defence < min)
                    card.Defence = min;
            }
        }

        public static string GetImage(this Card card) => card.CustomImage ?? card.Image;

        public static async Task Update(this Card card, IUser user, Shinden.ShindenClient client, bool updateTitle = false)
        {
            var response = await client.GetCharacterInfoAsync(card.Character);
            if (!response.IsSuccessStatusCode())
            {
                if (response.Code == System.Net.HttpStatusCode.NotFound)
                    card.Unique = true;

                throw new Exception($"Couldn't get card info!");
            }

            if (user != null)
            {
                if (card.FirstIdOwner == 0)
                    card.FirstIdOwner = user.Id;
            }

            card.Unique = false;
            card.CalculateCardPower();
            card.Name = response.Body.ToString();
            card.Image = response.Body.HasImage ? response.Body.PictureUrl : null;
            if (updateTitle)
            {
                card.Title = response.Body?.Relations?.OrderBy(x => x.Id).FirstOrDefault()?.Title ?? "????";
            }
        }

        public static bool TryParse(this StarStyle star, string s, out StarStyle type)
            => _starStyleParsingDic.TryGetValue(s.RemoveDiacritics().ToLower(), out type);

        public static StarStyle Parse(this StarStyle star, string s)
            => star.TryParse(s, out var type) ? type : throw new Exception("Could't parse input!");

        public static ExecutionResult CanUpgradePower(this Card card, int by = 1)
        {
            if (!card.FromFigure)
            {
                return ExecutionResult.FromError("ten przedmiot można użyć tylko na karcie ultimate.");
            }

            var currParams = card.AttackBonus + card.HealthBonus + card.DefenceBonus;
            var maxParams = 4900 * (int)card.Quality;
            if (currParams + by >= maxParams)
            {
                return ExecutionResult.FromError("nie można już bardziej zwiekszyć parametrów na tej karcie.");
            }

            return ExecutionResult.FromSuccess("");
        }

        public static Api.Models.CardFinalView ToViewUser(this Card c, string name, ulong shinden = 0, ISystemTime time = null)
            => Api.Models.CardFinalView.ConvertFromRawWithUserInfo(c, name, shinden, time);

        public static Api.Models.CardFinalView ToView(this Card c, ulong shindenId = 0, ISystemTime time = null)
            => Api.Models.CardFinalView.ConvertFromRaw(c, shindenId, time);

        public static Api.Models.ExpeditionCard ToExpeditionView(this Card card, User user, Expedition helper)
            => Api.Models.ExpeditionCard.ConvertFromRaw(user, card, helper);

        public static List<Api.Models.ExpeditionCard> ToExpeditionView(this IEnumerable<Card> clist, User user, Expedition helper)
        {
            var list = new List<Api.Models.ExpeditionCard>();
            foreach (var c in clist) list.Add(c.ToExpeditionView(user, helper));
            return list;
        }

        public static List<Api.Models.CardFinalView> ToView(this IEnumerable<Card> clist, ulong shindenId = 0, ISystemTime time = null)
        {
            var list = new List<Api.Models.CardFinalView>();
            foreach (var c in clist) list.Add(c.ToView(shindenId, time));
            return list;
        }

        public static string ToHeartWishlist(this Card card, bool isOnUserWishlist = false)
        {
            if (Fun.IsAF() && Fun.TakeATry(75d))
                return $"💗 ({Fun.GetRandomValue(1, 73)}) ";

            if (isOnUserWishlist) return "💚 ";
            if (card.WhoWantsCount < 1) return "🤍 ";
            return $"💗 ({card.WhoWantsCount}) ";
        }

        public static bool AddActivityFromNewCard(this Database.DatabaseContext db, Card card, bool isOnUserWishlist, ISystemTime time, User user, string username)
        {
            if (isOnUserWishlist || card.WhoWantsCount > 1)
            {
                db.UserActivities.Add(new Services.UserActivityBuilder(time)
                    .WithUser(user, username).WithCard(card)
                    .WithType(isOnUserWishlist ? Database.Models.ActivityType.AcquiredCardWishlist :
                        (card.WhoWantsCount >= 30 ? Database.Models.ActivityType.AcquiredCardHighKC:
                        Database.Models.ActivityType.AcquiredCardKC)).Build());
                return true;
            }
            return false;
        }

        public static bool DestroyOrRelease(this Card card, User user, bool release, double crueltyBonus = 0)
        {
            if (card.CustomImage != null && Dir.IsLocal(card.CustomImage) && File.Exists(card.CustomImage))
                File.Delete(card.CustomImage);

            if (card.CustomBorder != null && Dir.IsLocal(card.CustomBorder) && File.Exists(card.CustomBorder))
                File.Delete(card.CustomBorder);

            if (release)
            {
                return card.ReleaseCard(user, crueltyBonus);
            }
            return card.DestroyCard(user, crueltyBonus);
        }

        public static bool DestroyCard(this Card card, User user, double crueltyBonus = 0)
        {
            var chLvl = user.GameDeck.ExpContainer.Level;
            user.StoreExpIfPossible((card.ExpCnt > card.GetMaxExpToChest(chLvl))
                ? card.GetMaxExpToChest(chLvl)
                : card.ExpCnt);

            user.GameDeck.Karma -= user.GameDeck.CanCreateDemon() ? (0.75 + crueltyBonus) : (0.91 + crueltyBonus);
            user.Stats.DestroyedCards += 1;

            if (card.MarketValue >= 0.05)
            {
                var max = card.GetValue();
                user.GameDeck.CTCnt += Math.Max(Math.Min(max, (int)(max * card.MarketValue)), 1);
                return true;
            }
            return false;
        }

        public static bool ReleaseCard(this Card card, User user, double crueltyBonus = 0)
        {
            var chLvl = user.GameDeck.ExpContainer.Level;
            user.StoreExpIfPossible(((card.ExpCnt / 2) > card.GetMaxExpToChest(chLvl))
                ? card.GetMaxExpToChest(chLvl)
                : (card.ExpCnt / 2));

            user.GameDeck.Karma += user.GameDeck.CanCreateAngel() ? (0.75 - crueltyBonus) : (0.91 - crueltyBonus);
            user.Stats.ReleasedCards += 1;

            if (card.MarketValue >= 0.05 && crueltyBonus > 0 && Fun.TakeATry(50d))
            {
                var max = card.GetValue();
                user.GameDeck.CTCnt += Math.Max(Math.Min(max, (int)(max * card.MarketValue)), 1);
                return true;
            }
            return false;
        }

        public static void CalculateMarketValue(this Card card, double sourceCnt, double targetCnt)
        {
            card.MarketValue *= targetCnt / sourceCnt;
            if (double.IsInfinity(card.MarketValue))
                card.MarketValue = 0.001;

            card.MarketValue = Math.Max(Math.Min(card.MarketValue, 10), 0.001);
        }

        public static async Task ExchangeWithAsync(this Card card, (User user, int count, Tag tag, string username)
            source, (User user, int count, Tag tag, string username) target, DatabaseContext db, ISystemTime time)
        {
            _ = card.RecoverFatigue(time);
            if (card.IsDisallowedToExchange())
                return;

            if (card.Dere == Dere.Yami && target.user.GameDeck.IsGood())
                return;

            if (card.Dere == Dere.Raito && target.user.GameDeck.IsEvil())
                return;

            card.Active = false;
            card.Tags.Clear();
            card.Affection = card.Affection > 0 ? -5 : (card.Affection - 1.5);
            card.ExpeditionEndDate = time.Now();
            card.Fatigue += 321;

            if (card.ExpCnt > 1)
                card.ExpCnt *= 0.3;

            card.CalculateMarketValue(source.count, target.count);

            if (card.FirstIdOwner == 0)
                card.FirstIdOwner = source.user.Id;

            if (card.FromFigure)
            {
                card.IsTradable = false;

                await db.UserActivities.AddAsync(new UserActivityBuilder(time).WithUser(target.user, target.username)
                    .WithCard(card).WithType(Database.Models.ActivityType.AcquiredCarcUltimate).Build());
            }
            else if (card.Rarity == Rarity.SSS)
            {
                await db.UserActivities.AddAsync(new UserActivityBuilder(time).WithUser(target.user, target.username)
                    .WithCard(card).WithType(Database.Models.ActivityType.AcquiredCardSSS).Build());
            }

            source.user.GameDeck.RemoveFromWaifu(card);

            if (target.tag != null)
                card.Tags.Add(target.tag);

            card.GameDeckId = target.user.GameDeck.Id;

            var isOnUserWishlist = target.user.GameDeck.RemoveCardFromWishList(card.Id)
                || await target.user.GameDeck.RemoveCharacterFromWishListAsync(card.Character, db);

            db.AddActivityFromNewCard(card, isOnUserWishlist, time, target.user, target.username);
        }

        public static bool IsProtectedFromDiscarding(this Card card, TagHelper helper) => card is null
            || card.Active
            || card.InCage
            || helper.HasTag(card, Services.PocketWaifu.TagType.Favorite)
            || card.FromFigure
            || card.Curse != CardCurse.None
            || card.IsBlockadeFromFatigue()
            || card.Expedition != CardExpedition.None;

        public static bool IsDisallowedToExchange(this Card card) => card is null
            || card.Active
            || card.InCage
            || !card.IsTradable
            || card.Dere == Dere.Yato
            || card.Curse != CardCurse.None
            || card.Expedition != CardExpedition.None
            || (card.FromFigure && card.PAS != PreAssembledFigure.None)
            || card.IsBlockadeFromFatigue()
            || card.IsBroken();
    }
}
