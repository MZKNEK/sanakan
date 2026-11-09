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
using Sanakan.Services.PocketWaifu.Fight;
using Sanakan.Services.Time;

namespace Sanakan.Extensions
{
    public static class CardExtension
    {
        public static readonly bool _UseAlterUrl = true;

        private static Dictionary<string, StarStyle> _starStyleParsingDic = new Dictionary<string, StarStyle>
        {
            {"full",    StarStyle.Full},
            {"pelna",   StarStyle.Full},
            {"empty",   StarStyle.Empty},
            {"pusta",   StarStyle.Empty},
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
            card.FromFigure ? $"{card.Quality.ToName()}{card.GetOverflow()}" : card.Rarity.ToString();

        public static string GetCardParams(this Card card, bool showBaseHp = false, bool allowZero = false, bool inNewLine = false)
        {
            string hp = showBaseHp ? $"**({card.Health})**{card.GetHealthWithPenalty(allowZero)}" : $"{card.GetHealthWithPenalty(allowZero)}";
            var param = new string[] { $"❤{hp}", $"🔥{card.GetAttackWithBonus()}", $"🛡{card.GetDefenceWithBonus()}" };
            return string.Join(inNewLine ? "\n" : " ", param);
        }

        public static string ToName(this CardCurse curse) => curse switch
        {
            CardCurse.BloodBlockade      => "blokada używania krwi",
            CardCurse.DereBlockade       => "blokada zmiany dere",
            CardCurse.ExpeditionBlockade => "blokada wypraw",
            CardCurse.InvertedItems      => "odwrócenie działania przedmiotów",
            CardCurse.LoweredExperience  => "obniżenie zdobywanego doświadczenia",
            CardCurse.LoweredStats       => "obniżone statystyki",
            CardCurse.FoodBlockade       => "blokada używania przedmiotów zwiększających relacje",
            _ => "brak"
        };

        public static string GetNameWithUrl(this Card card) => $"[{card.Name}]({card.GetCharacterUrl()})";

        public static string GetCharacterUrl(this Card card) => ShindenCompat.Url.GetCharacterURL(card.Character);

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

        // duels pair decks of similar power, so a card is measured against opponents of its own kind:
        // normal cards from a fresh SSS to a maxed one, ultimates from quality α to fully pilled ones
        private static readonly (int Health, int Attack, int Defence)[] _normalReferences =
        {
            (100, 115, 92), (137, 123, 96), (225, 211, 99), (475, 403, 99), (850, 691, 99), (999, 999, 99),
        };

        private static readonly (int Health, int Attack, int Defence)[] _ultimateReferences =
        {
            (4000, 1200, 600), (12000, 2500, 1300), (28000, 4500, 2500), (3080, 3204, 1814), (4060, 5409, 3529), (6020, 9819, 6959),
            (12600, 9999, 9999), (27300, 9999, 9999), (42000, 9999, 9999), (71400, 9999, 9999), (99999, 9999, 9999),
        };

        // how much a dere is worth at equal stats, with its relations and ability: simulated duels were tuned until every dere
        // won half of its fights against random cards of the same power (Yami, Raito and Yato met four times less often);
        // the duel outcome depends on the exact pair of deres, so an average of the relation table misjudged them
        private static readonly double[] _normalDereFactor = { 0.732, 0.975, 1.061, 1.017, 0.996, 0.956, 0.977, 1.019, 1.332, 1.351, 1.581 };
        private static readonly double[] _ultimateDereFactor = { 0.793, 1.004, 1.025, 1.014, 0.979, 0.967, 1.007, 1.003, 1.213, 1.237, 1.346 };

        // a maxed normal SSS card of an average dere keeps the power it had with the old formula, in both scales
        private static readonly double _normalPowerScale = 310 / RawCardPower(999, 999, 99, false, 0);
        private static readonly double _ultimatePowerScale = 310 / RawCardPower(999, 999, 99, true, 0);

        public static double GetCritChance(this Card card) => card.CritChance + (card.Dere == Dere.Kuudere ? DereAbility.KuudereCritBonus : 0);

        private static double RawCardPower(double health, double attack, double defence, bool ultimate, double critChance)
        {
            double dealt = 0, taken = 0;
            var references = ultimate ? _ultimateReferences : _normalReferences;
            foreach (var r in references)
            {
                var hit = (1 - critChance) * Waifu.GetBaseDamage(attack, r.Defence, ultimate)
                    + critChance * Waifu.GetBaseDamage(attack, r.Defence, ultimate, true);
                dealt += hit / r.Health;
                taken += Waifu.GetBaseDamage(r.Attack, defence, ultimate);
            }

            dealt /= references.Length;
            taken /= references.Length;
            return Math.Sqrt(dealt * health / taken);
        }

        // how fast the card kills typical opponents times how long it survives them, so deck power sums compare decks fairly
        public static double CalculateCardPower(this Card card)
        {
            var dere = (int)card.Dere;
            double attack = card.GetAttackWithBonus(), defence = card.GetDefenceWithBonus();
            // the same penalty a card without a picture gets in a duel
            if (!card.HasImage())
            {
                attack -= attack * 20 / 100;
                defence -= defence * 20 / 100;
            }

            var cardPower = card.FromFigure
                ? _ultimatePowerScale * _ultimateDereFactor[dere] * RawCardPower(card.GetHealthWithPenalty(), attack, defence, true, card.GetCritChance() / 100)
                : _normalPowerScale * _normalDereFactor[dere] * RawCardPower(card.GetHealthWithPenalty(), attack, defence, false, card.GetCritChance() / 100);

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

        public static bool UsesSigmaFallback(this Card card)
        {
            if (card.Quality != Quality.Sigma)
                return false;

            var dir = Dir.GetResource($"PW/CG/{Quality.Sigma}");
            return !Directory.Exists(dir) || !Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Any();
        }

        public static Quality GetGraphicsQuality(this Card card) => card.UsesSigmaFallback() ? Quality.Delta : card.Quality;

        public static int GetCardVariantsCount(this Card card)
        {
            return card.Quality switch
            {
                Quality.Lambda => 1,
                Quality.Delta  => 8,
                Quality.Sigma  => 8,
                Quality.Eta    => 18,
                _ => 0
            };
        }

        public static string GetCardVariantString(this Card card)
        {
            if (card.GetCardVariantsCount() < 1 || card.BorderVariant < 1)
                return string.Empty;

            return card.Quality switch
            {
                Quality.Delta    => $"{card.BorderVariant}",
                Quality.Sigma    => $"{card.BorderVariant}",
                Quality.Eta      => $"{card.BorderVariant}",
                Quality.Lambda   => $"{card.BorderVariant}",
                _ => string.Empty
            };
        }

        public static string GetCardDereVariantString(this Card card)
        {
            if (card.GetCardVariantsCount() < 1 || card.BorderVariant < 1)
                return string.Empty;

            return card.Quality switch
            {
                Quality.Lambda => $"{card.BorderVariant}",
                _ => string.Empty
            };
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

        public static string GetPocketUrl(this Card card) => _UseAlterUrl ? card.GetPocketAlterUrl() : card.GetPocketWaifuUrl();
        public static string GetPocketWaifuUrl(this Card card) => card.Id == 0 ? "": $"https://waifu.sanakan.pl/#/card/{card.Id}";
        public static string GetPocketAlterUrl(this Card card) => card.Id == 0 ? "": $"https://alter.sanakan.pl/card/{card.Id}";

        public static string GetIdWithUrl(this Card card) => _UseAlterUrl ? card.GetIdWithAlterUrl() : card.GetIdWithWaifuUrl();
        public static string GetIdWithWaifuUrl(this Card card) => card.Id == 0 ? "~~**[0]**~~": $"**[[{card.Id}](https://waifu.sanakan.pl/#/card/{card.Id})]**";
        public static string GetIdWithAlterUrl(this Card card) => card.Id == 0 ? "~~**[0]**~~": $"**[[{card.Id}](https://alter.sanakan.pl/card/{card.Id})]**";

        private static string GetTagsLine(this Card card, Services.PocketWaifu.TagHelper tags)
        {
            var line = $"{(card.Tags.IsNullOrEmpty() ? "" : string.Join(" ", card.Tags.Select(x => x.Name)))} {card.GetStatusIcons(tags)}".Trim();
            return line.Length == 0 ? "brak" : line;
        }

        private static string GetWhoWantsLine(this Card card)
        {
            var kcs = Fun.IsAF() ? Fun.GetAFKC(card.Character) : card.WhoWantsCount;
            return $"KC: {kcs} ({card.AWhoWantsCount} aktywnych)";
        }

        public static string GetHealthLine(this Card card)
        {
            var health = card.GetHealthWithPenalty();
            return health == card.Health ? $"{health}" : $"{health} (bazowo {card.Health})";
        }

        public static EmbedBuilder GetView(this Card card, bool hideScalpelInfo, Services.PocketWaifu.TagHelper tags, ISystemTime time)
        {
            var more = new List<string>
            {
                $"{(card.Active ? "Aktywna" : "Nieaktywna")} · {(card.InCage ? "w klatce" : "poza klatką")} · {(card.IsTradable ? "można wymieniać" : "nie można wymieniać")}",
                $"Numer: {card.GetIdWithUrl()} · postać {card.Character} · restarty: {card.RestartCnt} · ulepszenia: {card.UpgradesCnt}",
                card.GetWhoWantsLine(),
                $"Zdobyta: {card.Source.GetString()}, {card.CreationDate.ToShortDateTime()}",
            };
            var ability = card.Dere.GetAbilityDescription();
            if (ability != null)
                more.Insert(0, $"Zdolność ({card.Dere}): {ability}");
            if (!string.IsNullOrEmpty(card.CustomImage) && !hideScalpelInfo)
                more.Add($"Obrazek ustawiony {card.CustomImageDate.ToShortDateTime()} · animacja: {card.IsCardAnimated().GetYesNo()}");
            more.Add($"Tagi: {card.GetTagsLine(tags)}");

            return new EmbedBuilder
            {
                Title = $"{card.Name} · {card.GetCardRealRarity()}".TrimToLength(EmbedBuilder.MaxTitleLength),
                Url = card.GetCharacterUrl(),
                Description = $"_{card.Title ?? "????"}_",
            }
            .AddField("❤ Życie", card.GetHealthLine(), true)
            .AddField("🔥 Atak", $"{card.GetAttackWithBonus()}", true)
            .AddField("🛡 Obrona", $"{card.GetDefenceWithBonus()}", true)
            .AddField("💞 Relacja", card.GetAffectionString(), true)
            .AddField("🎭 Charakter", card.Dere.ToString(), true)
            .AddField("😴 Zmęczenie", card.GetFatigueString(time), true)
            .AddField("📈 Doświadczenie", $"{card.ExpCnt:0.##} / {card.ExpToUpgrade():0.##}", true)
            .AddField("⚡ Moc", $"{card.CalculateCardPower():0}", true)
            .AddField("🎯 Precyzja", $"{card.GetCritChance():0.#}%", true)
            .AddField("Więcej", string.Join("\n", more).TrimToLength(EmbedFieldBuilder.MaxFieldValueLength));
        }

        public static string GetDescSmall(this Card card, Services.PocketWaifu.TagHelper tags, ISystemTime time)
        {
            var kcs = Fun.IsAF() ? Fun.GetAFKC(card.Character) : card.WhoWantsCount;
            return $"{card.GetIdWithUrl()} *({card.Character}) KC: {kcs}({card.AWhoWantsCount}) PWR: {card.CalculateCardPower():F}*\n"
                + $"{card.GetString(true, true, true, false, true)}\n"
                + $"_{card.Title}_\n\n"
                + $"{card.Dere}\n"
                + $"{card.GetAffectionString()}\n"
                + $"{card.GetFatigueString(time)}\n"
                + $"{card.ExpCnt:F}/{card.ExpToUpgrade():F} exp\n"
                + $"{card.GetCritChance():0.#}% precyzji\n\n"
                + $"{(card.Tags.IsNullOrEmpty() ? "---" : string.Join(" ", card.Tags.Select(x => x.Name)))}\n"
                + $"{card.GetStatusIcons(tags)}";
        }

        public static string GetDesc(this Card card, bool hideScalelInfo, Services.PocketWaifu.TagHelper tags, ISystemTime time)
        {
            var kcs = Fun.IsAF() ? Fun.GetAFKC(card.Character) : card.WhoWantsCount;
            string scalpelInfo = (!string.IsNullOrEmpty(card.CustomImage) && !hideScalelInfo)
                ? $"**Ustawiono obrazek:** {card.CustomImageDate.ToShortDateTime()}\n**Animacja:** {card.IsCardAnimated().GetYesNo()}\n" : "";

            return $"{card.GetNameWithUrl()} **{card.GetCardRealRarity()}**\n"
                + $"*{card.Title ?? "????"}*\n\n"
                + $"*{card.GetCardParams(true, false, true)}*\n\n"
                + $"**Relacja:** {card.GetAffectionString()}\n"
                + $"**Zmęczenie:** {card.GetFatigueString(time)}\n"
                + $"**Doświadczenie:** {card.ExpCnt:F}/{card.ExpToUpgrade():F}\n"
                + $"**Dostępne ulepszenia:** {card.UpgradesCnt}\n"
                + $"**Precyzja:** {card.GetCritChance():0.#}%\n\n"
                + $"**W klatce:** {card.InCage.GetYesNo()}\n"
                + $"**Aktywna:** {card.Active.GetYesNo()}\n"
                + $"**Możliwość wymiany:** {card.IsTradable.GetYesNo()}\n\n"
                + $"**WID:** {card.GetIdWithUrl()} *({card.Character})*\n"
                + $"**Restarty:** {card.RestartCnt}\n"
                + $"**Pochodzenie:** {card.Source.GetString()}\n"
                + $"**Moc:** {card.CalculateCardPower():F}\n"
                + $"**Charakter:** {card.Dere}\n"
                + $"**Utworzona:** {card.CreationDate.ToShortDateTime()}\n{scalpelInfo}"
                + $"**KC:** {kcs} ({card.AWhoWantsCount})\n"
                + $"**Tagi:** {(card.Tags.IsNullOrEmpty() ? "---" : string.Join(" ", card.Tags.Select(x => x.Name)))}\n"
                + $"{card.GetStatusIcons(tags)}\n\n";
        }

        public static int GetHealthWithPenalty(this Card card, bool allowZero = false)
        {
            var percent = card.Affection * 5d / 100d;
            var maxHealth = card.FromFigure ? MaxUltimateHealth : 999;
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

        public static int MaxStarType(this Card _) => 61;

        public static int GetRestartCntPerStar(this Card _) => 3;

        public static int GetMaxStarsPerType(this Card _) => 6;

        public static int GetAttackFromStars(this Card card)
        {
            var basePoints = Math.Min(card.RestartCnt, 100);
            var bonusPoints = Math.Max(0, card.RestartCnt - 100);

            var totalStars = card.GetTotalCardStarCount();
            var baseStars = Math.Min(totalStars, 50);
            var bonusStars = Math.Max(0, totalStars - 50);

            return (basePoints * 4) + bonusPoints + (baseStars * 20) + (bonusStars * 4);
        }

        public static int GetDefenceFromStars(this Card card)
        {
            var basePoints = Math.Min(card.RestartCnt, 100);
            var bonusPoints = Math.Max(0, card.RestartCnt - 100);

            var totalStars = card.GetTotalCardStarCount();
            var baseStars = Math.Min(totalStars, 50);
            var bonusStars = Math.Max(0, totalStars - 50);

            return (basePoints * 2) + (bonusPoints / 2) + (baseStars * 5) + bonusStars;
        }

        public static int GetAttackWithBonus(this Card card)
        {
            var maxAttack = card.FromFigure ? MaxUltimateAttack : 999;
            var newAttack = card.Attack + card.GetAttackFromStars();
            newAttack += card.FromFigure ? card.AttackBonus : card.AttackBonus / 3;

            if (card.Curse == CardCurse.LoweredStats)
            {
                newAttack -= newAttack * 5 / 10;
            }

            return Math.Min(newAttack, maxAttack);
        }

        public static int GetDefenceWithBonus(this Card card)
        {
            var maxDefence = card.FromFigure ? MaxUltimateDefence : 99;
            var newDefence = card.Defence + card.GetDefenceFromStars();
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
                    var toRecover = Math.Min(Waifu.FatigueRecoveryRate * breakFromExpedition, double.MaxValue);
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
                        return UltimateExpToUpgrade((int)q);
                    }
                    return 1000;
                case Rarity.SS:
                    return 100;

                default:
                    return 30 + (4 * (7 - (int)r));
            }
        }

        private static double UltimateExpToUpgrade(int qualityValue) => 1000 + (50 * qualityValue);

        public static double ExpToUpgrade(this Card card)
        {
            if (card.FromFigure && card.Rarity == Rarity.SSS)
                return UltimateExpToUpgrade(card.GetUltimateValue());

            return card.Rarity.ExpToUpgrade(card.FromFigure, card.Quality);
        }

        // the overflow adds whole points to the quality value and the card plays as the highest quality that value reaches,
        // so the gaps between qualities fill in by themselves if a new quality is ever added there
        public static int GetUltimateValue(this Card card) => (int)card.Quality + card.BorderOverflow;

        public static Quality GetEffectiveQuality(this Card card) => QualityReachedBy(card.GetUltimateValue());

        public static Quality QualityReachedBy(int value)
        {
            var reached = Quality.Broken;
            foreach (var quality in Enum.GetValues(typeof(Quality)).Cast<Quality>())
            {
                if ((int)quality <= value)
                    reached = quality;
            }
            return reached;
        }

        // both the overflow and a normal upgrade take a figure or an ultimate card of the quality the card plays as
        public static string GetUltimateUpgradeBlockReason(this Card card, Quality offered, bool isFigure)
        {
            var required = card.GetEffectiveQuality();
            if (required == Quality.Omega)
                return "tej karty nie można już ulepszyć.";

            if (offered != required)
                return $"{(isFigure ? "ta figurka" : "ta karta")} nie ma wymaganej jakości (potrzebna: {required.ToName()}).";

            return null;
        }

        public static void ApplyUltimateUpgrade(this Card card, bool overflow)
        {
            if (overflow)
            {
                card.BorderOverflow += 1;
                return;
            }

            card.Quality = card.GetEffectiveQuality().Next();
            card.BorderOverflow = 0;
            card.BorderVariant = 0;
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

        public static void DecAffectionOnExpeditionBy(this Card card, ref double value)
        {
            card.Affection -= value;

            switch (card.Expedition)
            {
                case CardExpedition.UltimateEasy:
                {
                    if (card.Affection < 0)
                    {
                        var r = -card.Affection;
                        card.Affection = 0;
                        value -= r;
                    }
                }
                break;

                case CardExpedition.UltimateMedium:
                {
                    if (card.Affection < -10)
                    {
                        var r = -card.Affection - 10;
                        card.Affection = -10;
                        value -= r;
                    }
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

        public static bool IsCardAnimated(this Card card) => card.IsAnimatedImage || card.Quality == Quality.Omega;

        public static string GetImage(this Card card) => card.CustomImage ?? card.Image;

        public static async Task Update(this Card card, IUser user, ShindenCompat.ShindenClient client, bool updateTitle = false)
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
        {
            type = default;
            return s != null && _starStyleParsingDic.TryGetValue(s.RemoveDiacritics().ToLowerInvariant(), out type);
        }

        public static StarStyle Parse(this StarStyle star, string s)
            => star.TryParse(s, out var type) ? type : throw new Exception("Couldn't parse input!");

        public const double PrecisionPerItem = 0.1;
        public const double MaxCritChance = 25;
        public const double MaxUltimateCritChance = 40;

        public static double GetMaxCritChance(this Card card) => card.FromFigure ? MaxUltimateCritChance : MaxCritChance;

        // glasses beyond the limit would be used up without changing anything
        public static long UsefulPrecisionItems(this Card card)
            => Math.Max(0, (long)Math.Floor((card.GetMaxCritChance() - card.CritChance) / PrecisionPerItem + 1e-6));

        public static void AddCritChance(this Card card, long items)
            => card.CritChance = Math.Min(card.GetMaxCritChance(), Math.Round(card.CritChance + items * PrecisionPerItem, 1));

        public const int MaxUltimateAttack = 9999;
        public const int MaxUltimateDefence = 9999;
        public const int MaxUltimateHealth = 99999;

        // pills beyond a stat limit would be used up without changing anything
        public static long UsefulUltimatePills(this Card card, ItemType type)
        {
            static long Room(long max, long current, int perPill) => Math.Max(0, (max - current + perPill - 1) / perPill);

            var attack = card.Attack + card.GetAttackFromStars() + card.AttackBonus;
            var defence = card.Defence + card.GetDefenceFromStars() + card.DefenceBonus;
            var health = card.Health + card.HealthBonus;

            return type switch
            {
                ItemType.IncreaseUltimateAttack => Room(MaxUltimateAttack, attack, 5),
                ItemType.IncreaseUltimateDefence => Room(MaxUltimateDefence, defence, 3),
                ItemType.IncreaseUltimateHealth => Room(MaxUltimateHealth, health, 5),
                ItemType.IncreaseUltimateAll => new[] { Room(MaxUltimateAttack, attack, 4), Room(MaxUltimateDefence, defence, 4), Room(MaxUltimateHealth, health, 4) }.Max(),
                _ => long.MaxValue,
            };
        }

        public static string UltimateStatAtLimit(this ItemType type) => type switch
        {
            ItemType.IncreaseUltimateAttack => $"atak tej karty jest już maksymalny ({MaxUltimateAttack}).",
            ItemType.IncreaseUltimateDefence => $"obrona tej karty jest już maksymalna ({MaxUltimateDefence}).",
            ItemType.IncreaseUltimateHealth => $"punkty życia tej karty są już maksymalne ({MaxUltimateHealth}).",
            _ => "wszystkie parametry tej karty są już maksymalne.",
        };

        // omega is the last quality, so its pills are enough to max every stat
        public static long GetMaxUltimateParams(this Card card) => card.GetEffectiveQuality() == Quality.Omega
            ? (long)MaxUltimateHealth + MaxUltimateAttack + MaxUltimateDefence
            : 4900L * card.GetUltimateValue();

        public static ExecutionResult CanUpgradePower(this Card card, long points)
        {
            if (!card.FromFigure)
            {
                return ExecutionResult.FromError("ten przedmiot można użyć tylko na karcie ultimate.");
            }

            long currParams = card.AttackBonus + card.HealthBonus + card.DefenceBonus;
            long maxParams = card.GetMaxUltimateParams();
            if (currParams + points > maxParams)
            {
                return ExecutionResult.FromError("nie można już bardziej zwiększyć parametrów na tej karcie.");
            }

            return ExecutionResult.FromSuccess("");
        }

        public static Api.Models.CardFinalView ToView(this Card c, string name, ulong shinden = 0, ISystemTime time = null)
            => string.IsNullOrEmpty(name) ? c.ToViewNoUser(shinden, time) : c.ToViewUser(name, shinden, time);

        private static Api.Models.CardFinalView ToViewUser(this Card c, string name, ulong shinden = 0, ISystemTime time = null)
            => Api.Models.CardFinalView.ConvertFromRawWithUserInfo(c, name, shinden, time);

        private static Api.Models.CardFinalView ToViewNoUser(this Card c, ulong shindenId = 0, ISystemTime time = null)
            => Api.Models.CardFinalView.ConvertFromRawWithUserInfo(c, "????", shindenId, time);

        public static Api.Models.ExpeditionCard ToExpeditionView(this Card card, User user, Expedition helper, string username)
            => Api.Models.ExpeditionCard.ConvertFromRaw(user, card, helper, username);

        public static List<Api.Models.ExpeditionCard> ToExpeditionView(this IEnumerable<Card> clist, User user, Expedition helper, string username)
        {
            var list = new List<Api.Models.ExpeditionCard>();
            foreach (var c in clist) list.Add(c.ToExpeditionView(user, helper, username));
            return list;
        }

        public static List<Api.Models.CardFinalView> ToView(this IEnumerable<Card> clist, string name, ulong shindenId = 0, ISystemTime time = null)
        {
            var list = new List<Api.Models.CardFinalView>();
            foreach (var c in clist) list.Add(c.ToView(name, shindenId, time));
            return list;
        }

        public static string ToHeartWishlist(this Card card, bool isOnUserWishlist = false)
        {
            if (Fun.IsAF() && Fun.TakeATry(75d))
                return $"💗 ({Fun.GetAFKC(card.Character)}) ";

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

            user.GameDeck.Karma -= user.GameDeck.IsSupaBad() ? (0.95 + crueltyBonus) : (1.25 + crueltyBonus);
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

            user.GameDeck.Karma += user.GameDeck.IsSupaGood() ? (0.95 - crueltyBonus) : (1.25 - crueltyBonus);
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

            // karta zmieniła właściciela - obie talie muszą mieć spójne liczniki (wpływają na matchmaking PvP)
            source.user.GameDeck.RecalculateDeck();
            target.user.GameDeck.RecalculateDeck();
        }

        public static int GetMarketBonusFromUltimate(this Card card) => card.GetEffectiveQuality() switch
        {
            Quality.Alpha   => 1,
            Quality.Beta    => 2,
            Quality.Gamma   => 3,
            Quality.Delta   => 4,
            Quality.Epsilon => 4,
            Quality.Zeta    => 5,
            Quality.Eta     => 5,
            Quality.Theta   => 6,
            Quality.Jota    => 6,
            Quality.Lambda  => 7,
            Quality.Sigma   => 7,
            Quality.Omega   => 8,
            _ => 0
        };

        public const string ProtectedFromDiscardingInfo = "chronione są karty aktywne, w klatce, ulubione, ultimate, przeklęte, zmęczone i na wyprawie";

        public static bool IsProtectedFromDiscarding(this Card card, TagHelper helper) => card is null
            || card.Active
            || card.InCage
            || helper.HasTag(card, Services.PocketWaifu.TagType.Favorite)
            || card.FromFigure
            || card.Curse != CardCurse.None
            || card.IsBlockadeFromFatigue()
            || card.Expedition != CardExpedition.None;

        public static string GetUpgradeSacrificeBlockReason(this Card sacrifice, Card target, bool isFavorite)
        {
            if (sacrifice.Id == target.Id)
                return "nie można poświęcić karty, którą się ulepsza.";

            if (sacrifice.Expedition != CardExpedition.None)
                return "karta do poświęcenia jest na wyprawie!";

            if (sacrifice.InCage)
                return "karta do poświęcenia znajduje się w klatce.";

            if (sacrifice.Active)
                return "karta do poświęcenia znajduje się w talii.";

            if (isFavorite)
                return "karta do poświęcenia jest oznaczona jako ulubiona.";

            return null;
        }

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
