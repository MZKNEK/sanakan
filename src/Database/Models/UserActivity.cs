#pragma warning disable 1591

using System;

namespace Sanakan.Database.Models
{
    public enum ActivityType
    {
        LevelUp = 0,
        Muted = 1,
        Banned = 2,
        Kicked = 3,
        Connected = 4,
        LotteryStarted = 5,
        WonLottery = 6,
        AcquiredCardSSS = 7,
        AcquiredCardKC = 8,
        AcquiredCardWishlist = 9,
        AcquiredCarcUltimate = 10,
        UsedScalpel = 11,
        CreatedYato = 12,
        CreatedYami = 13,
        CreatedRaito = 14,
        CreatedSSS = 15,
        CreatedUltiamte = 16,
        AddedToWishlistCharacter = 17,
        AddedToWishlistTitle = 18,
        AddedToWishlistCard = 19,
        AcquiredCardHighKC = 20
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