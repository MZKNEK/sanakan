#pragma warning disable 1591

using Sanakan.Database.Models;

namespace Sanakan.Extensions
{
    public static class DereExtension
    {
        /// <summary>
        /// Czy karta nie może zostać przeistoczona w demona (jest już demonem lub bogiem Yato).
        /// </summary>
        public static bool BlocksDemonTransform(this Dere dere) => dere == Dere.Yami || dere == Dere.Yato;

        /// <summary>
        /// Czy karta nie może zostać przeistoczona w anioła (jest już aniołem lub bogiem Yato).
        /// </summary>
        public static bool BlocksAngelTransform(this Dere dere) => dere == Dere.Raito || dere == Dere.Yato;
    }
}
