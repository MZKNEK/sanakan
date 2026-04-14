#pragma warning disable 1591

using System.Collections.Generic;
using System.Linq;
using Sanakan.Database.Models;

namespace Sanakan.Api.Models
{
    public enum OrderType
    {
        Id, IdDes, Name, NameDes, Rarity, RarityDes, Title, TitleDes, Health, HealthDes, HealthBase, HealthBaseDes,
        Atack, AtackDes, Defence, DefenceDes, Exp, ExpDes, Dere, DereDes, Picture, PictureDes, Relation, RelationDes,
        CardPower, CardPowerDes, WhoWantsCount, WhoWantsCountDes, Blocked, BlockedDes, Curse, CurseDes, Fatigue, FatigueDes,
        Overflow, OverflowDes, ActiveWhoWantsCount, ActiveWhoWantsCountDes, CharacterId, CharacterIdDes
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
        /// W jaki sposów filtrować po tagach
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
                OrderType.Atack => query.OrderBy(x => x.Attack + x.AttackBonus + (x.RestartCnt * 2d)),
                OrderType.AtackDes => query.OrderByDescending(x => x.Attack + x.AttackBonus + (x.RestartCnt * 2d)),
                OrderType.Exp => query.OrderBy(x => x.ExpCnt),
                OrderType.ExpDes => query.OrderByDescending(x => x.ExpCnt),
                OrderType.Dere => query.OrderBy(x => x.Dere),
                OrderType.DereDes => query.OrderByDescending(x => x.Dere),
                OrderType.Defence => query.OrderBy(x => x.Defence + x.DefenceBonus + x.RestartCnt),
                OrderType.DefenceDes => query.OrderByDescending(x => x.Defence + x.DefenceBonus + x.RestartCnt),
                OrderType.Health => query.OrderBy(x => x.Health + ((x.Health * (x.Affection * 5d / 100d)) + x.HealthBonus)),
                OrderType.HealthDes => query.OrderByDescending(x => x.Health + ((x.Health * (x.Affection * 5d / 100d)) + x.HealthBonus)),
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
                _ => query.OrderBy(x => x.Id)
            };
            return orderedQuery.ThenBy(x => x.Id);
        }
    }
}