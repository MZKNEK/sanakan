#pragma warning disable 1591

namespace Sanakan.Services.PocketWaifu.Fight
{
    public class AttackInfo
    {
        public double Dmg { get; set; }
        public ulong AtkCardId { get; set; }
        public ulong DefCardId { get; set; }
        public bool Critical { get; set; }
        public bool Dodged { get; set; }
        public bool SecondStrike { get; set; }
        public bool Healed { get; set; }
        public bool Rage { get; set; }
        public bool Guarded { get; set; }
        public double TargetHp { get; set; }
    }
}
