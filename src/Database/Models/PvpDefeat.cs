#pragma warning disable 1591

using System;

namespace Sanakan.Database.Models
{
    // a duel lost as the defender, the defender may take revenge on one attacker of the day
    public class PvpDefeat
    {
        public ulong Id { get; set; }
        public ulong UserId { get; set; }
        public ulong AttackerId { get; set; }
        public DateTime Date { get; set; }
        public bool Avenged { get; set; }
    }
}
