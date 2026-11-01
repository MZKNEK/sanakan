#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;
using Newtonsoft.Json.Linq;
using Sanakan.ShindenCompat.Models;
using Sanakan.ShindenCompat.Models.Entities;
using Remote = ShindenAPI.Models;

namespace Sanakan.ShindenCompat
{
    // Mirrors the conversions of Shinden.NET 0.3, which parsed the same PHP response format.
    public static class ShindenMapper
    {
        private static readonly Regex BBCode = new Regex("\\[(.*?)\\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly string[] TagCategoryOrder = { "production_type", "entity", "source", "studio", "genre", "place", "tag" };

        public static ICharacterInfo ToModel(this Remote.CharacterInfo info) => new CharacterInfo
        {
            Id = info.Id,
            PictureId = info.PictureId ?? 0,
            IsReal = IsTrue(info.IsReal),
            Age = Decode(info.Age),
            Bust = Decode(info.Bust),
            Hips = Decode(info.Hips),
            Waist = Decode(info.Waist),
            Weight = Decode(info.Weight),
            Height = Decode(info.Height),
            LastName = Decode(info.LastName),
            FirstName = Decode(info.FirstName),
            Bloodtype = Decode(info.Bloodtype),
            Gender = ParseSex(info.Gender),
            Biography = info.Biography == null ? null : new NationalText
            {
                Language = ParseLanguage(info.Biography.Language),
                Content = RemoveBBCode(Decode(info.Biography.Text ?? info.Biography.Content)),
            },
            Points = info.Points?.Where(x => x != null).Select(x => (IEditPoints)new EditPoints
            {
                UserId = x.ULong("user_id") ?? 0,
                Name = x.Str("name") ?? "",
                Points = x.Double("points") ?? 0,
            }).ToList() ?? new List<IEditPoints>(),
            Pictures = info.Pictures?.Where(x => x?.Str("artifact_type")?.ToLower() == "image_picture").Select(x => (IPicture)new Picture
            {
                PictureId = x.ULong("artifact_id") ?? 0,
                Is18Plus = IsTrue(x.Str("is_18plus")),
            }).ToList() ?? new List<IPicture>(),
            Relations = info.Relations.ToRelations(),
        };

        public static List<IRelation> ToRelations(this IEnumerable<Remote.PhpRow> rows)
            => rows?.Where(x => x != null).Select(ToRelation).ToList() ?? new List<IRelation>();

        private static IRelation ToRelation(Remote.PhpRow row) => new Relation
        {
            Id = row.ULong("many_id") ?? 0,
            StaffId = row.ULong("staff_id") ?? 0,
            CharacterId = row.ULong("character_id") ?? 0,
            Language = ParseLanguage(row.Str("seiyuu_lang")),
            CharacterFirstName = Decode(row.Str("first_name")),
            CharacterLastName = Decode(row.Str("last_name")),
            StaffFirstName = Decode(row.Str("s_first_name")),
            StaffLastName = Decode(row.Str("s_last_name")),
            Title = Decode(row.Str("title")),
            StaffPosition = row.Str("position"),
            CharacterRole = row.Str("role"),
        };

        public static ITitleInfo ToModel(this Remote.AnimeMangaInfo info)
        {
            var title = info?.Title;
            if (title == null)
                return null;

            TitleInfo model;
            if ((title.Type ?? "").ToLower() == "anime")
            {
                model = new AnimeTitleInfo
                {
                    Type = ParseAnimeType(title.Anime?.Type),
                    Status = ParseAnimeStatus(title.Status),
                    EpisodesCount = ToULong(title.Anime?.EpisodesCount),
                };
            }
            else
            {
                model = new MangaTitleInfo
                {
                    Type = ParseMangaType(title.Type),
                    Status = ParseMangaStatus(title.Status),
                    ChaptersCount = ToULong(title.Manga?.ChaptersCount),
                };
            }

            model.Id = title.Id;
            model.CoverId = title.CoverId ?? 0;
            model.Title = HttpUtility.HtmlDecode(title.Name);
            model.TotalRating = ToRating(title.Double("rating_total_cnt"), title.Double("rating_total_sum"));
            model.StartDate = ToDate(title.StartDate, title.ULong("premiere_precision"));
            model.FinishDate = ToDate(title.FinishDate, title.ULong("finish_precision"));
            model.TagCategories = ToTagCategories(title.Tags);
            model.AlternativeTitles = title.AlternativeTitles?.Where(x => x != null).Select(x => (INationalText)new NationalText
            {
                Language = ParseLanguage(x.Str("lang")),
                Content = RemoveBBCode(HttpUtility.HtmlDecode(x.Str("title"))),
            }).ToList() ?? new List<INationalText>();
            model.Description = title.Description == null ? null : new NationalText
            {
                Language = ParseLanguage(title.Description.Language),
                Content = RemoveBBCode(HttpUtility.HtmlDecode(title.Description.Text)),
            };
            return model;
        }

        private static List<ITagCategory> ToTagCategories(JToken tags)
        {
            var categories = new List<ITagCategory>();
            if (tags is not JObject obj)
                return categories;

            foreach (var key in TagCategoryOrder)
            {
                if (obj[key] is not JObject category)
                    continue;

                categories.Add(new TagCategory
                {
                    Name = category.Value<string>("name"),
                    Tags = (category["items"] as JArray)?.OfType<JObject>().Select(x => (ITag)new Tag
                    {
                        Id = ParseULong(x["tag_id"]) ?? 0,
                        Name = x.Value<string>("national_name") ?? x.Value<string>("tag_name"),
                    }).ToList() ?? new List<ITag>(),
                });
            }
            return categories;
        }

        public static IUserInfo ToModel(this Remote.UserInfo info) => new UserInfo
        {
            Id = info.Id,
            Name = info.Name,
            Rank = info.Rank,
            ForumId = info.ForumId ?? 0,
            AvatarId = ParseULong(info.Avatar) ?? 0,
            ListStats = new ListStats
            {
                ChaptersCount = ToULong(info.ChaptersCount),
                EpisodesCount = ToULong(info.WatchedCount),
                MangaTime = info.ReadTime?.ToModel(),
                AnimeTime = info.WatchTime?.ToModel(),
                MangaStatus = info.ReadStatus?.ToSeriesStatus(),
                AnimeStatus = info.WatchedStatus?.ToSeriesStatus(),
                AnimeMeanScore = info.MeanAnimeScore.ToModel(),
                MangaMeanScore = info.MeanMangaScore.ToModel(),
            },
        };

        private static IDedicatedTime ToModel(this Remote.DedicatedTime time) => new DedicatedTime
        {
            Years = ToULong(time.Years),
            Months = ToULong(time.DaysInYear),
            Days = ToULong(time.Days),
            Hours = ToULong(time.Hours),
            Minutes = ToULong(time.Minutes),
        };

        private static ISeriesStatus ToSeriesStatus(this Dictionary<string, long?> status)
        {
            ulong Get(string key) => status.TryGetValue(key, out var value) ? ToULong(value) : 0;
            return new SeriesStatus
            {
                InPlan = Get("plan"),
                OnHold = Get("hold"),
                Skipped = Get("skip"),
                Dropped = Get("dropped"),
                Completed = Get("completed"),
                InProgress = Get("in progress"),
            };
        }

        private static IMeanScore ToModel(this Remote.MeanScore score) => new MeanScore
        {
            Count = ToULong(score?.Count),
            Rating = score?.Rating ?? 0,
        };

        public static IUserSearch ToModel(this Remote.UserSearchResult user) => new UserSearch
        {
            Id = user.Id,
            Name = user.Name,
            Rank = user.Rank,
        };

        public static IPersonSearch ToModel(this Remote.CharacterSearchResult character) => new PersonSearch
        {
            Id = character.Id,
            PictureId = character.PictureId ?? 0,
            FirstName = character.FirstName,
            LastName = character.LastName,
        };

        public static IQuickSearch ToModel(this Remote.TitleSearchResult title) => new QuickSearch
        {
            Id = title.Id,
            CoverId = title.CoverId ?? 0,
            Title = HttpUtility.HtmlDecode(title.Name),
        };

        public static ICharacterInfoShort ToModel(this Remote.FavouriteCharacter character) => new CharacterInfoShort
        {
            Id = character.Id,
            PictureId = character.PictureId ?? 0,
            LastName = Decode(character.LastName),
            FirstName = Decode(character.FirstName),
        };

        public static ICharacterInfoTitle ToModel(this Remote.CharacterFlat character)
            => new CharacterInfoTitle(character.CharacterId, Decode(character.LastName), Decode(character.FirstName),
                character.PictureId, HttpUtility.HtmlDecode(character.TitleName ?? "????"));

        public static INewEpisode ToModel(this Remote.NewEpisode episode) => new NewEpisode
        {
            Id = episode.Id,
            AnimeId = episode.TitleId ?? 0,
            CoverId = episode.ULong("cover_artifact_id") ?? 0,
            EpisodeNumber = episode.ULong("episode_no") ?? 0,
            AddDate = ParseDateTime(episode.Str("add_date")),
            AnimeTitle = episode.Str("title") ?? "",
            EpisodeLength = TimeSpan.FromMinutes(episode.ULong("episode_time") ?? 0),
            SubtitlesLanguage = ParseLanguage(episode.Str("sub_lang")),
        };

        public static ILastWatched ToModel(this Remote.LastWatched anime) => new LastWatched
        {
            TitleId = anime.TitleId,
            CoverId = anime.ULong("cover_artifact_id") ?? 0,
            EpisodeNo = anime.EpisodeNumber ?? 0,
            EpisodesCnt = anime.EpisodesCount ?? 0,
            AnimeTitle = anime.Title,
        };

        public static ILastReaded ToModel(this Remote.LastRead manga) => new LastReaded
        {
            TitleId = manga.TitleId,
            CoverId = manga.ULong("cover_artifact_id") ?? 0,
            ChapterNo = manga.ChapterNumber ?? 0,
            ChaptersCnt = manga.Long("chapters") ?? 0,
            MangaTitle = manga.Title,
        };

        private static string Str(this Remote.PhpRow row, string key)
            => row.AdditionalData != null && row.AdditionalData.TryGetValue(key, out var value) ? AsString(value) : null;

        private static string AsString(JToken token) => token switch
        {
            null => null,
            JValue { Value: null } => null,
            JValue value => Convert.ToString(value.Value, CultureInfo.InvariantCulture),
            _ => token.ToString(),
        };

        private static ulong? ULong(this Remote.PhpRow row, string key) => ParseULong(row.Str(key));

        private static long? Long(this Remote.PhpRow row, string key)
            => long.TryParse(row.Str(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

        private static double? Double(this Remote.PhpRow row, string key)
            => double.TryParse(row.Str(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;

        private static ulong? ParseULong(JToken token) => ParseULong(AsString(token));

        private static ulong? ParseULong(string value)
            => ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;

        private static ulong ToULong(long? value) => value > 0 ? (ulong)value.Value : 0;

        private static bool IsTrue(string value) => value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

        private static string Decode(string value) => HttpUtility.HtmlDecode(value ?? "");

        private static string RemoveBBCode(string value) => value == null ? null : BBCode.Replace(value, "");

        private static double? ToRating(double? count, double? sum)
            => count.GetValueOrDefault() == 0 || sum.GetValueOrDefault() == 0 ? 0 : sum / count;

        private static IDateTimePrecision ToDate(string unixTimeStamp, ulong? precision)
        {
            double.TryParse(unixTimeStamp, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds);
            return new DateTimePrecision
            {
                Date = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc).AddSeconds(seconds),
                Precision = precision ?? 0,
            };
        }

        private static DateTime ParseDateTime(string value)
            => DateTime.TryParseExact(value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : DateTime.MinValue;

        private static Sex ParseSex(string value) => (value ?? "").ToLower() switch
        {
            "m" => Sex.Male,
            "k" => Sex.Female,
            "f" => Sex.Female,
            "male" => Sex.Male,
            "other" => Sex.Other,
            "female" => Sex.Female,
            _ => Sex.NotSpecified,
        };

        private static Language ParseLanguage(string value) => (value ?? "").ToLower() switch
        {
            "pl" => Language.Polish,
            "kr" => Language.Korean,
            "ko" => Language.Korean,
            "cn" => Language.Chinese,
            "en" => Language.English,
            "jp" => Language.Japanese,
            _ => Language.NotSpecified,
        };

        private static AnimeType ParseAnimeType(string value) => (value ?? "").ToLower() switch
        {
            "tv" => AnimeType.Tv,
            "ona" => AnimeType.Ona,
            "ova" => AnimeType.Ova,
            "movie" => AnimeType.Movie,
            "music" => AnimeType.Music,
            "special" => AnimeType.Special,
            _ => AnimeType.NotSpecified,
        };

        private static MangaType ParseMangaType(string value) => (value ?? "").ToLower() switch
        {
            "light_novel" => MangaType.LightNovel,
            "doujinshi" => MangaType.Doujinshi,
            "novel" => MangaType.LightNovel,
            "one_shot" => MangaType.OneShot,
            "one shot" => MangaType.OneShot,
            "doujin" => MangaType.Doujinshi,
            "manhua" => MangaType.Manhua,
            "manhwa" => MangaType.Manhwa,
            "manga" => MangaType.Manga,
            _ => MangaType.NotSpecified,
        };

        private static AnimeStatus ParseAnimeStatus(string value) => (value ?? "").ToLower() switch
        {
            "currently airing" => AnimeStatus.CurrentlyAiring,
            "finished airing" => AnimeStatus.FinishedAiring,
            "not yet aired" => AnimeStatus.NotYetAired,
            "proposal" => AnimeStatus.Proposal,
            _ => AnimeStatus.NotSpecified,
        };

        private static MangaStatus ParseMangaStatus(string value) => (value ?? "").ToLower() switch
        {
            "finished" => MangaStatus.Finished,
            "publishing" => MangaStatus.Publishing,
            "not yet published" => MangaStatus.NotYetPublished,
            _ => MangaStatus.NotSpecified,
        };
    }
}
