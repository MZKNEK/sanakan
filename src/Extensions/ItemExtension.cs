#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Sanakan.Database.Models;
using Sanakan.Services;
using Sanakan.Services.PocketWaifu;
using Shinden;
using SixLabors.ImageSharp.Drawing;
using static Sanakan.Services.PocketWaifu.SafariImage;

namespace Sanakan.Extensions
{
    public static class ItemExtension
    {
        public static string Desc(this ItemType type)
        {
            switch (type)
            {
                case ItemType.AffectionRecoveryGreat:
                    return "Poprawia relacje z kartą w dużym stopniu.";
                case ItemType.AffectionRecoveryBig:
                    return "Poprawia relacje z kartą w znacznym stopniu.";
                case ItemType.AffectionRecoveryNormal:
                    return "Poprawia relacje z kartą.";
                case ItemType.BetterIncreaseUpgradeCnt:
                    return "Może zwiększyć znacznie liczbę ulepszeń karty, tylko kto by chciał twoją krew?";
                case ItemType.IncreaseUpgradeCnt:
                    return "Dodaje dodatkowy punkt ulepszenia do karty.";
                case ItemType.DereReRoll:
                    return "Pozwala zmienić charakter karty.";
                case ItemType.CardParamsReRoll:
                    return "Pozwala wylosować na nowo parametry karty.";
                case ItemType.RandomBoosterPackSingleE:
                    return "Dodaje nowy pakiet z dwiema losowymi kartami.\n\nWykluczone jakości to: SS, S i A.";
                case ItemType.BigRandomBoosterPackE:
                    return "Dodaje nowy pakiet z dwudziestoma losowymi kartami.\n\nWykluczone jakości to: SS i S.";
                case ItemType.RandomTitleBoosterPackSingleE:
                    return "Dodaje nowy pakiet z dwiema losowymi, niewymienialnymi kartami z tytułu podanego przez kupującego.\n\nWykluczone jakości to: SS i S.";
                case ItemType.AffectionRecoverySmall:
                    return "Poprawia odrobinę relacje z kartą.";
                case ItemType.RandomNormalBoosterPackB:
                    return "Dodaje nowy pakiet z trzema losowymi kartami, w tym jedną o gwarantowanej jakości B.\n\nWykluczone jakości to: SS.";
                case ItemType.RandomNormalBoosterPackA:
                    return "Dodaje nowy pakiet z trzema losowymi kartami, w tym jedną o gwarantowanej jakości A.\n\nWykluczone jakości to: SS.";
                case ItemType.RandomNormalBoosterPackS:
                    return "Dodaje nowy pakiet z trzema losowymi kartami, w tym jedną o gwarantowanej jakości S.\n\nWykluczone jakości to: SS.";
                case ItemType.RandomNormalBoosterPackSS:
                    return "Dodaje nowy pakiet z trzema losowymi kartami, w tym jedną o gwarantowanej jakości SS.";
                case ItemType.CheckAffection:
                    return "Pozwala sprawdzić dokładny poziom relacji z kartą.";
                case ItemType.SetCustomImage:
                    return "Pozwala ustawić własny obrazek karcie. Zalecany wymiary 448x650.";
                case ItemType.IncreaseExpSmall:
                    return "Dodaje odrobinę punktów doświadczenia do karty.";
                case ItemType.IncreaseExpBig:
                    return "Dodaje punkty doświadczenia do karty.";
                case ItemType.ChangeStarType:
                    return "Pozwala zmienić typ gwiazdek na karcie.";
                case ItemType.SetCustomBorder:
                    return "Pozwala ustawić ramkę karcie kiedy jest wyświetlana w profilu.";
                case ItemType.ChangeCardImage:
                    return "Pozwala wybrać inny obrazek z shindena.";
                case ItemType.PreAssembledAsuna:
                case ItemType.PreAssembledGintoki:
                case ItemType.PreAssembledMegumin:
                    return "Gotowy szkielet nie wymagający użycia karty SSS.";
                case ItemType.FigureSkeleton:
                    return $"Szkielet pozwalający rozpoczęcie tworzenia figurki.";
                case ItemType.FigureUniversalPart:
                    return $"Uniwersalna część, którą można zamontować jako dowolną część ciała figurki.";
                case ItemType.FigureHeadPart:
                    return $"Część, którą można zamontować jako głowę figurki.";
                case ItemType.FigureBodyPart:
                    return $"Część, którą można zamontować jako tułów figurki.";
                case ItemType.FigureClothesPart:
                    return $"Część, którą można zamontować jako ciuchy figurki.";
                case ItemType.FigureLeftArmPart:
                    return $"Część, którą można zamontować jako lewą rękę figurki.";
                case ItemType.FigureLeftLegPart:
                    return $"Część, którą można zamontować jako lewą nogę figurki.";
                case ItemType.FigureRightArmPart:
                    return $"Część, którą można zamontować jako prawą rękę figurki.";
                case ItemType.FigureRightLegPart:
                    return $"Część, którą można zamontować jako prawą nogę figurki.";
                case ItemType.ResetCardValue:
                    return $"Resetuje warość karty do początkowego poziomu.";
                case ItemType.LotteryTicket:
                    return $"Zapewnia jedno wejście na loterię.";
                case ItemType.IncreaseUltimateAttack:
                    return $"Zwiększa atak karcie ultimate.";
                case ItemType.IncreaseUltimateDefence:
                    return $"Zwiększa obronę karcie ultimate.";
                case ItemType.IncreaseUltimateHealth:
                    return $"Zwiększa punkty życia karcie ultimate.";
                case ItemType.IncreaseUltimateAll:
                    return $"Zwiększa wszystkie parametry karcie ultimate.";

                default:
                    return "Brak opisu.";
            }
        }

        public static string Info(this ItemType type, Card card = null)
        {
            switch (type)
            {
                case ItemType.AffectionRecoveryGreat:
                    return "Bardzo powiększyła się relacja z kartą!";
                case ItemType.AffectionRecoveryBig:
                    return "Znacznie powiększyła się relacja z kartą!";
                case ItemType.AffectionRecoveryNormal:
                    return "Powiększyła się relacja z kartą!";
                case ItemType.AffectionRecoverySmall:
                    return "Powiększyła się trochę relacja z kartą!";
                case ItemType.IncreaseExpSmall:
                    return "Twoja karta otrzymała odrobinę punktów doświadczenia!";
                case ItemType.IncreaseExpBig:
                    return "Twoja karta otrzymała punkty doświadczenia!";
                case ItemType.ChangeStarType:
                    return "Zmieniono typ gwiazdki!";
                case ItemType.ChangeCardImage:
                    return "Ustawiono nowy obrazek.";
                case ItemType.SetCustomImage:
                    return "Ustawiono nowy obrazek. Pamiętaj jednak, że dodanie nieodpowiedniego obrazka może skutkować skasowaniem karty!";
                case ItemType.SetCustomBorder:
                    return "Ustawiono nowy obrazek jako ramkę. Pamiętaj jednak, że dodanie nieodpowiedniego obrazka może skutkować skasowaniem karty!";
                case ItemType.IncreaseUpgradeCnt:
                    return $"Zwiększono liczbę ulepszeń do {card.UpgradesCnt}!";
                case ItemType.ResetCardValue:
                    return "Wartość karty została zresetowana.";
                case ItemType.DereReRoll:
                    return $"Nowy charakter to: {card.Dere}!";
                case ItemType.CardParamsReRoll:
                    return $"Nowa moc karty to: 🔥{card.GetAttackWithBonus()} 🛡{card.GetDefenceWithBonus()}!";
                case ItemType.CheckAffection:
                    return $"Relacja wynosi: `{card.Affection:F}`";
                case ItemType.IncreaseUltimateAttack:
                    return $"Zwiększono atak karty!";
                case ItemType.IncreaseUltimateDefence:
                    return $"Zwiększono obronę karty!";
                case ItemType.IncreaseUltimateHealth:
                    return $"Zwiększono punkty życia karty!";
                case ItemType.IncreaseUltimateAll:
                    return $"Zwiększono parametry karty!";

                default:
                    return "";
            }
        }

        public static string Name(this ItemType type, string quality = "")
        {
            if (!string.IsNullOrEmpty(quality))
                quality = $" {quality}";

            switch (type)
            {
                case ItemType.AffectionRecoveryGreat:
                    return $"Wielka fontanna czekolady{quality}";
                case ItemType.AffectionRecoveryBig:
                    return $"Tort czekoladowy{quality}";
                case ItemType.AffectionRecoveryNormal:
                    return $"Ciasto truskawkowe{quality}";
                case ItemType.BetterIncreaseUpgradeCnt:
                    return "Kropla twojej krwi";
                case ItemType.IncreaseUpgradeCnt:
                    return "Pierścionek zaręczynowy";
                case ItemType.DereReRoll:
                    return "Bukiet kwiatów";
                case ItemType.CardParamsReRoll:
                    return "Naszyjnik z diamentem";
                case ItemType.RandomBoosterPackSingleE:
                    return "Tani pakiet losowych kart";
                case ItemType.BigRandomBoosterPackE:
                    return "Może i nie tani ale za to duży pakiet kart";
                case ItemType.RandomTitleBoosterPackSingleE:
                    return "Pakiet losowych kart z tytułu";
                case ItemType.AffectionRecoverySmall:
                    return $"Banan w czekoladzie{quality}";
                case ItemType.RandomNormalBoosterPackB:
                    return "Fioletowy pakiet losowych kart";
                case ItemType.RandomNormalBoosterPackA:
                    return "Pomarańczowy pakiet losowych kart";
                case ItemType.RandomNormalBoosterPackS:
                    return "Złoty pakiet losowych kart";
                case ItemType.RandomNormalBoosterPackSS:
                    return "Różowy pakiet losowych kart";
                case ItemType.CheckAffection:
                    return "Kryształowa kula";
                case ItemType.SetCustomImage:
                    return "Skalpel";
                case ItemType.IncreaseExpSmall:
                    return $"Mleko truskawkowe{quality}";
                case ItemType.IncreaseExpBig:
                    return $"Gorąca czekolada{quality}";
                case ItemType.ChangeStarType:
                    return "Stempel";
                case ItemType.SetCustomBorder:
                    return "Nożyczki";
                case ItemType.ChangeCardImage:
                    return "Plastelina";
                case ItemType.PreAssembledAsuna:
                    return "Szkielet Asuny (SAO)";
                case ItemType.PreAssembledGintoki:
                    return "Szkielet Gintokiego (Gintama)";
                case ItemType.PreAssembledMegumin:
                    return "Szkielet Megumin (Konosuba)";
                case ItemType.FigureSkeleton:
                    return $"Szkielet{quality}";
                case ItemType.FigureUniversalPart:
                    return $"Uniwersalna część figurki{quality}";
                case ItemType.FigureHeadPart:
                    return $"Głowa figurki{quality}";
                case ItemType.FigureBodyPart:
                    return $"Tułów figurki{quality}";
                case ItemType.FigureClothesPart:
                    return $"Ciuchy figurki{quality}";
                case ItemType.FigureLeftArmPart:
                    return $"Lewa ręka{quality}";
                case ItemType.FigureLeftLegPart:
                    return $"Lewa noga{quality}";
                case ItemType.FigureRightArmPart:
                    return $"Prawa ręka{quality}";
                case ItemType.FigureRightLegPart:
                    return $"Prawa noga{quality}";
                case ItemType.ResetCardValue:
                    return $"Marker";
                case ItemType.LotteryTicket:
                    return $"Przepustka";
                case ItemType.IncreaseUltimateAttack:
                    return $"Czerwona pigułka";
                case ItemType.IncreaseUltimateDefence:
                    return $"Niebieska pigułka";
                case ItemType.IncreaseUltimateHealth:
                    return $"Zielona pigułka";
                case ItemType.IncreaseUltimateAll:
                    return $"Czarna pigułka";

                default:
                    return "Brak";
            }
        }

        public static double GetQualityModifier(this Quality quality) => 0.1 * (int)quality;

        public static double GetBaseAffection(this Item item)
        {
            var aff = item.Type.GetBaseAffection();
            if (item.Type.HasDifferentQualities())
            {
                aff += aff * item.Quality.GetQualityModifier();
            }
            return aff;
        }

        public static double GetBaseAffection(this ItemType type)
        {
            switch (type)
            {
                case ItemType.AffectionRecoveryGreat: return 1.6;
                case ItemType.AffectionRecoveryBig: return 1;
                case ItemType.AffectionRecoveryNormal: return 0.12;
                case ItemType.AffectionRecoverySmall: return 0.02;
                case ItemType.BetterIncreaseUpgradeCnt: return 1.7;
                case ItemType.IncreaseUpgradeCnt: return 0.7;
                case ItemType.DereReRoll: return 0.1;
                case ItemType.CardParamsReRoll: return 0.2;
                case ItemType.CheckAffection: return 0.2;
                case ItemType.SetCustomImage: return 0.5;
                case ItemType.IncreaseExpSmall: return 0.15;
                case ItemType.IncreaseExpBig: return 0.25;
                case ItemType.ChangeStarType: return 0.3;
                case ItemType.SetCustomBorder: return 0.4;
                case ItemType.ChangeCardImage: return 0.1;
                case ItemType.ResetCardValue: return 0.1;
                case ItemType.IncreaseUltimateAttack: return 0.35;
                case ItemType.IncreaseUltimateDefence: return 0.35;
                case ItemType.IncreaseUltimateHealth: return 0.55;
                case ItemType.IncreaseUltimateAll: return 2.2;

                default: return 0;
            }
        }

        public static double GetBaseKarmaChange(this ItemType type)
        {
            switch (type)
            {
                case ItemType.AffectionRecoveryGreat: return 0.3;
                case ItemType.AffectionRecoveryBig: return 0.1;
                case ItemType.AffectionRecoveryNormal: return 0.01;
                case ItemType.AffectionRecoverySmall: return 0.001;
                case ItemType.IncreaseExpSmall: return 0.1;
                case ItemType.IncreaseExpBig: return 0.3;
                case ItemType.ChangeStarType: return 0.001;
                case ItemType.ChangeCardImage: return 0.001;
                case ItemType.SetCustomImage: return 0.001;
                case ItemType.SetCustomBorder: return 0.001;
                case ItemType.IncreaseUpgradeCnt: return 1;
                case ItemType.ResetCardValue: return 0.5;
                case ItemType.DereReRoll: return 0.02;
                case ItemType.CardParamsReRoll: return 0.03;
                case ItemType.CheckAffection: return -0.01;
                case ItemType.IncreaseUltimateAttack: return 0.4;
                case ItemType.IncreaseUltimateDefence: return 0.4;
                case ItemType.IncreaseUltimateHealth: return 0.6;
                case ItemType.IncreaseUltimateAll: return 1.2;
                case ItemType.FigureSkeleton: return -1;

                default: return 0;
            }
        }

        public static bool CanBeUsedWithNormalUseCommand(this ItemType type)
        {
            switch (type)
            {
                case ItemType.LotteryTicket:
                    return false;

                default:
                    return true;
            }
        }

        public static bool CanUseWithoutCard(this ItemType type, bool toExp)
        {
            switch (type)
            {
                case ItemType.FigureSkeleton:
                    return toExp;

                case ItemType.FigureHeadPart:
                case ItemType.FigureBodyPart:
                case ItemType.FigureClothesPart:
                case ItemType.FigureLeftArmPart:
                case ItemType.FigureLeftLegPart:
                case ItemType.FigureRightArmPart:
                case ItemType.FigureRightLegPart:
                case ItemType.FigureUniversalPart:
                    return true;

                default:
                    return false;
            }
        }

        public static bool CanUseMoreThanOne(this ItemType type, bool toExp)
        {
            switch (type)
            {
                case ItemType.AffectionRecoveryBig:
                case ItemType.AffectionRecoverySmall:
                case ItemType.AffectionRecoveryNormal:
                case ItemType.AffectionRecoveryGreat:
                case ItemType.IncreaseUpgradeCnt:
                case ItemType.IncreaseExpSmall:
                case ItemType.IncreaseExpBig:
                case ItemType.IncreaseUltimateAttack:
                case ItemType.IncreaseUltimateDefence:
                case ItemType.IncreaseUltimateHealth:
                case ItemType.IncreaseUltimateAll:
                // special case
                case ItemType.CardParamsReRoll:
                case ItemType.DereReRoll:
                case ItemType.ChangeCardImage:
                    return true;

                case ItemType.FigureUniversalPart:
                case ItemType.FigureHeadPart:
                case ItemType.FigureBodyPart:
                case ItemType.FigureLeftArmPart:
                case ItemType.FigureRightArmPart:
                case ItemType.FigureLeftLegPart:
                case ItemType.FigureRightLegPart:
                case ItemType.FigureClothesPart:
                case ItemType.FigureSkeleton:
                    if (toExp) return true;
                    return false;

                default:
                    return false;
            }
        }

        public static FigurePart GetPartType(this ItemType type)
        {
            switch (type)
            {
                case ItemType.FigureUniversalPart:
                    return FigurePart.All;
                case ItemType.FigureHeadPart:
                    return FigurePart.Head;
                case ItemType.FigureBodyPart:
                    return FigurePart.Body;
                case ItemType.FigureClothesPart:
                    return FigurePart.Clothes;
                case ItemType.FigureLeftArmPart:
                    return FigurePart.LeftArm;
                case ItemType.FigureLeftLegPart:
                    return FigurePart.LeftLeg;
                case ItemType.FigureRightArmPart:
                    return FigurePart.RightArm;
                case ItemType.FigureRightLegPart:
                    return FigurePart.RightLeg;

                default:
                    return FigurePart.None;
            }
        }

        public static bool HasDifferentQualitiesOnExpedition(this CardExpedition expedition)
        {
            switch (expedition)
            {
                case CardExpedition.UltimateEasy:
                case CardExpedition.UltimateMedium:
                case CardExpedition.UltimateHard:
                case CardExpedition.UltimateHardcore:
                case CardExpedition.ExtremeItemWithExp:
                    return true;

                default:
                    return false;
            }
        }

        public static bool HasDifferentQualities(this ItemType type)
        {
            switch (type)
            {
                case ItemType.FigureSkeleton:
                case ItemType.FigureUniversalPart:
                case ItemType.FigureHeadPart:
                case ItemType.FigureBodyPart:
                case ItemType.FigureClothesPart:
                case ItemType.FigureLeftArmPart:
                case ItemType.FigureLeftLegPart:
                case ItemType.FigureRightArmPart:
                case ItemType.FigureRightLegPart:
                    return true;

                default:
                    return false;
            }
        }

        public static long CValue(this ItemType type)
        {
            switch (type)
            {
                case ItemType.AffectionRecoveryGreat:
                    return 180;
                case ItemType.AffectionRecoveryBig:
                    return 140;
                case ItemType.AffectionRecoveryNormal:
                    return 15;
                case ItemType.BetterIncreaseUpgradeCnt:
                    return 500;
                case ItemType.IncreaseUpgradeCnt:
                    return 200;
                case ItemType.DereReRoll:
                    return 10;
                case ItemType.CardParamsReRoll:
                    return 15;
                case ItemType.CheckAffection:
                    return 15;
                case ItemType.SetCustomImage:
                    return 300;
                case ItemType.IncreaseExpSmall:
                    return 100;
                case ItemType.IncreaseExpBig:
                    return 400;
                case ItemType.ChangeStarType:
                    return 50;
                case ItemType.SetCustomBorder:
                    return 80;
                case ItemType.ChangeCardImage:
                    return 10;
                case ItemType.ResetCardValue:
                    return 5;
                case ItemType.LotteryTicket:
                    return 200;
                case ItemType.IncreaseUltimateAttack:
                    return 80;
                case ItemType.IncreaseUltimateDefence:
                    return 70;
                case ItemType.IncreaseUltimateHealth:
                    return 100;
                case ItemType.IncreaseUltimateAll:
                    return 800;

                default:
                    return 1;
            }
        }

        public static bool IsBoosterPack(this ItemType type)
        {
            switch (type)
            {
                case ItemType.RandomBoosterPackSingleE:
                case ItemType.RandomTitleBoosterPackSingleE:
                case ItemType.RandomNormalBoosterPackB:
                case ItemType.RandomNormalBoosterPackA:
                case ItemType.RandomNormalBoosterPackS:
                case ItemType.RandomNormalBoosterPackSS:
                case ItemType.BigRandomBoosterPackE:
                    return true;

                default:
                    return false;
            }
        }

        public static bool IsPreAssembledFigure(this ItemType type)
        {
            switch (type)
            {
                case ItemType.PreAssembledAsuna:
                case ItemType.PreAssembledGintoki:
                case ItemType.PreAssembledMegumin:
                    return true;

                default:
                    return false;
            }
        }

        public static int Count(this ItemType type)
        {
            switch (type)
            {
                case ItemType.RandomNormalBoosterPackB:
                case ItemType.RandomNormalBoosterPackA:
                case ItemType.RandomNormalBoosterPackS:
                case ItemType.RandomNormalBoosterPackSS:
                    return 3;

                case ItemType.BigRandomBoosterPackE:
                    return 20;

                default:
                    return 2;
            }
        }

        public static Rarity MinRarity(this ItemType type)
        {
            switch (type)
            {
                case ItemType.RandomNormalBoosterPackSS:
                    return Rarity.SS;

                case ItemType.RandomNormalBoosterPackS:
                    return Rarity.S;

                case ItemType.RandomNormalBoosterPackA:
                    return Rarity.A;

                case ItemType.RandomNormalBoosterPackB:
                    return Rarity.B;

                default:
                    return Rarity.E;
            }
        }

        public static bool IsTradable(this ItemType type)
        {
            switch (type)
            {
                case ItemType.RandomTitleBoosterPackSingleE:
                    return false;

                default:
                    return true;
            }
        }

        public static CardSource GetSource(this ItemType type)
        {
            switch (type)
            {
                case ItemType.RandomBoosterPackSingleE:
                case ItemType.RandomNormalBoosterPackB:
                case ItemType.RandomNormalBoosterPackA:
                case ItemType.RandomNormalBoosterPackS:
                case ItemType.RandomNormalBoosterPackSS:
                case ItemType.RandomTitleBoosterPackSingleE:
                case ItemType.BigRandomBoosterPackE:
                    return CardSource.Shop;

                default:
                    return CardSource.Other;
            }
        }

        public static List<RarityExcluded> RarityExcluded(this ItemType type)
        {
            var ex = new List<RarityExcluded>();

            switch (type)
            {
                case ItemType.RandomTitleBoosterPackSingleE:
                case ItemType.BigRandomBoosterPackE:
                    ex.Add(new RarityExcluded { Rarity = Rarity.SS });
                    ex.Add(new RarityExcluded { Rarity = Rarity.S });
                    break;

                case ItemType.RandomNormalBoosterPackB:
                case ItemType.RandomNormalBoosterPackA:
                case ItemType.RandomNormalBoosterPackS:
                    ex.Add(new RarityExcluded { Rarity = Rarity.SS });
                    break;

                case ItemType.RandomBoosterPackSingleE:
                    ex.Add(new RarityExcluded { Rarity = Rarity.SS });
                    ex.Add(new RarityExcluded { Rarity = Rarity.S });
                    ex.Add(new RarityExcluded { Rarity = Rarity.A });
                    break;

                default:
                    break;
            }

            return ex;
        }

        public static Item ToItem(this ItemType type, long count = 1, Quality quality = Quality.Broken, bool forceQuality = false)
        {
            if ((!type.HasDifferentQualities() && quality != Quality.Broken) && !forceQuality)
                quality = Quality.Broken;

            return new Item
            {
                Name = type.Name(quality.ToName()),
                Quality = quality,
                Count = count,
                Type = type,
            };
        }

        public static PreAssembledFigure ToPASType(this ItemType type)
        {
            switch (type)
            {
                case ItemType.PreAssembledAsuna:
                    return PreAssembledFigure.Asuna;
                case ItemType.PreAssembledGintoki:
                    return PreAssembledFigure.Gintoki;
                case ItemType.PreAssembledMegumin:
                    return PreAssembledFigure.Megumin;

                default:
                    return PreAssembledFigure.None;
            }
        }

        public static double ToExpForPart(this Item item, Quality skeleton)
        {
            double diff = ((int)skeleton - (int)item.Quality) / 10f;
            if (diff <= 0)
            {
                return 1 + (item.Quality.ToValue() * -diff);
            }
            return 1 / (diff + 2);
        }

        public static Figure ToFigure(this Item item, Card card)
        {
            if (item.Type != ItemType.FigureSkeleton || card.Rarity != Rarity.SSS)
                return null;

            var maxExp = card.ExpToUpgrade();
            var expToMove = card.ExpCnt;
            if (expToMove > maxExp)
                expToMove = maxExp;

            return new Figure
            {
                PartExp = 0,
                IsFocus = false,
                Dere = card.Dere,
                Name = card.Name,
                IsComplete = false,
                ExpCnt = expToMove,
                Title = card.Title,
                Health = card.Health,
                Attack = card.Attack,
                Defence = card.Defence,
                Character = card.Character,
                BodyQuality = Quality.Broken,
                RestartCnt = card.RestartCnt,
                HeadQuality = Quality.Broken,
                PAS = PreAssembledFigure.None,
                CompletionDate = DateTime.Now,
                FocusedPart = FigurePart.Head,
                SkeletonQuality = item.Quality,
                ClothesQuality = Quality.Broken,
                LeftArmQuality = Quality.Broken,
                LeftLegQuality = Quality.Broken,
                RightArmQuality = Quality.Broken,
                RightLegQuality = Quality.Broken,
            };
        }

        public static Figure ToPAFigure(this ItemType type)
        {
            if (!type.IsPreAssembledFigure())
                return null;

            var pas = type.ToPASType();

            return new Figure
            {
                PAS = pas,
                ExpCnt = 0,
                PartExp = 0,
                Health = 300,
                RestartCnt = 0,
                IsFocus = false,
                IsComplete = false,
                Dere = Dere.Yandere,
                Title = pas.GetTitleName(),
                BodyQuality = Quality.Alpha,
                HeadQuality = Quality.Alpha,
                Name = pas.GetCharacterName(),
                CompletionDate = DateTime.Now,
                FocusedPart = FigurePart.Head,
                ClothesQuality = Quality.Alpha,
                LeftArmQuality = Quality.Alpha,
                LeftLegQuality = Quality.Alpha,
                RightArmQuality = Quality.Alpha,
                RightLegQuality = Quality.Alpha,
                SkeletonQuality = Quality.Alpha,
                Character = pas.GetCharacterId(),
                Attack = Rarity.SSS.GetAttackMin(),
                Defence = Rarity.SSS.GetDefenceMin(),
            };
        }

        public static BoosterPack ToBoosterPack(this ItemType type)
        {
            if (!type.IsBoosterPack())
                return null;

            return new BoosterPack
            {
                Name = type.Name(),
                CardCnt = type.Count(),
                MinRarity = type.MinRarity(),
                CardSourceFromPack = type.GetSource(),
                IsCardFromPackTradable = type.IsTradable(),
                RarityExcludedFromPack = type.RarityExcluded(),
            };
        }

        public static string ToItemListString(this List<Item> list)
        {
            var items = new System.Text.StringBuilder();
            for (int i = 0; i < list.Count; i++)
                items.AppendLine($"**[{i + 1}]** {list[i].Name} x{list[i].Count}");

            return items.ToString();
        }

        public static List<string> ToItemList(this List<Item> list)
        {
            var items = new List<string>();
            for (int i = 0; i < list.Count; i++)
                items.Add($"**[{i + 1}]** {list[i].Name} x{list[i].Count}");

            return items;
        }

        public static List<List<T>> SplitList<T>(this List<T> locations, int nSize = 50)
        {
            var list = new List<List<T>>();

            for (int i = 0; i < locations.Count; i += nSize)
            {
                list.Add(locations.GetRange(i, Math.Min(nSize, locations.Count - i)));
            }

            return list;
        }

        public static List<List<T>> SplitList<T>(this IEnumerable<T> locations, int nSize = 50)
            => locations.ToList().SplitList(nSize);

        public static IEnumerable<T> ToRealEnumerable<T>(this IEnumerable<(T, int)> chances)
        {
            var list = new List<T>();
            foreach (var item in chances)
            {
                list.AddRange(Enumerable.Repeat(item.Item1, item.Item2));
            }
            return list.Shuffle();
        }

        public static List<T> ToRealList<T>(this IEnumerable<(T, int)> chances) => chances.ToRealEnumerable().ToList();

        public static List<(T, float)> GetChances<T>(this IEnumerable<T> list)
        {
            var chances = new List<(T, float)>();
            float all = list.Count();

            foreach (var item in list.Distinct())
            {
                float cnt = list.Count(x => x.Equals(item));
                chances.Add((item, cnt / (all / 100)));
            }

            return chances.ToList();
        }

        public static ExecutionResult Use(this Item item, User user, int itemCnt, bool itemToExp)
        {
            if (!item.Type.CanUseWithoutCard(itemToExp))
            {
                return ExecutionResult.FromError("nie można użyć przedmiotu bez karty.");
            }

            var activeFigure = user.GameDeck.Figures.FirstOrDefault(x => x.IsFocus);
            if (activeFigure == null)
            {
                return ExecutionResult.FromError("nie posiadasz aktywnej figurki!");
            }

            // double karmachange = item.Type.GetBaseKarmaChange() * itemCnt;

            var str = new StringBuilder().Append($"Użyto _{item.Name}_ {((itemCnt > 1) ? $"x{itemCnt}" : "")}\n\n");

            switch (item.Type)
            {
                case ItemType.FigureHeadPart:
                case ItemType.FigureBodyPart:
                case ItemType.FigureClothesPart:
                case ItemType.FigureLeftArmPart:
                case ItemType.FigureLeftLegPart:
                case ItemType.FigureRightArmPart:
                case ItemType.FigureRightLegPart:
                case ItemType.FigureUniversalPart:
                    if (itemToExp)
                    {
                        var itemPartType = item.Type.GetPartType();
                        if (activeFigure.FocusedPart != itemPartType && itemPartType != FigurePart.All)
                            return ExecutionResult.FromError("typy części się nie zgadzają.");

                        var expFromPart = item.ToExpForPart(activeFigure.SkeletonQuality);
                        activeFigure.PartExp += expFromPart * itemCnt;

                        str.Append($"Dodano do wybranej części figurki {expFromPart:F} punktów konstrukcji. W sumie posiada ich {activeFigure.PartExp:F}.");
                        break;
                    }

                    if (!activeFigure.CanAddPart(item))
                        return ExecutionResult.FromError("część, którą próbujesz dodać ma zbyt niską jakość.");

                    if (!activeFigure.HasEnoughPointsToAddPart(item))
                        return ExecutionResult.FromError($"aktywowana część ma zbyt małą ilość punktów konstrukcji, wymagana to {activeFigure.ConstructionPointsToInstall(item)}.");

                    if (!activeFigure.AddPart(item))
                        return ExecutionResult.FromError("coś poszło nie tak.");

                    str.Append("Dodano część do figurki.");
                    break;

                case ItemType.FigureSkeleton:
                    if (itemToExp)
                    {
                        var expFromPart = item.ToExpForPart(activeFigure.SkeletonQuality);
                        activeFigure.PartExp += expFromPart * itemCnt;

                        str.Append($"Dodano do wybranej części figurki {expFromPart:F} punktów konstrukcji. W sumie posiada ich {activeFigure.PartExp:F}.");
                        break;
                    }
                    return ExecutionResult.FromError("nie możesz użyć szkieletu bez karty, chyba, że chcesz przerobić go na exp.");

                default:
                    return ExecutionResult.FromError($"tego przedmiotu ({item.Name}) nie powinno tutaj być!");
            }

            return ExecutionResult.FromSuccess(str.ToString());
        }

        public async static Task<ExecutionResult> UseOnCardAsync(this Item item, User user, string userName, int itemCnt, ulong wid, string detail, Waifu _waifu, ShindenClient shinden)
        {
            var card = user.GameDeck.Cards.FirstOrDefault(x => x.Id == wid);
            if (card == null)
            {
                return ExecutionResult.FromError("nie posiadasz takiej karty!");
            }

            if (card.Expedition != CardExpedition.None)
            {
                return ExecutionResult.FromError("ta karta jest na wyprawie!");
            }

            switch (item.Type)
            {
                case ItemType.FigureSkeleton:
                case ItemType.IncreaseExpBig:
                case ItemType.IncreaseExpSmall:
                case ItemType.CardParamsReRoll:
                case ItemType.IncreaseUpgradeCnt:
                case ItemType.BetterIncreaseUpgradeCnt:
                    if (!card.FromFigure)
                        goto default;
                    return ExecutionResult.FromError("tego przedmiotu nie można użyć na tej karcie.");

                case ItemType.IncreaseUltimateAttack:
                case ItemType.IncreaseUltimateDefence:
                case ItemType.IncreaseUltimateHealth:
                case ItemType.IncreaseUltimateAll:
                    var res = card.CanUpgradePower(itemCnt);
                    if (res.Status == ExecutionResult.EStatus.Error)
                        return res;
                    break;

                default:
                    break;
            }

            var consumeItem = true;
            var embedColor = EMType.Bot;

            var textRelation = card.GetAffectionString();

            double karmaChange = item.Type.GetBaseKarmaChange() * itemCnt;
            double affectionInc = item.Type.GetBaseAffection() * itemCnt;

            var str = new StringBuilder().Append($"Użyto _{item.Name}_ {((itemCnt > 1) ? $"x{itemCnt}" : "")}{(" na " + card.GetString(false, false, true))}\n\n");

            switch (item.Type)
            {
                case ItemType.AffectionRecoveryBig:
                case ItemType.AffectionRecoveryGreat:
                case ItemType.AffectionRecoveryNormal:
                case ItemType.AffectionRecoverySmall:
                case ItemType.IncreaseExpBig:
                case ItemType.IncreaseExpSmall:
                case ItemType.CheckAffection:
                    break;

                case ItemType.ResetCardValue:
                    card.MarketValue = 1;
                    break;

                case ItemType.CardParamsReRoll:
                    card.Attack = Waifu.RandomizeAttack(card.Rarity);
                    card.Defence = Waifu.RandomizeDefence(card.Rarity);
                    break;

                case ItemType.IncreaseUltimateAttack:
                    card.AttackBonus += itemCnt * 5;
                    break;

                case ItemType.IncreaseUltimateDefence:
                    card.DefenceBonus += itemCnt * 3;
                    break;

                case ItemType.IncreaseUltimateHealth:
                    card.HealthBonus += itemCnt * 5;
                    break;

                case ItemType.IncreaseUltimateAll:
                    card.AttackBonus += itemCnt * 5;
                    card.HealthBonus += itemCnt * 5;
                    card.DefenceBonus += itemCnt * 5;
                    break;

                case ItemType.ChangeStarType:
                    try
                    {
                        card.StarStyle = new StarStyle().Parse(detail);
                    }
                    catch (Exception)
                    {
                        return ExecutionResult.FromError("Nie rozpoznano typu gwiazdki!");
                    }
                    break;

                case ItemType.ChangeCardImage:
                    var res = await shinden.GetCharacterInfoAsync(card.Character);
                    if (!res.IsSuccessStatusCode())
                    {
                        return ExecutionResult.FromError("Nie odnaleziono postaci na shinden!");
                    }
                    var urls = res.Body.Pictures.GetPicList();
                    if (itemCnt == 0)
                    {
                        int tidx = 0;
                        return ExecutionResult.FromSuccess("Obrazki: \n" + string.Join("\n", urls.Select(x => $"{++tidx}: {x}")), EMType.Info);
                    }
                    else
                    {
                        if (itemCnt > urls.Count)
                        {
                            return ExecutionResult.FromError("Nie odnaleziono obrazka!");
                        }
                        var turl = urls[itemCnt - 1];
                        if (card.GetImage() == turl)
                        {
                            return ExecutionResult.FromError("Taki obrazek jest już ustawiony!");
                        }
                        card.CustomImage = turl;
                    }
                    break;

                case ItemType.SetCustomImage:
                    if (!detail.IsURLToImage())
                    {
                        return ExecutionResult.FromError("Nie wykryto obrazka! Upewnij się, że podałeś poprawny adres!");
                    }
                    if (card.Image == null && !card.FromFigure)
                    {
                        return ExecutionResult.FromError("Aby ustawić własny obrazek, karta musi posiadać wcześniej ustawiony główny (na stronie)!");
                    }
                    card.CustomImage = detail;
                    consumeItem = !card.FromFigure;
                    break;

                case ItemType.SetCustomBorder:
                    if (!detail.IsURLToImage())
                    {
                        return ExecutionResult.FromError("Nie wykryto obrazka! Upewnij się, że podałeś poprawny adres!");
                    }
                    if (card.Image == null)
                    {
                        return ExecutionResult.FromError("Aby ustawić ramkę, karta musi posiadać wcześniej ustawiony obrazek na stronie!");
                    }
                    card.CustomBorder = detail;
                    break;

                case ItemType.BetterIncreaseUpgradeCnt:
                    if (card.Curse == CardCurse.BloodBlockade)
                    {
                        return ExecutionResult.FromError("na tej karcie ciąży klątwa!");
                    }
                    if (card.Rarity == Rarity.SSS)
                    {
                        return ExecutionResult.FromError("karty **SSS** nie można już ulepszyć!");
                    }
                    if (!card.CanGiveBloodOrUpgradeToSSS())
                    {
                        if (card.HasNoNegativeEffectAfterBloodUsage())
                        {
                            if (card.CanGiveRing())
                            {
                                affectionInc = 1.7;
                                karmaChange += 0.6;
                                str.Append("Bardzo powiększyła się relacja z kartą!");
                            }
                            else
                            {
                                affectionInc = 1.2;
                                karmaChange += 0.4;
                                embedColor = EMType.Warning;
                                str.Append($"Karta się zmartwiła!");
                            }
                        }
                        else
                        {
                            affectionInc = -5;
                            karmaChange -= 0.5;
                            embedColor = EMType.Error;
                            str.Append($"Karta się przeraziła!");
                        }
                    }
                    else
                    {
                        karmaChange += 2;
                        affectionInc = 1.5;
                        card.UpgradesCnt += 2;
                        str.Append($"Zwiększono liczbę ulepszeń do {card.UpgradesCnt}!");
                    }
                    break;

                case ItemType.IncreaseUpgradeCnt:
                    if (!card.CanGiveRing())
                    {
                        return ExecutionResult.FromError("karta musi mieć min. poziom relacji: *Miłość*.");
                    }
                    if (card.Rarity == Rarity.SSS)
                    {
                        return ExecutionResult.FromError("karty **SSS** nie można już ulepszyć!");
                    }
                    if (card.UpgradesCnt + itemCnt > 5)
                    {
                        return ExecutionResult.FromError("nie można mieć więcej jak pięć ulepszeń dostępnych na karcie.");
                    }
                    card.UpgradesCnt += itemCnt;
                    break;

                case ItemType.DereReRoll:
                    if (card.Curse == CardCurse.DereBlockade)
                    {
                        return ExecutionResult.FromError("na tej karcie ciąży klątwa!");
                    }
                    card.Dere = Waifu.RandomizeDere();
                    break;

                case ItemType.FigureSkeleton:
                    if (card.Rarity != Rarity.SSS)
                    {
                        return ExecutionResult.FromError("karta musi być rangi **SSS**.");
                    }

                    if (user.GameDeck.Figures.Any(x => x.Character == card.Character))
                    {
                        return ExecutionResult.FromError("już posiadasz figurkę tej postaci.");
                    }

                    var figure = item.ToFigure(card);
                    if (figure != null)
                    {
                        user.GameDeck.Figures.Add(figure);
                        user.GameDeck.Cards.Remove(card);
                    }
                    str.Append($"Rozpoczęto tworzenie figurki.");
                    break;

                default:
                    return ExecutionResult.FromError($"tego przedmiotu (({item.Name})) nie powinno tutaj być!");
            }

            _waifu.DeleteCardImageIfExist(card);
            str.Append(item.Type.Info(card));

            if (card.Character == user.GameDeck.Waifu)
                affectionInc *= 1.15;

            var response = await shinden.GetCharacterInfoAsync(card.Character);
            if (response.IsSuccessStatusCode())
            {
                if (response.Body?.Points != null)
                {
                    var ordered = response.Body.Points.OrderByDescending(x => x.Points);
                    if (ordered.Any(x => x.Name == userName))
                        affectionInc *= 1.1;
                }
            }

            if (card.Dere == Dere.Tsundere)
                affectionInc *= 1.2;

            if (consumeItem)
                item.Count -= itemCnt;

            if (card.Curse == CardCurse.InvertedItems)
            {
                affectionInc = -affectionInc;
                karmaChange = -karmaChange;
            }

            user.GameDeck.Karma += karmaChange;
            card.Affection += affectionInc;

            _ = card.CalculateCardPower();

            if (textRelation != card.GetAffectionString())
                str.Append($"\nNowa relacja to *{card.GetAffectionString()}*.");

            return ExecutionResult.FromSuccess(str.ToString(), embedColor);
        }
    }
}
