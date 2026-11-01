#pragma warning disable 1591

using System;
using System.Collections.Generic;

namespace Sanakan.ShindenCompat.Models
{
    public interface IIndexable
    {
        ulong Id { get; }
    }

    public interface ICharacterInfoShort : IIndexable
    {
        bool HasImage { get; }
        string LastName { get; }
        string FirstName { get; }
        string PictureUrl { get; }
        string CharacterUrl { get; }
    }

    public interface ICharacterInfoTitle : ICharacterInfoShort
    {
        string TitleName { get; }
    }

    public interface ICharacterInfo : ICharacterInfoShort
    {
        string Age { get; }
        Sex Gender { get; }
        string Hips { get; }
        string Bust { get; }
        bool IsReal { get; }
        string Waist { get; }
        string Height { get; }
        string Weight { get; }
        string Bloodtype { get; }
        List<IPicture> Pictures { get; }
        List<IEditPoints> Points { get; }
        List<IRelation> Relations { get; }
        INationalText Biography { get; }
    }

    public interface INationalText
    {
        string Content { get; }
        Language Language { get; }
    }

    public interface IPicture
    {
        ulong PictureId { get; }
        bool Is18Plus { get; }
    }

    public interface IEditPoints
    {
        ulong UserId { get; }
        string Name { get; }
        double Points { get; }
    }

    public interface IRelation : IIndexable
    {
        string CharacterFirstName { get; }
        string CharacterLastName { get; }
        string StaffFirstName { get; }
        string StaffLastName { get; }
        string CharacterRole { get; }
        string StaffPosition { get; }
        ulong? CharacterId { get; }
        Language Language { get; }
        ulong? StaffId { get; }
        string Title { get; }
    }

    public interface IDateTimePrecision
    {
        DateTime Date { get; }
        ulong? Precision { get; }
        bool IsSpecified { get; }
    }

    public interface ITag : IIndexable
    {
        string Name { get; }
    }

    public interface ITagCategory
    {
        string Name { get; }
        List<ITag> Tags { get; }
    }

    public interface ITitleInfo : IIndexable
    {
        string Title { get; }
        string CoverUrl { get; }
        double? TotalRating { get; }
        INationalText Description { get; }
        IDateTimePrecision StartDate { get; }
        IDateTimePrecision FinishDate { get; }
        List<ITagCategory> TagCategories { get; }
        List<INationalText> AlternativeTitles { get; }
    }

    public interface IAnimeTitleInfo : ITitleInfo
    {
        AnimeType Type { get; }
        string AnimeUrl { get; }
        AnimeStatus Status { get; }
        ulong? EpisodesCount { get; }
    }

    public interface IMangaTitleInfo : ITitleInfo
    {
        MangaType Type { get; }
        string MangaUrl { get; }
        MangaStatus Status { get; }
        ulong? ChaptersCount { get; }
    }

    public interface IQuickSearch : IIndexable
    {
        string Title { get; }
        string CoverUrl { get; }
    }

    public interface IPersonSearch : IIndexable
    {
        string LastName { get; }
        string FirstName { get; }
        string PersonUrl { get; }
        string PictureUrl { get; }
    }

    public interface IUserSearch : IIndexable
    {
        string Name { get; }
        string Rank { get; }
    }

    public interface IDedicatedTime
    {
        ulong? Days { get; }
        ulong? Years { get; }
        ulong? Hours { get; }
        ulong? Months { get; }
        ulong? Minutes { get; }
        ulong? TotalMinutes { get; }
    }

    public interface IMeanScore
    {
        ulong? Count { get; }
        double? Rating { get; }
    }

    public interface ISeriesStatus
    {
        ulong? InProgress { get; }
        ulong? Completed { get; }
        ulong? Dropped { get; }
        ulong? Skipped { get; }
        ulong? InPlan { get; }
        ulong? OnHold { get; }
        ulong? Total { get; }
    }

    public interface IListStats
    {
        ulong? ChaptersCount { get; }
        ulong? EpisodesCount { get; }
        IDedicatedTime MangaTime { get; }
        IDedicatedTime AnimeTime { get; }
        IMeanScore AnimeMeanScore { get; }
        IMeanScore MangaMeanScore { get; }
        ISeriesStatus AnimeStatus { get; }
        ISeriesStatus MangaStatus { get; }
    }

    public interface IUserInfo : IIndexable
    {
        string Name { get; }
        string AvatarUrl { get; }
        string Rank { get; }
        ulong? ForumId { get; }
        IListStats ListStats { get; }
    }

    public interface INewEpisode : IIndexable
    {
        string AnimeUrl { get; }
        DateTime AddDate { get; }
        string AnimeTitle { get; }
        string EpisodeUrl { get; }
        ulong EpisodeNumber { get; }
        string AnimeCoverUrl { get; }
        TimeSpan EpisodeLength { get; }
        Language SubtitlesLanguage { get; }
    }

    public interface ILastWatched
    {
        ulong TitleId { get; }
        long EpisodeNo { get; }
        long EpisodesCnt { get; }
        string AnimeTitle { get; }
        string AnimeCoverUrl { get; }
    }

    public interface ILastReaded
    {
        ulong TitleId { get; }
        long ChapterNo { get; }
        long ChaptersCnt { get; }
        string MangaTitle { get; }
        string MangaCoverUrl { get; }
    }
}
