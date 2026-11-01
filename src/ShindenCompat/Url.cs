#pragma warning disable 1591

namespace Sanakan.ShindenCompat
{
    // Same links as Shinden.NET 0.3, card images saved in the database depend on this format.
    public static class Url
    {
        private const string SiteUrl = "https://shinden.pl/";
        private const string ImageServerUrl = "http://cdn.shinden.eu/";

        public static string GetBigImageURL(ulong? imageId)
            => imageId.GetValueOrDefault() == 0 ? GetPlaceholderImageURL() : $"{ImageServerUrl}cdn1/images/genuine/{imageId}.jpg";

        public static string GetUserAvatarURL(ulong? imageId, ulong userId)
            => imageId.GetValueOrDefault() == 0 ? GetPlaceholderUserImageURL() : $"{ImageServerUrl}cdn1/avatars/225x350/{userId}.jpg?v{imageId}";

        public static string GetPersonPictureURL(ulong? imageId) => GetBigImageURL(imageId);

        public static string GetPlaceholderImageURL() => $"{ImageServerUrl}cdn1/other/placeholders/title/225x350.jpg";

        public static string GetPlaceholderUserImageURL() => $"{ImageServerUrl}cdn1/other/placeholders/user/100x100.jpg";

        public static string GetSeriesURL(ulong seriesId) => $"{SiteUrl}titles/{seriesId}";

        public static string GetMangaURL(ulong seriesId) => $"{SiteUrl}manga/{seriesId}";

        public static string GetEpisodeURL(ulong seriesId, ulong episodeId) => $"{SiteUrl}epek/{seriesId}/view/{episodeId}";

        public static string GetCharacterURL(ulong id) => $"{SiteUrl}character/{id}";

        public static string GetProfileURL(ulong id) => $"{SiteUrl}user/{id}";
    }
}
