#pragma warning disable 1591

namespace Sanakan
{
    // Jedno miejsce, w którym buduje się nazwy tagów cache.
    // Dzięki temu nie ma literałów typu $"user-{id}" rozsianych po całym kodzie.
    public static class CacheTags
    {
        public const string Mute = "mute";
        public const string Quiz = "quiz";
        public const string UltimateCards = "ultimate-cards";
        public const string UniqueCards = "unique-cards";

        public static string User(ulong id) => $"user-{id}";
        public static string Character(ulong id) => $"character-{id}";
        public static string Guild(ulong id) => $"config-{id}";
        public static string UserProfile(ulong id) => $"user-profile-{id}";
    }
}
