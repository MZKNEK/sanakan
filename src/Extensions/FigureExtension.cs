#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.Linq;
using Discord;
using Discord.WebSocket;
using Sanakan.Database.Models;

namespace Sanakan.Extensions
{
    public static class FigureExtension
    {
        public static string ToName(this Quality q, string broken = "")
        {
            switch (q)
            {
                case Quality.Alpha:   return "α";
                case Quality.Beta:    return "β";
                case Quality.Gamma:   return "γ";
                case Quality.Delta:   return "Δ";
                case Quality.Epsilon: return "ε";
                case Quality.Zeta:    return "ζ";
                case Quality.Theta:   return "Θ";
                case Quality.Jota:    return "ι";
                case Quality.Lambda:  return "λ";
                case Quality.Sigma:   return "Σ";
                case Quality.Omega:   return "Ω";

                default:
                case Quality.Broken:
                    return broken;
            }
        }

        public static string GetStatus(this Figure f)
        {
            if (f.IsComplete) return "🎖️";
            return "➖";
        }

        public static double ToValue(this Quality q) => 0.73 + ((int) q * 1.47);

        public static string ToName(this FigurePart p)
        {
            switch (p)
            {
                case FigurePart.Body:     return "Tułów";
                case FigurePart.Clothes:  return "Ciuchy";
                case FigurePart.Head:     return "Głowa";
                case FigurePart.LeftArm:  return "Lewa ręka";
                case FigurePart.LeftLeg:  return "Lewa noga";
                case FigurePart.RightArm: return "Prawa ręka";
                case FigurePart.RightLeg: return "Prawa noga";
                case FigurePart.All:      return "Uniwersalna";

                default:
                case FigurePart.None:
                    return "brak";
            }
        }

        public static ulong GetCharacterId(this PreAssembledFigure pas)
        {
            switch (pas)
            {
                case PreAssembledFigure.Asuna:   return 45276;
                case PreAssembledFigure.Gintoki: return 663;
                case PreAssembledFigure.Megumin: return 72013;

                default:
                    return 0;
            }
        }

        public static string GetCharacterName(this PreAssembledFigure pas)
        {
            switch (pas)
            {
                case PreAssembledFigure.Asuna:   return "Asuna Yuuki";
                case PreAssembledFigure.Gintoki: return "Gintoki Sakata";
                case PreAssembledFigure.Megumin: return "Megumin";

                default:
                    return "";
            }
        }

        public static string GetTitleName(this PreAssembledFigure pas)
        {
            switch (pas)
            {
                case PreAssembledFigure.Asuna:   return "Sword Art Online";
                case PreAssembledFigure.Gintoki: return "Gintama";
                case PreAssembledFigure.Megumin: return "Kono Subarashii Sekai ni Shukufuku wo!";

                default:
                    return "";
            }
        }

        public static bool CanCreateUltimateCard(this Figure figure) =>
            figure.AllPartsInstalled() && figure.ExpCnt >= CardExtension.ExpToUpgrade(Rarity.SSS, true, figure.SkeletonQuality);

        public static bool AllPartsInstalled(this Figure figure)
        {
            bool canCreate = true;
            canCreate &= figure.SkeletonQuality != Quality.Broken;
            canCreate &= figure.HeadQuality     != Quality.Broken;
            canCreate &= figure.BodyQuality     != Quality.Broken;
            canCreate &= figure.LeftArmQuality  != Quality.Broken;
            canCreate &= figure.RightArmQuality != Quality.Broken;
            canCreate &= figure.LeftLegQuality  != Quality.Broken;
            canCreate &= figure.RightLegQuality != Quality.Broken;
            canCreate &= figure.ClothesQuality  != Quality.Broken;
            return canCreate;
        }

        public static Quality GetAvgQuality(this Figure figure)
        {
            double tavg = ((int)figure.SkeletonQuality) * 10;
            double pavg = (int)figure.HeadQuality;
            pavg += (int)figure.BodyQuality;
            pavg += (int)figure.LeftArmQuality;
            pavg += (int)figure.RightArmQuality;
            pavg += (int)figure.LeftLegQuality;
            pavg += (int)figure.RightLegQuality;
            pavg += (int)figure.ClothesQuality;
            tavg += pavg / 7;

            int rAvg = (int)Math.Floor(tavg / 10);
            var eQ = Quality.Broken;

            foreach (int v in Enum.GetValues(typeof(Quality)))
            {
                if (v > rAvg) break;
                eQ = (Quality)v;
            }

            return eQ;
        }

        public static Card ToCard(this Figure figure, DateTime creationTime)
        {
            var quality = figure.GetAvgQuality();
            var card = new Card
            {
                CustomImageDate = DateTime.MinValue,
                FirstIdOwner = figure.GameDeckId,
                Expedition = CardExpedition.None,
                LastIdOwner = figure.GameDeckId,
                RestartCnt = figure.RestartCnt,
                ExpeditionDate = creationTime,
                PAS = PreAssembledFigure.None,
                Character = figure.Character,
                CreationDate = creationTime,
                StarStyle = StarStyle.Full,
                Source = CardSource.Figure,
                RarityOnStart = Rarity.SSS,
                QualityOnStart = quality,
                Defence = figure.Defence,
                FixedCustomImageCnt = 0,
                IsAnimatedImage = false,
                Health = figure.Health,
                Curse = CardCurse.None,
                Attack = figure.Attack,
                Tags = new List<Tag>(),
                Title = figure.Title,
                FigureId = figure.Id,
                Rarity = Rarity.SSS,
                CustomBorder = null,
                CustomImage = null,
                Name = figure.Name,
                Dere = figure.Dere,
                IsTradable = false,
                Quality = quality,
                FromFigure = true,
                WhoWantsCount = 0,
                DefenceBonus = 0,
                RateNegative = 0,
                RatePositive = 0,
                HealthBonus = 0,
                AttackBonus = 0,
                UpgradesCnt = 0,
                MarketValue = 1,
                EnhanceCnt = 0,
                Unique = false,
                InCage = false,
                Active = false,
                Affection = 0,
                CardPower = 0,
                Image = null,
                ExpCnt = 0,
            };

            _ = card.CalculateCardPower();

            return card;
        }

        public static int ConstructionPointsToInstall(this Figure figure, Item part)
        {
            var pointsFromSkeleton = 50 * (int) figure.SkeletonQuality;
            var basePointsFromPartQuality = part.Quality switch
            {
                Quality.Alpha   => 80,
                Quality.Beta    => 100,
                Quality.Gamma   => 100,
                Quality.Delta   => 120,
                Quality.Epsilon => 120,
                Quality.Zeta    => 120,
                Quality.Theta   => 120,
                Quality.Jota    => 120,
                Quality.Lambda  => 140,
                Quality.Sigma   => 140,
                _ => 160
            };
            return (basePointsFromPartQuality * (int) part.Quality) + pointsFromSkeleton;
        }

        public static Quality GetQualityOfFocusedPart(this Figure figure)
            => figure.GetQualityOfPart(figure.FocusedPart);

        public static Quality GetQualityOfPart(this Figure figure, FigurePart part)
        {
            switch (part)
            {
                case FigurePart.Body:
                    return figure.BodyQuality;
                case FigurePart.Clothes:
                    return figure.ClothesQuality;
                case FigurePart.Head:
                    return figure.HeadQuality;
                case FigurePart.LeftArm:
                    return figure.LeftArmQuality;
                case FigurePart.LeftLeg:
                    return figure.LeftLegQuality;
                case FigurePart.RightArm:
                    return figure.RightArmQuality;
                case FigurePart.RightLeg:
                    return figure.RightLegQuality;

                default:
                    return Quality.Broken;
            }
        }

        public static bool CanAddPart(this Figure fig, Item part)
        {
            return part.Quality >= fig.SkeletonQuality && fig.GetQualityOfFocusedPart() == Quality.Broken;
        }

        public static bool HasEnoughPointsToAddPart(this Figure fig, Item part)
        {
            return fig.PartExp >= fig.ConstructionPointsToInstall(part);
        }

        public static bool AddPart(this Figure figure, Item part)
        {
            if (!figure.CanAddPart(part) || !figure.HasEnoughPointsToAddPart(part))
                return false;

            var partType = part.Type.GetPartType();
            if (partType != figure.FocusedPart && partType != FigurePart.All)
                return false;

            switch (figure.FocusedPart)
            {
                case FigurePart.Body:
                    figure.BodyQuality = part.Quality;
                    break;
                case FigurePart.Clothes:
                    figure.ClothesQuality = part.Quality;
                    break;
                case FigurePart.Head:
                    figure.HeadQuality = part.Quality;
                    break;
                case FigurePart.LeftArm:
                    figure.LeftArmQuality = part.Quality;
                    break;
                case FigurePart.LeftLeg:
                    figure.LeftLegQuality = part.Quality;
                    break;
                case FigurePart.RightArm:
                    figure.RightArmQuality = part.Quality;
                    break;
                case FigurePart.RightLeg:
                    figure.RightLegQuality = part.Quality;
                    break;

                default:
                    return false;
            }

            figure.PartExp = 0;
            return true;
        }

        public static string IsActive(this Figure fig)
        {
            return fig.IsFocus ? "**A**" : "";
        }

        public static List<Embed> GetFiguresList(this GameDeck deck, SocketUser user)
        {
            var pages = new List<Embed>();
            var list = deck.Figures.Select(x => $"{x.GetStatus()} **[{x.Id}]** *{x.SkeletonQuality.ToName("??")}* [{x.Name}]({Shinden.API.Url.GetCharacterURL(x.Character)}) {x.IsActive()}").SplitList(30);

            for (int i = 0; i < list.Count; i++)
            {
                var embed = new EmbedBuilder
                {
                    Color = EMType.Info.Color(),
                    Description = $"{user.Mention} twoje figurki **({i + 1}/{list.Count})**:\n\n{string.Join("\n", list[i]).TrimToLength()}"
                };
                pages.Add(embed.Build());
            }

            return pages;
        }

        public static string GetDesc(this Figure fig)
        {
            var selectedPart = $"**Aktywna część:**\n{fig.FocusedPart.ToName()} *{fig.PartExp:F} pk*\n\n";
            var name =  $"[{fig.Name}]({Shinden.API.Url.GetCharacterURL(fig.Character)})";
            var desc = fig.IsComplete
                ? $"**Ukończona**: {fig.CompletionDate.ToShortDateTime()}\n"
                + $"**WID**: [{fig.CreatedCardId}](https://waifu.sanakan.pl/#/card/{fig.CreatedCardId})"
                : $"*{fig.ExpCnt:F} / {CardExtension.ExpToUpgrade(Rarity.SSS, true, fig.SkeletonQuality)} exp*\n\n"
                + $"{(fig.AllPartsInstalled() ? "" : selectedPart)}"
                + $"**Części:**\n"
                + $"*Głowa*: {fig.HeadQuality.ToName("brak")}\n"
                + $"*Tułów*: {fig.BodyQuality.ToName("brak")}\n"
                + $"*Prawa ręka*: {fig.RightArmQuality.ToName("brak")}\n"
                + $"*Lewa ręka*: {fig.LeftArmQuality.ToName("brak")}\n"
                + $"*Prawa noga*: {fig.RightLegQuality.ToName("brak")}\n"
                + $"*Lewa noga*: {fig.LeftLegQuality.ToName("brak")}\n"
                + $"*Ciuchy*: {fig.ClothesQuality.ToName("brak")}";

            return $"**[{fig.Id}] Figurka {fig.SkeletonQuality.ToName("??")}**\n{name}\n**Aktywna**: {fig.IsFocus.GetYesNo()}\n{desc}";
        }
    }
}
