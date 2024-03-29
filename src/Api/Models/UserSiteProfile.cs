﻿using System.Collections.Generic;

namespace Sanakan.Api.Models
{
    /// <summary>
    /// Profil użytkownika na stronie
    /// </summary>
    public class UserSiteProfile
    {
        /// <summary>
        /// Liczba posaidanych kart z podziałem na jakość
        /// </summary>
        public Dictionary<string, long> CardsCount { get; set; }
        /// <summary>
        /// Waluty jakie użytkownik posiada
        /// </summary>
        public Dictionary<string, long> Wallet { get; set; }
        /// <summary>
        /// Waifu
        /// </summary>
        public CardFinalView Waifu { get; set; }
        /// <summary>
        /// Galeria
        /// </summary>
        public List<CardFinalView> Gallery { get; set; }
        /// <summary>
        /// Posortowanie galerii - kolejność id kart
        /// </summary>
        public List<ulong> GalleryOrder { get; set; }
        /// <summary>
        /// Lista wypraw
        /// </summary>
        public List<ExpeditionCard> Expeditions { get; set; }
        /// <summary>
        /// Lista tagów jakie ma użytkownik na kartach
        /// </summary>
        public List<TagIdPair> TagList { get; set; }
        /// <summary>
        /// warunki wymiany z użytkownikiem
        /// </summary>
        public string ExchangeConditions { get; set; }
        /// <summary>
        /// Tytuł użytkownika z gry
        /// </summary>
        public string UserTitle { get; set; }
        /// <summary>
        /// Pozycja obrazku tła profilu użytkownika
        /// </summary>
        public int BackgroundPosition { get; set; }
        /// <summary>
        /// Pozycja obrazka postaci na tle profilu użytkownika
        /// </summary>
        public int ForegroundPosition { get; set; }
        /// <summary>
        /// Obrazek tła profilu użytkownika
        /// </summary>
        public string BackgroundImageUrl { get; set; }
        /// <summary>
        /// Obrazek postaci na tle profilu użytkownika
        /// </summary>
        public string ForegroundImageUrl { get; set; }
        /// <summary>
        /// Główny kolor profilu użytkownika
        /// </summary>
        public string ForegroundColor { get; set; }
        /// <summary>
        /// Karma użytkownika
        /// </summary>
        public double Karma { get; set; }

        public static ulong TryParseIds(string s)
        {
            ulong.TryParse(s, out var nId);
            return nId;
        }
    }
}
