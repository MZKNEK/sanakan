#pragma warning disable 1591

using System;

namespace Sanakan.Database.Models.Analytics
{
    // one value per day, metric and label, e.g. (2026-11-13, currency_out, SC:sklepik, 4000); see GameTelemetry
    public class GameStat
    {
        public DateTime Date { get; set; }
        public string Metric { get; set; }
        public string Label { get; set; }
        public long Value { get; set; }
    }

    // a player who used the bot that day; kept for a year
    public class PlayerDay
    {
        public DateTime Date { get; set; }
        public ulong UserId { get; set; }
        public int Commands { get; set; }
        // the bot's account was created that day
        public bool IsNew { get; set; }
    }

    public enum DuelOutcome
    {
        AttackerWon = 0,
        DefenderWon = 1,
        Draw = 2,
    }

    // one PvP duel as it stood before it, without the players; kept for a year
    public class DuelResult
    {
        public ulong Id { get; set; }
        public DateTime Date { get; set; }
        public DuelOutcome Outcome { get; set; }
        public double AttackerWinChance { get; set; }
        public double AttackerPower { get; set; }
        public double DefenderPower { get; set; }
        public long AttackerRank { get; set; }
        public long DefenderRank { get; set; }
        public double AttackerMmr { get; set; }
        public double DefenderMmr { get; set; }
        // the active cards' rarities, best first, e.g. "SSS,SS,S"
        public string AttackerCards { get; set; }
        public string DefenderCards { get; set; }
        public int Rounds { get; set; }
    }
}
