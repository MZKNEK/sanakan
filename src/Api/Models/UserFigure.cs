#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.Linq;
using Sanakan.Database.Models;
using Sanakan.Extensions;

namespace Sanakan.Api.Models
{
    /// <summary>
    /// Figurka użytkownika
    /// </summary>
    public class UserFigure
    {
        private static readonly FigurePart[] _parts = new[]
        {
            FigurePart.Head, FigurePart.Body, FigurePart.LeftArm, FigurePart.RightArm,
            FigurePart.LeftLeg, FigurePart.RightLeg, FigurePart.Clothes,
        };

        /// <summary>
        /// Id figurki
        /// </summary>
        public ulong Id { get; set; }
        /// <summary>
        /// Nazwa postaci
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// Tytuł
        /// </summary>
        public string Title { get; set; }
        /// <summary>
        /// Id postaci na shinden
        /// </summary>
        public ulong CharacterId { get; set; }
        /// <summary>
        /// Charakter
        /// </summary>
        public Dere Dere { get; set; }
        /// <summary>
        /// Atak
        /// </summary>
        public int Attack { get; set; }
        /// <summary>
        /// Obrona
        /// </summary>
        public int Defence { get; set; }
        /// <summary>
        /// Życie
        /// </summary>
        public int Health { get; set; }
        /// <summary>
        /// Czy figurka jest aktywna (otrzymuje doświadczenie)
        /// </summary>
        public bool IsActive { get; set; }
        /// <summary>
        /// Czy figurka została zamieniona w kartę ultimate
        /// </summary>
        public bool IsComplete { get; set; }
        /// <summary>
        /// WID utworzonej karty ultimate (null - jeszcze nie utworzona)
        /// </summary>
        public ulong? CreatedCardId { get; set; }
        /// <summary>
        /// Data ukończenia (null - nieukończona)
        /// </summary>
        public DateTime? CompletionDate { get; set; }
        /// <summary>
        /// Jakość szkieletu (null - brak szkieletu)
        /// </summary>
        public Quality? SkeletonQuality { get; set; }
        /// <summary>
        /// Jakość zamontowanych części (null - część niezamontowana)
        /// </summary>
        public Dictionary<FigurePart, Quality?> Parts { get; set; }
        /// <summary>
        /// Część, do której trafiają punkty konstrukcji
        /// </summary>
        public FigurePart FocusedPart { get; set; }
        /// <summary>
        /// Punkty konstrukcji aktywnej części
        /// </summary>
        public double PartExp { get; set; }
        /// <summary>
        /// Doświadczenie figurki
        /// </summary>
        public double Exp { get; set; }
        /// <summary>
        /// Liczba restartów
        /// </summary>
        public int RestartCount { get; set; }
        /// <summary>
        /// Czy wszystkie części są zamontowane
        /// </summary>
        public bool AllPartsInstalled { get; set; }
        /// <summary>
        /// Czy można już utworzyć kartę ultimate
        /// </summary>
        public bool CanCreateUltimateCard { get; set; }

        public static UserFigure From(Figure fig) => new UserFigure
        {
            Id = fig.Id,
            Name = fig.Name,
            Title = fig.Title,
            CharacterId = fig.Character,
            Dere = fig.Dere,
            Attack = fig.Attack,
            Defence = fig.Defence,
            Health = fig.Health,
            IsActive = fig.IsFocus,
            IsComplete = fig.IsComplete,
            CreatedCardId = fig.IsComplete && fig.CreatedCardId != 0 ? fig.CreatedCardId : null,
            CompletionDate = fig.IsComplete ? fig.CompletionDate : null,
            SkeletonQuality = OrNull(fig.SkeletonQuality),
            Parts = _parts.ToDictionary(x => x, x => OrNull(fig.GetQualityOfPart(x))),
            FocusedPart = fig.FocusedPart,
            PartExp = fig.PartExp,
            Exp = fig.ExpCnt,
            RestartCount = fig.RestartCnt,
            AllPartsInstalled = fig.AllPartsInstalled(),
            CanCreateUltimateCard = !fig.IsComplete && fig.CanCreateUltimateCard(),
        };

        private static Quality? OrNull(Quality quality) => quality == Quality.Broken ? null : quality;
    }
}
