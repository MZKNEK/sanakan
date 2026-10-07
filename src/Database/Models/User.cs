#pragma warning disable 1591

using System;
using System.Collections.Generic;

namespace Sanakan.Database.Models
{
    public enum ProfileType
    {
        Stats = 0,
        Img = 1,
        StatsWithImg = 2,
        Cards = 3,
        CardsOnImg = 4,
        StatsOnImg = 5,
        MiniGallery = 6,
        MiniGalleryOnImg = 7
    }

    public enum CharacterPoolType
    {
        Anime = 0,
        Manga = 1,
        All = 2
    }

    public enum AvatarBorder
    {
        None = 0,
        PurpleLeaves = 1,
        Dzedai = 2,
        Base = 3,
        Water = 4,
        Crows = 5,
        Bow = 6,
        Metal = 7,
        RedThinLeaves = 8,
        Skull = 9,
        Fire = 10,
        Promium = 11,
        Ice = 12,
        Gold = 13,
        Red = 14,
        Rainbow = 15,
        Pink = 16,
        Simple = 17,
        TurqLeaves = 18
    }

    [Flags]
    public enum ProfileSettings
    {
        None            = 0,
        ShowAnime       = 1,
        ShowManga       = 2,
        ShowCards       = 4,
        Flip            = 8,
        HalfGallery     = 16,
        ShowGallery     = 32,
        ShowWaifu       = 64,
        BarOnTop        = 128,
        BorderColor     = 256,
        RoundAvatar     = 512,
        BarOpacity      = 1024,
        ShowOverlay     = 2048,
        ShowOverlayPro  = 4096,

        Default = ShowAnime | ShowManga | ShowCards | ShowGallery
            | ShowOverlay | ShowOverlayPro | BorderColor,
    }

    public class User
    {
        public ulong Id { get; set; }
        public ulong Shinden { get; set; }
        public bool IsBlacklisted { get; set; }
        public long AcCnt { get; set; }
        public long TcCnt { get; set; }
        public long ScCnt { get; set; }
        public long Level { get; set; }
        public long ExpCnt { get; set; }
        public ProfileType ProfileType { get; set; }
        public string BackgroundProfileUri { get; set; }
        public string StatsReplacementProfileUri { get; set; }
        public ulong MessagesCnt { get; set; }
        public ulong CommandsCnt { get; set; }
        public DateTime MeasureDate { get; set; }
        public ulong MessagesCntAtDate { get; set; }
        public ulong CharacterCntFromDate { get; set; }
        public long Warnings { get; set; }
        public CharacterPoolType PoolType { get; set; }
        public AvatarBorder AvatarBorder { get; set; }
        public ProfileSettings StatsStyleSettings { get; set; }
        public string CustomProfileOverlayUrl { get; set; }
        public string PremiumCustomProfileOverlayUrl { get; set; }
        public float ProfileShadowsOpacity { get; set; }

        public virtual UserStats Stats { get; set; }
        public virtual GameDeck GameDeck { get; set; }
        public virtual SlotMachineConfig SMConfig { get; set; }

        public virtual ICollection<TimeStatus> TimeStatuses { get; set; }
    }
}
