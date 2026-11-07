#pragma warning disable 1591

namespace Sanakan.ShindenCompat
{
    public enum QuickSearchType
    {
        Manga, Anime
    }
}

namespace Sanakan.ShindenCompat.Models
{
    public enum AnimeSeason
    {
        Winter, Spring, Summer, Autumn
    }

    public enum Sex
    {
        NotSpecified, Female, Other, Male
    }

    public enum Language
    {
        NotSpecified, Japanese, Chinese, English, Korean, Polish
    }

    public enum AnimeType
    {
        NotSpecified, Special, Music, Movie, Ova, Ona, Tv
    }

    public enum MangaType
    {
        NotSpecified, LightNovel, Doujinshi, OneShot, Manhwa, Manhua, Manga
    }

    public enum AnimeStatus
    {
        NotSpecified, FinishedAiring, CurrentlyAiring, NotYetAired, Proposal
    }

    public enum MangaStatus
    {
        NotSpecified, NotYetPublished, Publishing, Finished
    }
}
