#pragma warning disable 1591

using System;

namespace Sanakan.Database.Models.Analytics
{
    public enum LotteryOutcome
    {
        Won = 0,
        NoParticipants = 1,
        // everyone who joined was muted, blacklisted or without a bot profile
        NoEligibleWinner = 2,
        // the giver had no cards left when the winner was drawn
        NoCards = 3,
    }

    // one card lottery (dev rozdaj); names the winner and is not removed
    public class LotteryResult
    {
        public ulong Id { get; set; }
        public DateTime Date { get; set; }
        public ulong GuildId { get; set; }
        public ulong ChannelId { get; set; }
        public ulong GiverId { get; set; }
        // the cards announced in the lottery
        public int Prize { get; set; }
        // players (not bots) who reacted before the end
        public int Participants { get; set; }
        public ulong? WinnerId { get; set; }
        public int CardsWon { get; set; }
        public LotteryOutcome Outcome { get; set; }
    }
}
