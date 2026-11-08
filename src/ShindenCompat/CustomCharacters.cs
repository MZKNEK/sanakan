#pragma warning disable 1591

namespace Sanakan.ShindenCompat
{
    // characters made up for the bot get ids far above Shinden's, so they never collide with a real character
    public static class CustomCharacters
    {
        public const ulong IdStart = 1_000_000_000;

        public static bool IsCustom(ulong characterId) => characterId > IdStart;
    }
}
