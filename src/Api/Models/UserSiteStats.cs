#pragma warning disable 1591

namespace Sanakan.Api.Models
{
    /// <summary>
    /// Dodatkowe statystyki gracza dla strony
    /// </summary>
    public class UserSiteStats
    {
        /// <summary>
        /// Liczba nieotwartych pakietów
        /// </summary>
        public int UnopenedBoosterPacks { get; set; }
        /// <summary>
        /// Liczba kart utworzonych z przedmiotów
        /// </summary>
        public long CreatedCardsFromItems { get; set; }
        /// <summary>
        /// Liczba gier na automacie
        /// </summary>
        public long SlotMachineGames { get; set; }
        /// <summary>
        /// Zarobione SC
        /// </summary>
        public long IncomeInSc { get; set; }
        /// <summary>
        /// Stracone SC
        /// </summary>
        public long ScLost { get; set; }
        /// <summary>
        /// Liczba poprawnych odpowiedzi w quizach
        /// </summary>
        public long RightAnswers { get; set; }
        /// <summary>
        /// Liczba wszystkich odpowiedzi w quizach
        /// </summary>
        public long TotalAnswers { get; set; }
        /// <summary>
        /// Liczba wygranych turniejów
        /// </summary>
        public long TurnamentsWon { get; set; }
    }
}
