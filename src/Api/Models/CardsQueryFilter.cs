#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Sanakan.Database.Models;

namespace Sanakan.Api.Models
{
    public enum OrderType
    {
        Id, IdDes, Name, NameDes, Rarity, RarityDes, Title, TitleDes, Health, HealthDes, HealthBase, HealthBaseDes,
        Atack, AtackDes, Defence, DefenceDes, Exp, ExpDes, Dere, DereDes, Picture, PictureDes, Relation, RelationDes,
        CardPower, CardPowerDes, WhoWantsCount, WhoWantsCountDes, Blocked, BlockedDes, Curse, CurseDes, Fatigue, FatigueDes,
        Overflow, OverflowDes, ActiveWhoWantsCount, ActiveWhoWantsCountDes, CharacterId, CharacterIdDes,
        Unique, UniqueDes
    }

    public enum FilterTagsMethodType
    {
        And, Or
    }

    /// <summary>
    /// Filtrowanie listy kart
    /// </summary>
    public class CardsQueryFilter
    {
        /// <summary>
        /// Sortowanie po parametrze
        /// </summary>
        public OrderType OrderBy { get; set; }
        /// <summary>
        /// Tekst wyszukiwania
        /// </summary>
        public string SearchText { get; set; }
        /// <summary>
        /// Tagi jakie ma zawierać karta
        /// </summary>
        public List<TagIdPair> IncludeTags { get; set; }
        /// <summary>
        /// Tagi jakich karta ma nie mieć
        /// </summary>
        public List<TagIdPair> ExcludeTags { get; set; }
        /// <summary>
        /// W jaki sposób filtrować po tagach
        /// </summary>
        public FilterTagsMethodType FilterTagsMethod { get; set; }
        /// <summary>
        /// Lista id kart
        /// </summary>
        public List<ulong> CardIds { get; set; }
        /// <summary>
        /// Lista id postaci
        /// </summary>
        public List<ulong> CharIds { get; set; }

        // the stats as the card shows them (GetAttackWithBonus, GetDefenceWithBonus, GetHealthWithPenalty), written so the
        // database can compute them; 3 restarts make a star and a card has at most 6 * 61 stars. Integer divisions are
        // truncated explicitly, MySQL's / keeps the fraction
        internal static readonly Expression<Func<Card, int>> AttackShown = WithCurseAndCap(x => x.Attack
            + Math.Min(x.RestartCnt, 100) * 4 + Math.Max(0, x.RestartCnt - 100)
            + Math.Min(Math.Min((int)Math.Truncate(x.RestartCnt / 3d), 366), 50) * 20 + Math.Max(0, Math.Min((int)Math.Truncate(x.RestartCnt / 3d), 366) - 50) * 4
            + (x.FromFigure ? x.AttackBonus : (int)Math.Truncate(x.AttackBonus / 3d)), 999, Extensions.CardExtension.MaxUltimateAttack);

        internal static readonly Expression<Func<Card, int>> DefenceShown = WithCurseAndCap(x => x.Defence
            + Math.Min(x.RestartCnt, 100) * 2 + (int)Math.Truncate(Math.Max(0, x.RestartCnt - 100) / 2d)
            + Math.Min(Math.Min((int)Math.Truncate(x.RestartCnt / 3d), 366), 50) * 5 + Math.Max(0, Math.Min((int)Math.Truncate(x.RestartCnt / 3d), 366) - 50)
            + (x.FromFigure ? x.DefenceBonus : 0), 99, Extensions.CardExtension.MaxUltimateDefence);

        internal static readonly Expression<Func<Card, int>> HealthShown = x => Math.Max(Math.Min(x.Health
            + Math.Min((int)Math.Truncate(x.Health * (x.Affection * 5d / 100d)), 2000)
            + (x.FromFigure ? x.HealthBonus : 0), x.FromFigure ? Extensions.CardExtension.MaxUltimateHealth : 999), 10);

        // the lowered stats curse halves the value, then the value is capped like the card does it
        private static Expression<Func<Card, int>> WithCurseAndCap(Expression<Func<Card, int>> value, int normalCap, int ultimateCap)
        {
            var card = value.Parameters[0];
            var raw = value.Body;
            var cursed = Expression.Equal(Expression.Property(card, nameof(Card.Curse)), Expression.Constant(CardCurse.LoweredStats));
            var truncate = typeof(Math).GetMethod(nameof(Math.Truncate), new[] { typeof(double) });
            var half = Expression.Convert(Expression.Call(truncate, Expression.Divide(Expression.Convert(Expression.Multiply(raw, Expression.Constant(5)), typeof(double)), Expression.Constant(10d))), typeof(int));
            var lowered = Expression.Condition(cursed, Expression.Subtract(raw, half), raw);
            var cap = Expression.Condition(Expression.Property(card, nameof(Card.FromFigure)), Expression.Constant(ultimateCap), Expression.Constant(normalCap));
            var min = Expression.Call(typeof(Math).GetMethod(nameof(Math.Min), new[] { typeof(int), typeof(int) }), lowered, cap);
            return Expression.Lambda<Func<Card, int>>(min, card);
        }

        public static IOrderedQueryable<Card> Use(OrderType type, IQueryable<Card> query)
        {
            var orderedQuery = type switch
            {
                OrderType.Overflow => query.OrderBy(x => x.BorderOverflow),
                OrderType.OverflowDes => query.OrderByDescending(x => x.BorderOverflow),
                OrderType.Fatigue => query.OrderBy(x => x.Fatigue),
                OrderType.FatigueDes => query.OrderByDescending(x => x.Fatigue),
                OrderType.Curse => query.OrderBy(x => x.Curse != CardCurse.None ? 1 : 0),
                OrderType.CurseDes => query.OrderByDescending(x => x.Curse != CardCurse.None ? 1 : 0),
                OrderType.Atack => query.OrderBy(AttackShown),
                OrderType.AtackDes => query.OrderByDescending(AttackShown),
                OrderType.Exp => query.OrderBy(x => x.ExpCnt),
                OrderType.ExpDes => query.OrderByDescending(x => x.ExpCnt),
                OrderType.Dere => query.OrderBy(x => x.Dere),
                OrderType.DereDes => query.OrderByDescending(x => x.Dere),
                OrderType.Defence => query.OrderBy(DefenceShown),
                OrderType.DefenceDes => query.OrderByDescending(DefenceShown),
                OrderType.Health => query.OrderBy(HealthShown),
                OrderType.HealthDes => query.OrderByDescending(HealthShown),
                OrderType.HealthBase => query.OrderBy(x => x.Health),
                OrderType.HealthBaseDes => query.OrderByDescending(x => x.Health),
                OrderType.CardPower => query.OrderBy(x => x.CardPower),
                OrderType.CardPowerDes => query.OrderByDescending(x => x.CardPower),
                OrderType.WhoWantsCount => query.OrderBy(x => x.WhoWantsCount).ThenBy(x => x.Character),
                OrderType.WhoWantsCountDes => query.OrderByDescending(x => x.WhoWantsCount).ThenByDescending(x => x.Character),
                OrderType.ActiveWhoWantsCount => query.OrderBy(x => x.AWhoWantsCount).ThenBy(x => x.Character),
                OrderType.ActiveWhoWantsCountDes => query.OrderByDescending(x => x.AWhoWantsCount).ThenByDescending(x => x.Character),
                OrderType.Relation => query.OrderBy(x => x.Affection),
                OrderType.RelationDes => query.OrderByDescending(x => x.Affection),
                OrderType.Title => query.OrderBy(x => x.Title).ThenBy(x => x.Character),
                OrderType.TitleDes => query.OrderByDescending(x => x.Title).ThenByDescending(x => x.Character),
                OrderType.RarityDes => query.OrderBy(x => x.Rarity).ThenByDescending(x => x.Quality).ThenByDescending(x => x.BorderOverflow),
                OrderType.Rarity => query.OrderByDescending(x => x.Rarity).ThenBy(x => x.Quality).ThenBy(x => x.BorderOverflow),
                OrderType.Name => query.OrderBy(x => x.Name).ThenBy(x => x.Character),
                OrderType.NameDes => query.OrderByDescending(x => x.Name).ThenByDescending(x => x.Character),
                OrderType.Picture => query.OrderBy(x => x.CustomImage == null ? (x.Image == null ? 0 : 1) : (x.IsAnimatedImage ? 3 : 2)).ThenBy(x => x.CustomImageDate),
                OrderType.PictureDes => query.OrderByDescending(x => x.CustomImage == null ? (x.Image == null ? 0 : 1) : (x.IsAnimatedImage ? 3 : 2)).ThenByDescending(x => x.CustomImageDate),
                OrderType.Blocked => query.OrderBy(x => x.IsTradable ? 1 : 0),
                OrderType.BlockedDes => query.OrderByDescending(x => x.IsTradable ? 1: 0),
                OrderType.IdDes => query.OrderByDescending(x => x.Id),
                OrderType.CharacterId => query.OrderBy(x => x.Character),
                OrderType.CharacterIdDes => query.OrderByDescending(x => x.Character),
                OrderType.Unique => query.OrderBy(x => x.Unique ? 1 : 0),
                OrderType.UniqueDes => query.OrderByDescending(x => x.Unique ? 1 : 0),
                _ => query.OrderBy(x => x.Id)
            };
            // the tie-breaker follows the last key's direction, so a descending sort can read its index backwards
            var descending = orderedQuery.Expression is System.Linq.Expressions.MethodCallExpression last && last.Method.Name.EndsWith("Descending");
            return descending ? orderedQuery.ThenByDescending(x => x.Id) : orderedQuery.ThenBy(x => x.Id);
        }
    }
}