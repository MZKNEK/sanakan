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
        None, PurpleLeaves, Dzedai, Base, Water, Crows, Bow
    }

    public enum ProfileVersion
    {
        BarOnTop, BarOnBottom
    }

    [Flags]
    public enum StatsSettings
    {
        None        = 0,
        ShowAnime   = 1,
        ShowManga   = 2,
        ShowCards   = 4,
        Flip        = 8,
        HalfGallery = 16,
        ShowGallery = 32,
        ShowAll     = ShowAnime | ShowManga | ShowCards | ShowGallery,
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
        public bool ShowWaifuInProfile { get; set; }
        public long Warnings { get; set; }
        public CharacterPoolType PoolType { get; set; }
        public ProfileVersion ProfileVersion { get; set; }
        public AvatarBorder AvatarBorder { get; set; }
        public StatsSettings StatsStyleSettings { get; set; }
        public string CustomProfileOverlayUrl { get; set; }

        public virtual UserStats Stats { get; set; }
        public virtual GameDeck GameDeck { get; set; }
        public virtual SlotMachineConfig SMConfig { get; set; }

        public virtual ICollection<TimeStatus> TimeStatuses { get; set; }
    }
}
