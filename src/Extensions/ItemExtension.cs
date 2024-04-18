#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.Linq;
using Sanakan.Database.Models;

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
                case ItemType.SetCustomAnimatedImage:
                    return "Pozwala ustawić własny animowany obrazek karcie. Zalecany wymiary 448x650.";
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
                case ItemType.CardFragment:
                    return $"Pozwalalają utworzyć kartę po uzbieraniu ich odpowiedniej liczby.";
                case ItemType.BloodOfYourWaifu:
                    return $"Nie mam pojęcia co zamierzasz z tym zrobić.";
                case ItemType.IncreaseUltimateAttack:
                    return $"Zwiększa atak karcie ultimate.";
                case ItemType.IncreaseUltimateDefence:
                    return $"Zwiększa obronę karcie ultimate.";
                case ItemType.IncreaseUltimateHealth:
                    return $"Zwiększa punkty życia karcie ultimate.";
                case ItemType.IncreaseUltimateAll:
                    return $"Zwiększa wszystkie parametry karcie ultimate.";
                case ItemType.GiveTagSlot:
                    return $"Zwiększa limit własnych oznaczeń.";
                case ItemType.RemoveCurse:
                    return $"Zdejmuje klątwę z karty.";
                case ItemType.CreationItemBase:
                    return $"Jest wymagany przy tworzeniu dowolnego przedmiotu.";
                case ItemType.CheckCurse:
                    return $"Pozwala sprawdzić jaka klątwa ciąży na karcie.";
                case ItemType.NotAnItem:
                    return $"Pozwala wytworzyć kartę postaci spoza bazy shindena.";

                default:
                    return "Brak opisu.";
            }
        }

        public static string Info(this ItemType type, Card card = null)
        {
            switch (type)
            {
                case ItemType.GiveTagSlot:
                    return "Zwiększył się twój limit tagów!";
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
                case ItemType.SetCustomAnimatedImage:
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
                    return $"Relacja wynosi: `{card.Curse.ToName()}`";
                case ItemType.CheckCurse:
                    return $"Klątwa: `{card.Affection:F}`";
                case ItemType.IncreaseUltimateAttack:
                    return $"Zwiększono atak karty!";
                case ItemType.IncreaseUltimateDefence:
                    return $"Zwiększono obronę karty!";
                case ItemType.IncreaseUltimateHealth:
                    return $"Zwiększono punkty życia karty!";
                case ItemType.IncreaseUltimateAll:
                    return $"Zwiększono parametry karty!";
                case ItemType.RemoveCurse:
                    return $"Karta już nie jest przeklęta!";

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
                case ItemType.SetCustomAnimatedImage:
                    return "Kamera";
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
                case ItemType.CardFragment:
                    return $"Fragment karty";
                case ItemType.BloodOfYourWaifu:
                    return $"Kropla krwi twojej waifu";
                case ItemType.IncreaseUltimateAttack:
                    return $"Czerwona pigułka";
                case ItemType.IncreaseUltimateDefence:
                    return $"Niebieska pigułka";
                case ItemType.IncreaseUltimateHealth:
                    return $"Zielona pigułka";
                case ItemType.IncreaseUltimateAll:
                    return $"Czarna pigułka";
                case ItemType.RemoveCurse:
                    return $"Krwawa mary";
                case ItemType.CreationItemBase:
                    return $"Rozbita butelka";
                case ItemType.CheckCurse:
                    return $"Lupa";
                case ItemType.GiveTagSlot:
                    return $"Nieśmiertelnik";
                case ItemType.NotAnItem:
                    return $"Magiczna rózga";

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

        public static double GetBaseAffection(this ItemType type) => type switch
        {
            ItemType.AffectionRecoveryGreat  => 2.2,
            ItemType.AffectionRecoveryBig    => 1.5,
            ItemType.AffectionRecoveryNormal => 0.2,
            ItemType.AffectionRecoverySmall  => 0.04,
            _ => 0
        };

        public static double GetBaseKarmaChange(this ItemType type) => type switch
        {
            ItemType.AffectionRecoveryGreat  => 0.24,
            ItemType.AffectionRecoveryBig    => 0.08,
            ItemType.AffectionRecoveryNormal => 0.008,
            ItemType.AffectionRecoverySmall  => 0.001,
            ItemType.RemoveCurse             => 1,
            _ => 0
        };

        public static bool CanBeUsedWithNormalUseCommand(this ItemType type)
        {
            switch (type)
            {
                case ItemType.LotteryTicket:
                case ItemType.CardFragment:
                case ItemType.CreationItemBase:
                case ItemType.NotAnItem:
                    return false;

                default:
                    return true;
            }
        }

        public static bool IsFigureNeededToUse(this ItemType type)
        {
            switch (type)
            {
                case ItemType.GiveTagSlot:
                    return false;

                default:
                    return true;
            }
        }

        public static bool CanUseWithCard(this ItemType type) => !type.CanUseWithoutCard(false);

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
                case ItemType.GiveTagSlot:
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
                case ItemType.CardFragment:
                case ItemType.BloodOfYourWaifu:
                case ItemType.BetterIncreaseUpgradeCnt:
                case ItemType.GiveTagSlot:
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
                    return toExp;

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

        public static bool IsProtected(this ItemType type)
        {
            switch (type)
            {
                case ItemType.CheckAffection:
                case ItemType.SetCustomImage:
                case ItemType.ChangeStarType:
                case ItemType.SetCustomBorder:
                case ItemType.ChangeCardImage:
                case ItemType.ResetCardValue:
                case ItemType.LotteryTicket:
                case ItemType.IncreaseUltimateAttack:
                case ItemType.IncreaseUltimateDefence:
                case ItemType.IncreaseUltimateHealth:
                case ItemType.IncreaseUltimateAll:
                case ItemType.SetCustomAnimatedImage:
                case ItemType.PreAssembledMegumin:
                case ItemType.PreAssembledGintoki:
                case ItemType.PreAssembledAsuna:
                case ItemType.FigureSkeleton:
                case ItemType.CardFragment:
                case ItemType.RemoveCurse:
                case ItemType.CreationItemBase:
                case ItemType.CheckCurse:
                case ItemType.GiveTagSlot:
                case ItemType.NotAnItem:
                    return true;

                default:
                    return false;
            }
        }

        public static long CValue(this ItemType type) => type switch
        {
            ItemType.AffectionRecoveryGreat   => 64,
            ItemType.AffectionRecoveryBig     => 42,
            ItemType.AffectionRecoveryNormal  => 16,
            ItemType.AffectionRecoverySmall   => 3,
            ItemType.BetterIncreaseUpgradeCnt => 401,
            ItemType.BloodOfYourWaifu         => 401,
            ItemType.IncreaseUpgradeCnt       => 111,
            ItemType.DereReRoll               => 32,
            ItemType.CardParamsReRoll         => 32,
            ItemType.CheckAffection           => 52,
            ItemType.IncreaseExpSmall         => 82,
            ItemType.IncreaseExpBig           => 321,
            ItemType.ChangeStarType           => 51,
            ItemType.SetCustomBorder          => 81,
            ItemType.ChangeCardImage          => 11,
            ItemType.ResetCardValue           => 8,
            ItemType.LotteryTicket            => 205,
            ItemType.IncreaseUltimateAttack   => 61,
            ItemType.IncreaseUltimateDefence  => 61,
            ItemType.IncreaseUltimateHealth   => 71,
            ItemType.IncreaseUltimateAll      => 205,
            ItemType.SetCustomImage           => 666,
            ItemType.SetCustomAnimatedImage   => 3251,
            ItemType.RemoveCurse              => 802,
            ItemType.GiveTagSlot              => 1151,
            ItemType.CreationItemBase         => 222,
            ItemType.CheckCurse               => 82,
            ItemType.CardFragment             => 0,
            _ => 1
        };

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

        public static Figure ToFigure(this Item item, Card card, DateTime creationTime)
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
                Character = card.Character,
                BodyQuality = Quality.Broken,
                RestartCnt = card.RestartCnt,
                HeadQuality = Quality.Broken,
                PAS = PreAssembledFigure.None,
                CompletionDate = creationTime,
                FocusedPart = FigurePart.Head,
                SkeletonQuality = item.Quality,
                ClothesQuality = Quality.Broken,
                LeftArmQuality = Quality.Broken,
                LeftLegQuality = Quality.Broken,
                RightArmQuality = Quality.Broken,
                RightLegQuality = Quality.Broken,
                Health = card.Health + card.HealthBonus,
                Attack = card.Attack + card.AttackBonus,
                Defence = card.Defence + card.DefenceBonus,
            };
        }

        public static Figure ToPAFigure(this ItemType type, DateTime creationTime)
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
                CompletionDate = creationTime,
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

        public static List<string> ToItemList(this IEnumerable<Item> list, string filter)
        {
            var filterDisabled = string.IsNullOrEmpty(filter);
            var items = new List<string>();
            var index = 0;

            foreach(var item in list)
            {
                index++;

                if (filterDisabled || item.Name.Contains(filter, StringComparison.InvariantCultureIgnoreCase))
                    items.Add($"**[{index}]** {item.Name} x{item.Count}");
            }

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

        public static string ToName(this ActionAfterExpedition action) => action switch
        {
            ActionAfterExpedition.Destroy => "zniszcz",
            ActionAfterExpedition.Release => "uwolnij",
            _ => "nic"
        };
    }
}
