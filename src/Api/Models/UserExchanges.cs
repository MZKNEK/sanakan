#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.Linq;
using Sanakan.Database.Models;
using Sanakan.Database.Models.Analytics;

namespace Sanakan.Api.Models
{
    /// <summary>
    /// Historia wymian kart użytkownika, liczona z jego strony (oddane / otrzymane)
    /// </summary>
    public class UserExchanges
    {
        /// <summary>
        /// Użytkownik
        /// </summary>
        public ExchangePlayer User { get; set; }
        /// <summary>
        /// Liczba wszystkich wymian
        /// </summary>
        public int TotalExchanges { get; set; }
        /// <summary>
        /// Łączna liczba oddanych kart
        /// </summary>
        public long CardsGiven { get; set; }
        /// <summary>
        /// Łączna liczba otrzymanych kart
        /// </summary>
        public long CardsReceived { get; set; }
        /// <summary>
        /// Wszyscy partnerzy wymian, od najczęstszego
        /// </summary>
        public List<ExchangePartner> Partners { get; set; }
        /// <summary>
        /// Wymiany z wybranej strony, od najnowszej
        /// </summary>
        public List<UserExchange> Exchanges { get; set; }

        public static UserExchanges From(ulong userId, IReadOnlyCollection<CardExchange> all, IEnumerable<CardExchange> page,
            IReadOnlyDictionary<ulong, Card> cards, Func<ulong, ExchangePlayer> player)
        {
            var mine = all.Select(x => Side.Of(x, userId)).ToList();
            return new UserExchanges
            {
                User = player(userId),
                TotalExchanges = mine.Count,
                CardsGiven = mine.Sum(x => (long)x.Given),
                CardsReceived = mine.Sum(x => (long)x.Received),
                Partners = mine.GroupBy(x => x.Partner).Select(g => new ExchangePartner
                {
                    Player = player(g.Key),
                    Exchanges = g.Count(),
                    CardsGiven = g.Sum(x => (long)x.Given),
                    CardsReceived = g.Sum(x => (long)x.Received),
                    LastExchange = g.Max(x => x.Date),
                }).OrderByDescending(x => x.Exchanges).ThenByDescending(x => x.LastExchange).ToList(),
                Exchanges = page.Select(x => UserExchange.From(x, userId, cards, player)).ToList(),
            };
        }

        private readonly struct Side
        {
            public ulong Partner { get; init; }
            public int Given { get; init; }
            public int Received { get; init; }
            public DateTime Date { get; init; }

            public static Side Of(CardExchange x, ulong userId) => x.User1Id == userId
                ? new Side { Partner = x.User2Id, Given = x.User1Gave, Received = x.User2Gave, Date = x.Date }
                : new Side { Partner = x.User1Id, Given = x.User2Gave, Received = x.User1Gave, Date = x.Date };
        }
    }

    /// <summary>
    /// Gracz biorący udział w wymianie
    /// </summary>
    public class ExchangePlayer
    {
        /// <summary>
        /// Id na discordzie
        /// </summary>
        public string DiscordId { get; set; }
        /// <summary>
        /// Nick na discordzie (null, gdy bot go nie zna, np. gracz opuścił serwer)
        /// </summary>
        public string Nick { get; set; }
        /// <summary>
        /// Id konta na Shindenie (null, gdy konto nie jest połączone lub gracz nie ma już profilu bota)
        /// </summary>
        public string ShindenId { get; set; }
        /// <summary>
        /// Nick na Shindenie (null, gdy nie udało się go pobrać)
        /// </summary>
        public string ShindenNick { get; set; }
    }

    /// <summary>
    /// Partner wymian z podsumowaniem wszystkich wymian z nim
    /// </summary>
    public class ExchangePartner
    {
        /// <summary>
        /// Partner
        /// </summary>
        public ExchangePlayer Player { get; set; }
        /// <summary>
        /// Liczba wymian z partnerem
        /// </summary>
        public int Exchanges { get; set; }
        /// <summary>
        /// Karty oddane partnerowi
        /// </summary>
        public long CardsGiven { get; set; }
        /// <summary>
        /// Karty otrzymane od partnera
        /// </summary>
        public long CardsReceived { get; set; }
        /// <summary>
        /// Data ostatniej wymiany
        /// </summary>
        public DateTime LastExchange { get; set; }
    }

    /// <summary>
    /// Jedna wymiana
    /// </summary>
    public class UserExchange
    {
        /// <summary>
        /// Id wymiany
        /// </summary>
        public ulong Id { get; set; }
        /// <summary>
        /// Data wymiany
        /// </summary>
        public DateTime Date { get; set; }
        /// <summary>
        /// Czy użytkownik zaproponował wymianę
        /// </summary>
        public bool Proposed { get; set; }
        /// <summary>
        /// Partner wymiany
        /// </summary>
        public ExchangePlayer Partner { get; set; }
        /// <summary>
        /// Liczba oddanych kart
        /// </summary>
        public int GivenCount { get; set; }
        /// <summary>
        /// Liczba otrzymanych kart
        /// </summary>
        public int ReceivedCount { get; set; }
        /// <summary>
        /// Oddane karty
        /// </summary>
        public List<ExchangedCard> Given { get; set; }
        /// <summary>
        /// Otrzymane karty
        /// </summary>
        public List<ExchangedCard> Received { get; set; }

        public static UserExchange From(CardExchange x, ulong userId, IReadOnlyDictionary<ulong, Card> cards, Func<ulong, ExchangePlayer> player)
        {
            var proposed = x.User1Id == userId;
            var partner = proposed ? x.User2Id : x.User1Id;
            var moved = x.Cards ?? new List<CardExchangeCard>();
            return new UserExchange
            {
                Id = x.Id,
                Date = x.Date,
                Proposed = proposed,
                Partner = player(partner),
                GivenCount = proposed ? x.User1Gave : x.User2Gave,
                ReceivedCount = proposed ? x.User2Gave : x.User1Gave,
                Given = moved.Where(c => c.FromUserId == userId).OrderBy(c => c.CardId).Select(c => ExchangedCard.From(c.CardId, cards)).ToList(),
                Received = moved.Where(c => c.ToUserId == userId).OrderBy(c => c.CardId).Select(c => ExchangedCard.From(c.CardId, cards)).ToList(),
            };
        }
    }

    /// <summary>
    /// Karta z wymiany, opis według jej obecnego stanu
    /// </summary>
    public class ExchangedCard
    {
        /// <summary>
        /// WID karty
        /// </summary>
        public ulong Id { get; set; }
        /// <summary>
        /// Czy karta nadal istnieje (false, gdy została zniszczona lub uwolniona)
        /// </summary>
        public bool Exists { get; set; }
        /// <summary>
        /// Nazwa postaci (null, gdy karta nie istnieje)
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// Obecna jakość karty (null, gdy karta nie istnieje)
        /// </summary>
        public Rarity? Rarity { get; set; }
        /// <summary>
        /// Id obecnego właściciela na discordzie (null, gdy karta nie istnieje)
        /// </summary>
        public string OwnerId { get; set; }

        public static ExchangedCard From(ulong id, IReadOnlyDictionary<ulong, Card> cards)
            => cards.TryGetValue(id, out var card)
                ? new ExchangedCard { Id = id, Exists = true, Name = card.Name, Rarity = card.Rarity, OwnerId = card.GameDeckId.ToString() }
                : new ExchangedCard { Id = id };
    }
}
