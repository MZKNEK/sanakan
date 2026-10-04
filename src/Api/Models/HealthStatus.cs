#pragma warning disable 1591

using System;
using Newtonsoft.Json;

namespace Sanakan.Api.Models
{
    /// <summary>
    /// Stan bota
    /// </summary>
    public class HealthStatus
    {
        /// <summary>
        /// ok, degraded (baza lub Shinden nie odpowiada, wysoki ping albo odrzucone polecenia) lub down (brak połączenia z Discordem)
        /// </summary>
        public string Status { get; set; }
        /// <summary>
        /// Wersja bota
        /// </summary>
        public string Version { get; set; }
        /// <summary>
        /// Czas uruchomienia procesu
        /// </summary>
        public DateTime StartedAt { get; set; }
        /// <summary>
        /// Połączenie z Discordem
        /// </summary>
        public HealthDiscord Discord { get; set; }
        /// <summary>
        /// Baza danych
        /// </summary>
        public HealthDatabase Database { get; set; }
        /// <summary>
        /// Shinden API
        /// </summary>
        public HealthShinden Shinden { get; set; }
        /// <summary>
        /// Wykonane polecenia
        /// </summary>
        public HealthCommands Commands { get; set; }
    }

    /// <summary>
    /// Połączenie z Discordem
    /// </summary>
    public class HealthDiscord
    {
        /// <summary>
        /// Stan połączenia
        /// </summary>
        public string State { get; set; }
        /// <summary>
        /// Opóźnienie gatewaya
        /// </summary>
        public int LatencyMs { get; set; }
        /// <summary>
        /// Od kiedy jest połączony (null - brak połączenia)
        /// </summary>
        public DateTime? ConnectedAt { get; set; }
        /// <summary>
        /// Liczba serwerów
        /// </summary>
        public int Guilds { get; set; }
        /// <summary>
        /// Suma członków wszystkich serwerów (bez usuwania duplikatów)
        /// </summary>
        public int Members { get; set; }
    }

    /// <summary>
    /// Wynik sprawdzenia zależności (odświeżany co 20 s)
    /// </summary>
    public class HealthProbe
    {
        /// <summary>
        /// Czy odpowiada
        /// </summary>
        public bool Ok { get; set; }
        /// <summary>
        /// Czas odpowiedzi (dla bazy: najkrótsze zapytanie bota w bieżącej minucie albo SELECT 1, jeśli nie było żadnego)
        /// </summary>
        public long LatencyMs { get; set; }
    }

    /// <summary>
    /// Baza danych - wynik sprawdzenia i zapytania wykonane przez bota
    /// </summary>
    public class HealthDatabase : HealthProbe
    {
        /// <summary>
        /// Zapytania w ostatnich 5 minutach
        /// </summary>
        [JsonProperty("queries5min")]
        public int Queries5Min { get; set; }
        /// <summary>
        /// Nieudane zapytania w ostatnich 5 minutach
        /// </summary>
        [JsonProperty("errors5min")]
        public int Errors5Min { get; set; }
        /// <summary>
        /// Średni czas zapytania w ostatnich 5 minutach
        /// </summary>
        [JsonProperty("avgMs5min")]
        public long AvgMs5Min { get; set; }
        /// <summary>
        /// Najdłuższe zapytanie w ostatnich 5 minutach
        /// </summary>
        [JsonProperty("maxMs5min")]
        public long MaxMs5Min { get; set; }
    }

    /// <summary>
    /// Shinden API - wynik sprawdzenia i zapytania wysłane przez bota
    /// </summary>
    public class HealthShinden : HealthProbe
    {
        /// <summary>
        /// Zapytania w ostatnich 5 minutach
        /// </summary>
        [JsonProperty("requests5min")]
        public int Requests5Min { get; set; }
        /// <summary>
        /// Nieudane zapytania w ostatnich 5 minutach (błąd połączenia, timeout, błąd parsowania, 5xx, 401, 403, 408, 429)
        /// </summary>
        [JsonProperty("errors5min")]
        public int Errors5Min { get; set; }
        /// <summary>
        /// Procent nieudanych zapytań w ostatnich 5 minutach
        /// </summary>
        [JsonProperty("errorRate5min")]
        public double ErrorRate5Min { get; set; }
        /// <summary>
        /// Zapytania przerwane przez timeout w ostatnich 5 minutach (wliczone w errors5min)
        /// </summary>
        [JsonProperty("timeouts5min")]
        public int Timeouts5Min { get; set; }
        /// <summary>
        /// Zapytania w ostatniej godzinie
        /// </summary>
        [JsonProperty("requestsHour")]
        public int RequestsHour { get; set; }
        /// <summary>
        /// Nieudane zapytania w ostatniej godzinie
        /// </summary>
        [JsonProperty("errorsHour")]
        public int ErrorsHour { get; set; }
        /// <summary>
        /// Procent nieudanych zapytań w ostatniej godzinie
        /// </summary>
        [JsonProperty("errorRateHour")]
        public double ErrorRateHour { get; set; }
        /// <summary>
        /// Zapytania przerwane przez timeout w ostatniej godzinie (wliczone w errorsHour)
        /// </summary>
        [JsonProperty("timeoutsHour")]
        public int TimeoutsHour { get; set; }
    }

    /// <summary>
    /// Wykonane polecenia
    /// </summary>
    public class HealthCommands
    {
        /// <summary>
        /// Polecenia w ostatnich 5 minutach
        /// </summary>
        [JsonProperty("last5min")]
        public int Last5Min { get; set; }
        /// <summary>
        /// Polecenia zakończone błędem w ostatnich 5 minutach
        /// </summary>
        [JsonProperty("errors5min")]
        public int Errors5Min { get; set; }
        /// <summary>
        /// Polecenia w ostatniej godzinie
        /// </summary>
        [JsonProperty("lastHour")]
        public int LastHour { get; set; }
        /// <summary>
        /// Polecenia zakończone błędem w ostatniej godzinie
        /// </summary>
        [JsonProperty("errorsHour")]
        public int ErrorsHour { get; set; }
        /// <summary>
        /// Polecenia odrzucone w ostatnich 5 minutach (pełna kolejka)
        /// </summary>
        [JsonProperty("rejected5min")]
        public int Rejected5Min { get; set; }
        /// <summary>
        /// Polecenia odrzucone w ostatniej godzinie (pełna kolejka)
        /// </summary>
        [JsonProperty("rejectedHour")]
        public int RejectedHour { get; set; }
    }
}
