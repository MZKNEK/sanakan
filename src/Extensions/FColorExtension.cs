#pragma warning disable 1591

using Sanakan.Services;
using Sanakan.Services.PocketWaifu;

namespace Sanakan.Extensions
{
    public static class FColorExtension
    {
        public static int Price(this FColor color, CurrencyType currency)
        {
            if (color == FColor.CleanColor)
                return 0;

            if (currency == CurrencyType.SC)
                return 39999;

            switch (color)
            {
                case FColor.DefinitelyNotWhite:
                    return 799;

                default:
                    return 800;
            }
        }

        public static bool IsOption(this FColor color)
        {
            switch (color)
            {
                case FColor.None:
                case FColor.CleanColor:
                    return true;

                default:
                    return false;
            }
        }
    }
}
