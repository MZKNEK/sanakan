#pragma warning disable 1591

using System;
using System.Collections.Generic;

namespace Sanakan.Database.Models
{
    public enum ProfileType
    {
        Stats, Img, StatsWithImg, Cards, CardsOnImg, StatsOnImg, MiniGallery, MiniGalleryOnImg
    }

    public enum CharacterPoolType
    {
        Anime, Manga, All
    }

    public enum AvatarBorder
    {
        None,
        PurpleLeaves,
        Dzedai,
        Base,
        Water,
        Crows,
        Bow,
        Metal,
        RedThinLeaves,
        Skull,
        Fire,
        Promium,
        Ice,
        Gold,
        Red,
        Rainbow,
        Pink,
        Simple,
        TurqLeaves
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
