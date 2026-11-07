#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Sanakan.Extensions;
using Sanakan.ShindenCompat.Models;
using Sanakan.ShindenCompat.Models.Entities;

namespace Sanakan.Services.Cache
{
    public class SeasonSnapshot
    {
        public int Year { get; set; }
        public AnimeSeason Season { get; set; }
        public DateTime UpdatedAt { get; set; }
        public List<ulong> Titles { get; set; } = new List<ulong>();
        public List<ulong> Characters { get; set; } = new List<ulong>();

        [JsonIgnore]
        public string Key => KeyOf(Year, Season);

        [JsonIgnore]
        public int Index => IndexOf(Year, Season);

        public string Name()
        {
            var name = Season switch
            {
                AnimeSeason.Winter => "zima",
                AnimeSeason.Spring => "wiosna",
                AnimeSeason.Summer => "lato",
                _ => "jesień",
            };
            return $"{name} {Year}";
        }

        public static string KeyOf(int year, AnimeSeason season) => $"{year}-{season.ToString().ToLower()}";

        public static int IndexOf(int year, AnimeSeason season) => year * 4 + (int)season;

        public static (int Year, AnimeSeason Season) FromIndex(int index) => (index / 4, (AnimeSeason)(index % 4));

        public static (int Year, AnimeSeason Season) Of(DateTime date) => (date.Year, (AnimeSeason)((date.Month - 1) / 3));

        public static bool TryParseKey(string key, out int year, out AnimeSeason season)
        {
            year = 0;
            season = AnimeSeason.Winter;

            var parts = key?.Trim().Split('-');
            if (parts == null || parts.Length != 2 || !int.TryParse(parts[0], out year) || year < 1900 || year > 3000)
                return false;

            AnimeSeason? parsed = parts[1].ToLowerInvariant() switch
            {
                "winter" => AnimeSeason.Winter,
                "spring" => AnimeSeason.Spring,
                "summer" => AnimeSeason.Summer,
                "autumn" or "fall" => AnimeSeason.Autumn,
                _ => null,
            };

            season = parsed ?? AnimeSeason.Winter;
            return parsed.HasValue;
        }
    }

    // Each season is kept in its own file and only grows, so packs granted earlier can still be opened.
    public class SeasonStore
    {
        // below this share of today's titles already known, Shinden is considered to have moved to the next season
        public const double SwitchOverlap = 0.5;

        // not *.json, so it is never read as a season
        private const string CurrentFile = "current.txt";

        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
        private readonly object _loadLock = new object();
        private readonly Dictionary<string, SeasonSnapshot> _seasons = new Dictionary<string, SeasonSnapshot>();
        private readonly TimeSpan _maxAge;
        private readonly TimeSpan _retryAfter;
        private readonly string _dir;
        private readonly ILogger _logger;
        private SeasonSnapshot _current;
        private DateTime _failedAt = DateTime.MinValue;
        private bool _loaded;

        public SeasonStore(TimeSpan maxAge, TimeSpan retryAfter, string dir = null, ILogger logger = null)
        {
            _maxAge = maxAge;
            _retryAfter = retryAfter;
            _dir = dir;
            _logger = logger;
            _loaded = dir == null;
        }

        public string LastError { get; private set; }

        public SeasonSnapshot Get(string key)
        {
            EnsureLoaded();
            lock (_seasons)
                return key != null && _seasons.TryGetValue(key, out var season) ? season : null;
        }

        public DateTime? FailedAt => _failedAt == DateTime.MinValue ? null : _failedAt;

        public List<SeasonSnapshot> All()
        {
            EnsureLoaded();
            lock (_seasons)
                return _seasons.Values.OrderByDescending(x => x.Index).ToList();
        }

        public SeasonSnapshot Current
        {
            get
            {
                EnsureLoaded();
                return _current;
            }
        }

        // fetch returns null or throws when there is nothing usable
        public async Task<SeasonSnapshot> GetCurrentAsync(Func<Task<CurrentSeason>> fetch, DateTime now, bool force = false)
        {
            EnsureLoaded();
            if (!force && !NeedsRefresh(now)) return _current;

            await _lock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!force && !NeedsRefresh(now)) return _current;

                try
                {
                    var value = await fetch().ConfigureAwait(false);
                    if (value != null && (!value.Titles.IsNullOrEmpty() || !value.Characters.IsNullOrEmpty()))
                    {
                        var (year, season) = Target(_current, value, now);
                        var merged = Merge(Get(SeasonSnapshot.KeyOf(year, season)), year, season, value, now);
                        lock (_seasons)
                        {
                            _seasons[merged.Key] = merged;
                            _current = merged;
                        }
                        LastError = null;
                        await SaveAsync(merged).ConfigureAwait(false);
                        await SaveCurrentAsync(merged).ConfigureAwait(false);
                        return merged;
                    }
                    LastError = "Empty response";
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                }

                _failedAt = now;
                return _current;
            }
            finally
            {
                _lock.Release();
            }
        }

        // fills the season the response is tagged with, the current season stays as it was
        public async Task<(SeasonSnapshot Season, string Error)> FillAsync(Func<Task<CurrentSeason>> fetch, DateTime now)
        {
            EnsureLoaded();
            await _lock.WaitAsync().ConfigureAwait(false);
            try
            {
                CurrentSeason value;
                try
                {
                    value = await fetch().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    return (null, ex.Message);
                }

                if (value == null || (value.Titles.IsNullOrEmpty() && value.Characters.IsNullOrEmpty()))
                    return (null, "Empty response");

                if (!value.Year.HasValue || !value.Season.HasValue)
                    return (null, "Response without season");

                var merged = Merge(Get(SeasonSnapshot.KeyOf(value.Year.Value, value.Season.Value)), value.Year.Value, value.Season.Value, value, now);
                lock (_seasons)
                {
                    _seasons[merged.Key] = merged;
                    if (_current?.Key == merged.Key)
                        _current = merged;
                }
                await SaveAsync(merged).ConfigureAwait(false);
                return (merged, null);
            }
            finally
            {
                _lock.Release();
            }
        }

        public static (int Year, AnimeSeason Season) Target(SeasonSnapshot current, CurrentSeason value, DateTime now)
        {
            if (value.Year.HasValue && value.Season.HasValue)
                return (value.Year.Value, value.Season.Value);

            var calendar = SeasonSnapshot.Of(now);
            if (current == null)
                return calendar;

            if (!IsNewSeason(current, value.Titles ?? new List<ulong>()))
                return (current.Year, current.Season);

            // Shinden may switch before or after the calendar does, the new season is never the one being replaced
            return SeasonSnapshot.FromIndex(Math.Max(SeasonSnapshot.IndexOf(calendar.Year, calendar.Season), current.Index + 1));
        }

        public static SeasonSnapshot Merge(SeasonSnapshot existing, int year, AnimeSeason season, CurrentSeason value, DateTime now)
        {
            var titles = value.Titles ?? new List<ulong>();
            var characters = value.Characters ?? new List<ulong>();

            // new lists instead of in-place changes, readers may still hold the old ones
            return new SeasonSnapshot
            {
                Year = year,
                Season = season,
                UpdatedAt = now,
                Titles = (existing?.Titles ?? new List<ulong>()).Union(titles).ToList(),
                Characters = (existing?.Characters ?? new List<ulong>()).Union(characters).ToList(),
            };
        }

        private static bool IsNewSeason(SeasonSnapshot current, List<ulong> titles)
        {
            if (titles.Count == 0)
                return false;

            var known = new HashSet<ulong>(current.Titles);
            return titles.Count(known.Contains) < titles.Count * SwitchOverlap;
        }

        private bool NeedsRefresh(DateTime now)
        {
            var updatedAt = _current?.UpdatedAt ?? DateTime.MinValue;
            return now - updatedAt >= _maxAge && now - _failedAt >= _retryAfter;
        }

        private void EnsureLoaded()
        {
            if (_loaded) return;

            lock (_loadLock)
            {
                if (_loaded) return;

                try
                {
                    if (Directory.Exists(_dir))
                    {
                        foreach (var file in Directory.GetFiles(_dir, "*.json"))
                        {
                            try
                            {
                                var season = JsonConvert.DeserializeObject<SeasonSnapshot>(File.ReadAllText(file));
                                if (season?.Characters == null || season.Titles == null) continue;

                                lock (_seasons)
                                {
                                    _seasons[season.Key] = season;
                                    // without the pointer file the last one written is what Shinden reported most recently
                                    if (_current == null || season.UpdatedAt > _current.UpdatedAt)
                                        _current = season;
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger?.LogError($"Seasons: loading {file}: {ex.Message}");
                            }
                        }

                        var pointer = Path.Combine(_dir, CurrentFile);
                        if (File.Exists(pointer))
                        {
                            var key = JsonConvert.DeserializeObject<string>(File.ReadAllText(pointer));
                            lock (_seasons)
                            {
                                if (key != null && _seasons.TryGetValue(key, out var current))
                                    _current = current;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError($"Seasons: loading {_dir}: {ex.Message}");
                }

                _loaded = true;
            }
        }

        private async Task SaveAsync(SeasonSnapshot season)
        {
            if (_dir == null) return;

            try
            {
                await CacheFile.WriteAsync(Path.Combine(_dir, $"{season.Key}.json"), season).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Seasons: saving {season.Key}: {ex.Message}");
            }
        }

        private async Task SaveCurrentAsync(SeasonSnapshot season)
        {
            if (_dir == null) return;

            try
            {
                await CacheFile.WriteAsync(Path.Combine(_dir, CurrentFile), season.Key).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Seasons: saving current: {ex.Message}");
            }
        }
    }
}
