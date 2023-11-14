#pragma warning disable 1591

using System;

namespace Sanakan.Database.Models
{
    public enum ActivityType
    {
        LevelUp, Muted, Banned, Kicked, Connected, LotteryStarted, WonLottery,
        AcquiredCardSSS, AcquiredCardKC, AcquiredCardWishlist, AcquiredCarcUltimate, UsedScalpel,
        CreatedYato, CreatedYami, CreatedRaito, CreatedSSS, CreatedUltiamte,
        AddedToWishlistCharacter, AddedToWishlistTitle, AddedToWishlistCard
    }

    public class UserActivity
    {
        public ulong Id { get; set; }
        public ulong UserId { get; set; }
        public ulong ShindenId { get; set; }
        public ulong TargetId { get; set; }
        public ActivityType Type { get; set; }
        public string Text { get; set; }
        public DateTime Date { get; set; }
        public string Misc { get; set; }
    }
}