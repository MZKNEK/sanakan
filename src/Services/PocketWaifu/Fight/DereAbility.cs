#pragma warning disable 1591

using Sanakan.Database.Models;

namespace Sanakan.Services.PocketWaifu.Fight
{
    public static class DereAbility
    {
        public const double TsundereGuardBelow = 0.5;
        public const double TsundereGuardMultiplier = 0.75;
        public const double DeredereHealBelow = 0.3;
        public const double DeredereHeal = 0.25;
        public const double YandereRageBelow = 0.25;
        public const double YandereRageMultiplier = 1.5;
        public const double DandereDodgeChance = 10;
        public const double KuudereCritBonus = 20;
        public const double BodereSecondStrikeChance = 15;

        public static string GetAbilityDescription(this Dere dere) => dere switch
        {
            Dere.Tsundere => "poniżej połowy życia otrzymuje o 25% mniej obrażeń",
            Dere.Kamidere => "przewaga charakteru przeciwnika nie zwiększa otrzymywanych obrażeń",
            Dere.Deredere => "raz na walkę, gdy spadnie poniżej 30% życia, leczy się o 25% życia",
            Dere.Yandere  => "poniżej 25% życia zadaje o połowę większe obrażenia",
            Dere.Dandere  => "unika 10% ataków",
            Dere.Kuudere  => "ma 20 punktów precyzji więcej",
            Dere.Mayadere => "przewaga charakteru przeciwnika nie zmniejsza zadawanych obrażeń",
            Dere.Bodere   => "po swoim ataku w 15% przypadków atakuje drugi raz",
            _ => null
        };
    }
}
