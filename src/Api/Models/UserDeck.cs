#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.Linq;
using Sanakan.Database.Models;
using Sanakan.Extensions;

namespace Sanakan.Api.Models
{
    /// <summary>
    /// Aktywna talia użytkownika
    /// </summary>
    public class UserDeck
    {
        /// <summary>
        /// Aktywne karty
        /// </summary>
        public List<UserDeckCard> Cards { get; set; }
        /// <summary>
        /// Moc talii
        /// </summary>
        public double Power { get; set; }
        /// <summary>
        /// Minimalna moc talii wymagana do PvP
        /// </summary>
        public double MinPower { get; set; }
        /// <summary>
        /// Maksymalna moc talii dozwolona w PvP
        /// </summary>
        public double MaxPower { get; set; }
        /// <summary>
        /// Maksymalna liczba aktywnych kart w PvP
        /// </summary>
        public int MaxCards { get; set; }
        /// <summary>
        /// Stan talii względem wymagań PvP
        /// </summary>
        public DeckPowerStatus PvpDeckStatus { get; set; }
        /// <summary>
        /// Pojedynki rozegrane dzisiaj
        /// </summary>
        public long PvpGamesToday { get; set; }
        /// <summary>
        /// Dzienny limit pojedynków
        /// </summary>
        public int PvpGamesMax { get; set; }
        /// <summary>
        /// Czy można teraz rozegrać pojedynek PvP (talia poprawna i limit nie wyczerpany)
        /// </summary>
        public bool CanPlayPvp { get; set; }

        public static UserDeck From(User user, DateTime now)
        {
            var deck = user.GameDeck;
            var active = deck.Cards.Where(x => x.Active).ToList();
            var power = active.Sum(x => x.CalculateCardPower());

            var pvp = user.TimeStatuses?.FirstOrDefault(x => x.Type == StatusType.Pvp);
            var gamesToday = pvp != null && pvp.IsActive(now) ? (long)deck.PVPDailyGamesPlayed : 0;

            var status = DeckPowerStatus.Ok;
            if (active.Count > UserExtension.MAX_CARDS_IN_DECK) status = DeckPowerStatus.TooManyCards;
            else if (active.Count < 1) status = DeckPowerStatus.NotEnoughtCards;
            else if (power > deck.GetMaxDeckPower()) status = DeckPowerStatus.TooHigh;
            else if (power < deck.GetMinDeckPower()) status = DeckPowerStatus.TooLow;

            return new UserDeck
            {
                Cards = active.Select(UserDeckCard.From).ToList(),
                Power = power,
                MinPower = deck.GetMinDeckPower(),
                MaxPower = deck.GetMaxDeckPower(),
                MaxCards = UserExtension.MAX_CARDS_IN_DECK,
                PvpDeckStatus = status,
                PvpGamesToday = gamesToday,
                PvpGamesMax = UserExtension.MAX_PVP_DAILY_GAMES,
                CanPlayPvp = status == DeckPowerStatus.Ok && gamesToday < UserExtension.MAX_PVP_DAILY_GAMES,
            };
        }
    }

    /// <summary>
    /// Karta w talii
    /// </summary>
    public class UserDeckCard
    {
        /// <summary>
        /// WID karty
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
        /// Jakość karty
        /// </summary>
        public Rarity Rarity { get; set; }
        /// <summary>
        /// Charakter karty
        /// </summary>
        public Dere Dere { get; set; }
        /// <summary>
        /// Atak z bonusami
        /// </summary>
        public int Attack { get; set; }
        /// <summary>
        /// Obrona z bonusami
        /// </summary>
        public int Defence { get; set; }
        /// <summary>
        /// Życie z bonusami
        /// </summary>
        public int Health { get; set; }
        /// <summary>
        /// Moc karty
        /// </summary>
        public double Power { get; set; }
        /// <summary>
        /// Czy karta jest kartą ultimate
        /// </summary>
        public bool Ultimate { get; set; }

        public static UserDeckCard From(Card card) => new UserDeckCard
        {
            Id = card.Id,
            Name = card.Name,
            Title = card.Title,
            Rarity = card.Rarity,
            Dere = card.Dere,
            Attack = card.GetAttackWithBonus(),
            Defence = card.GetDefenceWithBonus(),
            Health = card.GetHealthWithPenalty(),
            Power = card.CalculateCardPower(),
            Ultimate = card.FromFigure,
        };
    }
}
