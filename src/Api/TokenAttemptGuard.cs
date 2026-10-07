#pragma warning disable 1591

using System;
using System.Collections.Concurrent;
using Sanakan.Services.Time;

namespace Sanakan.Api
{
    public interface ITokenAttemptGuard
    {
        bool IsLocked(string key, out TimeSpan retryAfter);
        void RegisterFailure(string key);
        void RegisterSuccess(string key);
    }

    /// <summary>
    /// Śledzi nieudane próby wymiany klucza aplikacji na token (endpoint <c>api/token</c>).
    /// Po <see cref="MaxAttempts"/> nieudanych próbach blokuje dany klucz klienta na <see cref="LockDuration"/>.
    /// Stan trzymany w pamięci (nie przetrwa restartu procesu) - celowo, aby nie tworzyć kolejnej tabeli.
    /// </summary>
    public class TokenAttemptGuard : ITokenAttemptGuard
    {
        public const int MaxAttempts = 3;
        public static readonly TimeSpan LockDuration = TimeSpan.FromHours(24);

        private sealed class AttemptState
        {
            public int Failures;
            public DateTime? LockedUntil;
        }

        private readonly ConcurrentDictionary<string, AttemptState> _states = new ConcurrentDictionary<string, AttemptState>();
        private readonly ISystemTime _time;

        public TokenAttemptGuard(ISystemTime time) => _time = time;

        public bool IsLocked(string key, out TimeSpan retryAfter)
        {
            retryAfter = TimeSpan.Zero;
            if (string.IsNullOrEmpty(key) || !_states.TryGetValue(key, out var state))
                return false;

            lock (state)
            {
                if (state.LockedUntil == null)
                    return false;

                var now = _time.Now();
                if (state.LockedUntil.Value <= now)
                {
                    state.LockedUntil = null;
                    state.Failures = 0;
                    return false;
                }

                retryAfter = state.LockedUntil.Value - now;
                return true;
            }
        }

        public void RegisterFailure(string key)
        {
            if (string.IsNullOrEmpty(key))
                return;

            var state = _states.GetOrAdd(key, _ => new AttemptState());
            lock (state)
            {
                state.Failures++;
                if (state.Failures >= MaxAttempts)
                    state.LockedUntil = _time.Now().Add(LockDuration);
            }
        }

        public void RegisterSuccess(string key)
        {
            if (string.IsNullOrEmpty(key))
                return;

            _states.TryRemove(key, out _);
        }
    }
}
