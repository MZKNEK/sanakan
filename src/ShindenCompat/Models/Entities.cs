#pragma warning disable 1591

using System;
using System.Collections.Generic;

namespace Sanakan.ShindenCompat.Models.Entities
{
    public class CharacterInfoShort : ICharacterInfoShort
    {
        public ulong Id { get; set; }
        public ulong? PictureId { get; set; }
        public string LastName { get; set; }
        public string FirstName { get; set; }
        public string CharacterUrl => Url.GetCharacterURL(Id);
        public string PictureUrl => Url.GetPersonPictureURL(PictureId);
        public bool HasImage => PictureUrl != Url.GetPlaceholderImageURL();

        public override string ToString() => FirstName + " " + LastName;
    }

    public class CharacterInfoTitle : CharacterInfoShort, ICharacterInfoTitle
    {
        public CharacterInfoTitle() { }

        public CharacterInfoTitle(ulong id, string lastName, string firstName, ulong pictureId, string titleName)
        {
            Id = id;
            LastName = lastName;
            FirstName = firstName;
            PictureId = pictureId;
            TitleName = titleName;
        }

        public string TitleName { get; set; }
    }

    public class CharacterInfo : CharacterInfoShort, ICharacterInfo
    {
        public string Age { get; set; }
        public Sex Gender { get; set; }
        public string Hips { get; set; }
        public string Bust { get; set; }
        public bool IsReal { get; set; }
        public string Waist { get; set; }
        public string Height { get; set; }
        public string Weight { get; set; }
        public string Bloodtype { get; set; }
        public List<IPicture> Pictures { get; set; } = new List<IPicture>();
        public List<IEditPoints> Points { get; set; } = new List<IEditPoints>();
        public List<IRelation> Relations { get; set; } = new List<IRelation>();
        public INationalText Biography { get; set; }
    }

    public class NationalText : INationalText
    {
        public string Content { get; set; }
        public Language Language { get; set; }

        public override string ToString() => Content;
    }

    public class Picture : IPicture
    {
        public ulong PictureId { get; set; }
        public bool Is18Plus { get; set; }
    }

    public class EditPoints : IEditPoints
    {
        public ulong UserId { get; set; }
        public string Name { get; set; }
        public double Points { get; set; }
    }

    public class Relation : IRelation
    {
        public ulong Id { get; set; }
        public string CharacterFirstName { get; set; }
        public string CharacterLastName { get; set; }
        public string StaffFirstName { get; set; }
        public string StaffLastName { get; set; }
        public string CharacterRole { get; set; }
        public string StaffPosition { get; set; }
        public ulong? CharacterId { get; set; }
        public Language Language { get; set; }
        public ulong? StaffId { get; set; }
        public string Title { get; set; }

        public override string ToString() => Title;
    }

    public class DateTimePrecision : IDateTimePrecision
    {
        private static readonly DateTime DefaultDate = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);

        public DateTime Date { get; set; }
        public ulong? Precision { get; set; }
        public bool IsSpecified => DefaultDate.Date != Date.Date;

        public override string ToString() => Precision switch
        {
            1 => Date.ToString("yyyy"),
            2 => Date.ToString("MM.yyyy"),
            _ => Date.ToString("dd.MM.yyyy"),
        };
    }

    public class Tag : ITag
    {
        public ulong Id { get; set; }
        public string Name { get; set; }

        public override string ToString() => Name;
    }

    public class TagCategory : ITagCategory
    {
        public string Name { get; set; }
        public List<ITag> Tags { get; set; } = new List<ITag>();

        public override string ToString() => Name;
    }

    public abstract class TitleInfo : ITitleInfo
    {
        public ulong Id { get; set; }
        public ulong? CoverId { get; set; }
        public string Title { get; set; }
        public string CoverUrl => Url.GetBigImageURL(CoverId);
        public double? TotalRating { get; set; }
        public INationalText Description { get; set; }
        public IDateTimePrecision StartDate { get; set; }
        public IDateTimePrecision FinishDate { get; set; }
        public List<ITagCategory> TagCategories { get; set; } = new List<ITagCategory>();
        public List<INationalText> AlternativeTitles { get; set; } = new List<INationalText>();

        public override string ToString() => Title;
    }

    public class AnimeTitleInfo : TitleInfo, IAnimeTitleInfo
    {
        public AnimeType Type { get; set; }
        public string AnimeUrl => Url.GetSeriesURL(Id);
        public AnimeStatus Status { get; set; }
        public ulong? EpisodesCount { get; set; }
    }

    public class MangaTitleInfo : TitleInfo, IMangaTitleInfo
    {
        public MangaType Type { get; set; }
        public string MangaUrl => Url.GetMangaURL(Id);
        public MangaStatus Status { get; set; }
        public ulong? ChaptersCount { get; set; }
    }

    public class QuickSearch : IQuickSearch
    {
        public ulong Id { get; set; }
        public ulong? CoverId { get; set; }
        public string Title { get; set; }
        public string CoverUrl => Url.GetBigImageURL(CoverId);

        public override string ToString() => Title;
    }

    public class PersonSearch : IPersonSearch
    {
        public ulong Id { get; set; }
        public ulong? PictureId { get; set; }
        public string LastName { get; set; }
        public string FirstName { get; set; }
        public string PersonUrl => Url.GetCharacterURL(Id);
        public string PictureUrl => Url.GetPersonPictureURL(PictureId);

        public override string ToString() => FirstName + " " + LastName;
    }

    public class UserSearch : IUserSearch
    {
        public ulong Id { get; set; }
        public string Name { get; set; }
        public string Rank { get; set; }

        public override string ToString() => Name;
    }

    public class DedicatedTime : IDedicatedTime
    {
        public ulong? Days { get; set; }
        public ulong? Years { get; set; }
        public ulong? Hours { get; set; }
        public ulong? Months { get; set; }
        public ulong? Minutes { get; set; }
        public ulong? TotalMinutes => Minutes + Hours * 60 + Days * 24 * 60 + Months * 31 * 24 * 60 + Years * 12 * 31 * 24 * 60;

        public override string ToString() => $"{TotalMinutes}";
    }

    public class MeanScore : IMeanScore
    {
        public ulong? Count { get; set; }
        public double? Rating { get; set; }
    }

    public class SeriesStatus : ISeriesStatus
    {
        public ulong? InProgress { get; set; }
        public ulong? Completed { get; set; }
        public ulong? Dropped { get; set; }
        public ulong? Skipped { get; set; }
        public ulong? InPlan { get; set; }
        public ulong? OnHold { get; set; }
        public ulong? Total => InProgress + Completed + Dropped + Skipped + InPlan + OnHold;

        public override string ToString() => $"{Total}";
    }

    public class ListStats : IListStats
    {
        public ulong? ChaptersCount { get; set; }
        public ulong? EpisodesCount { get; set; }
        public IDedicatedTime MangaTime { get; set; }
        public IDedicatedTime AnimeTime { get; set; }
        public IMeanScore AnimeMeanScore { get; set; }
        public IMeanScore MangaMeanScore { get; set; }
        public ISeriesStatus AnimeStatus { get; set; }
        public ISeriesStatus MangaStatus { get; set; }
    }

    public class UserInfo : IUserInfo
    {
        public ulong Id { get; set; }
        public ulong? AvatarId { get; set; }
        public string Name { get; set; }
        public string AvatarUrl => Url.GetUserAvatarURL(AvatarId, Id);
        public string Rank { get; set; }
        public ulong? ForumId { get; set; }
        public IListStats ListStats { get; set; }

        public override string ToString() => Name;
    }

    public class NewEpisode : INewEpisode
    {
        public ulong Id { get; set; }
        public ulong AnimeId { get; set; }
        public ulong? CoverId { get; set; }
        public DateTime AddDate { get; set; }
        public string AnimeTitle { get; set; }
        public ulong EpisodeNumber { get; set; }
        public TimeSpan EpisodeLength { get; set; }
        public Language SubtitlesLanguage { get; set; }
        public string AnimeUrl => Url.GetSeriesURL(AnimeId);
        public string AnimeCoverUrl => Url.GetBigImageURL(CoverId);
        public string EpisodeUrl => Url.GetEpisodeURL(AnimeId, Id);

        public override string ToString() => $"{AnimeTitle} ({EpisodeNumber})";
    }

    public class LastWatched : ILastWatched
    {
        public ulong TitleId { get; set; }
        public ulong? CoverId { get; set; }
        public long EpisodeNo { get; set; }
        public long EpisodesCnt { get; set; }
        public string AnimeTitle { get; set; }
        public string AnimeCoverUrl => Url.GetBigImageURL(CoverId);

        public override string ToString() => $"{AnimeTitle}: {EpisodeNo}/{EpisodesCnt}";
    }

    public class LastReaded : ILastReaded
    {
        public ulong TitleId { get; set; }
        public ulong? CoverId { get; set; }
        public long ChapterNo { get; set; }
        public long ChaptersCnt { get; set; }
        public string MangaTitle { get; set; }
        public string MangaCoverUrl => Url.GetBigImageURL(CoverId);

        public override string ToString() => $"{MangaTitle}: {ChapterNo}/{ChaptersCnt}";
    }
}
