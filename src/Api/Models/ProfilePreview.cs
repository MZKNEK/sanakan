#pragma warning disable 1591

using System.Collections.Generic;

namespace Sanakan.Api.Models
{
    /// <summary>
    /// Dane do podglądu profilu w Profilatorze
    /// </summary>
    public class ProfilePreview
    {
        /// <summary>
        /// Kod obecnego wyglądu profilu z linkami do własnych obrazków (konfiguracja profilu kod)
        /// </summary>
        public string Code { get; set; }
        /// <summary>
        /// Nazwa na serwerze
        /// </summary>
        public string Nick { get; set; }
        /// <summary>
        /// Awatar z Discorda
        /// </summary>
        public string Avatar { get; set; }
        /// <summary>
        /// Kolor najwyższej roli na serwerze, #rrggbb
        /// </summary>
        public string Color { get; set; }
        /// <summary>
        /// Ranga z Shindena
        /// </summary>
        public string Rank { get; set; }
        public long Level { get; set; }
        public long Exp { get; set; }
        /// <summary>
        /// Miejsce w rankingu poziomów
        /// </summary>
        public long Top { get; set; }
        public ulong Messages { get; set; }
        public long Sc { get; set; }
        public long Tc { get; set; }
        public long Ac { get; set; }
        public long Ct { get; set; }
        public long Pc { get; set; }
        /// <summary>
        /// Liczba posiadanych kart
        /// </summary>
        public int Cards { get; set; }
        /// <summary>
        /// Limit kart
        /// </summary>
        public long Limit { get; set; }
        public double Karma { get; set; }
        /// <summary>
        /// Liczba kart (bez ultimate) według jakości
        /// </summary>
        public Dictionary<string, int> Rarities { get; set; }
        /// <summary>
        /// Karty z własnym obrazkiem
        /// </summary>
        public int Scalpels { get; set; }
        /// <summary>
        /// Karty z własną ramką
        /// </summary>
        public int Scissors { get; set; }
        public int Ultimates { get; set; }
        /// <summary>
        /// Obrazek profilowy karty waifu
        /// </summary>
        public string Waifu { get; set; }
        /// <summary>
        /// Obrazki profilowe kart galerii w kolejności profilu
        /// </summary>
        public List<string> Gallery { get; set; }
        /// <summary>
        /// Statystyki listy anime z Shindena
        /// </summary>
        public ProfileListStats Anime { get; set; }
        /// <summary>
        /// Statystyki listy mang z Shindena
        /// </summary>
        public ProfileListStats Manga { get; set; }
    }

    /// <summary>
    /// Statystyki listy z Shindena, jak na profilu
    /// </summary>
    public class ProfileListStats
    {
        public ulong InProgress { get; set; }
        public ulong Completed { get; set; }
        public ulong Skipped { get; set; }
        public ulong OnHold { get; set; }
        public ulong Dropped { get; set; }
        public ulong InPlan { get; set; }
        public ulong Total { get; set; }
        public double Rating { get; set; }
        public ulong Count { get; set; }
        public ulong Years { get; set; }
        public ulong Months { get; set; }
        public ulong Days { get; set; }
        public ulong Hours { get; set; }
        public ulong Minutes { get; set; }
    }
}
