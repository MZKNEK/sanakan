#pragma warning disable 1591

using System;
using System.Collections.Generic;

namespace Sanakan.Database.Models.Analytics
{
    // one finished card exchange; unlike the other telemetry it names the players (for moderation)
    // and stays when they leave
    public class CardExchange
    {
        public ulong Id { get; set; }
        public DateTime Date { get; set; }
        // the player who proposed the exchange
        public ulong User1Id { get; set; }
        public ulong User2Id { get; set; }
        public int User1Gave { get; set; }
        public int User2Gave { get; set; }
        // the bot's exchange message
        public ulong? MessageId { get; set; }
        // read back from an old exchange message; an offer of more than 12 cards has no cards listed
        public bool Imported { get; set; }

        public virtual ICollection<CardExchangeCard> Cards { get; set; } = new List<CardExchangeCard>();
    }

    // a card that changed hands in an exchange; the card's id stays even after the card is gone
    public class CardExchangeCard
    {
        public ulong ExchangeId { get; set; }
        public ulong CardId { get; set; }
        public ulong FromUserId { get; set; }
        public ulong ToUserId { get; set; }

        public virtual CardExchange Exchange { get; set; }
    }

    // the last name a player was seen with, for players the bot no longer sees (e.g. who left the server)
    public class PlayerName
    {
        public ulong UserId { get; set; }
        public string Name { get; set; }
        public DateTime SeenAt { get; set; }
    }

    // SC given with podarujsc: Sent leaves the giver, Received (after the tax) reaches the recipient
    public class ScGift
    {
        public ulong Id { get; set; }
        public DateTime Date { get; set; }
        public ulong FromUserId { get; set; }
        public ulong ToUserId { get; set; }
        public long Sent { get; set; }
        public long Received { get; set; }
    }
}
