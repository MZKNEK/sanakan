#pragma warning disable 1591

using System.IO;

namespace Sanakan.Services
{
    public static class Dir
    {
        private const string PicDir = "./Pictures";
        private const string BaseOutput = "../GOut";

        public static void Create()
        {
            Directory.CreateDirectory(LocalCardData);
            Directory.CreateDirectory(CardsMiniatures);
            Directory.CreateDirectory(CardsInProfiles);
        }

        public static string Cards = $"{BaseOutput}/Cards";
        public static string CardsMiniatures = $"{Cards}/Small";
        public static string CardsInProfiles = $"{Cards}/Profile";

        public static string SavedData = $"{BaseOutput}/Saved";
        public static string LocalCardData = $"{SavedData}/Card";

        public static string GetResource(string path) => $"{PicDir}/{path}";
        public static bool IsLocal(string path) => path.StartsWith(BaseOutput) || path.StartsWith(PicDir);
    }
}