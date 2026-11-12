#pragma warning disable 1591

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Sanakan.Services.PocketWaifu
{
    public enum CardDrawing { Drawn, AlreadyDrawn, NoPicture, Failed, Removed }

    // Draws the images of changed cards in the background, so they are usually ready before anyone asks for them.
    // A card is drawn only after it has not changed for a while, so a series of changes costs one drawing.
    public sealed class CardImageQueue
    {
        public const int Workers = 2;
        public const int MaxPending = 1000;
        public static readonly TimeSpan DefaultQuiet = TimeSpan.FromSeconds(10);
        public static readonly TimeSpan ReportInterval = TimeSpan.FromHours(1);
        private static readonly TimeSpan DefaultTick = TimeSpan.FromSeconds(1);

        private readonly record struct Entry(long Due, bool Force);

        private sealed class Counters
        {
            public long Drawn, Forced, AlreadyDrawn, NoPicture, Failed, Removed, Rejected, LateRequests;
        }

        private readonly Func<ulong, bool, Task<CardDrawing>> _generate;
        private readonly ILogger _logger;
        private readonly Func<long> _nowMs;
        private readonly long _quietMs;
        private readonly TimeSpan _tick;
        private readonly ConcurrentDictionary<ulong, Entry> _pending = new ConcurrentDictionary<ulong, Entry>();
        private readonly Channel<(ulong Id, bool Force)> _due = Channel.CreateUnbounded<(ulong, bool)>();
        private Counters _counters = new Counters();
        private long _lastReportMs;
        private Timer _report;
        private volatile bool _overflowLogged;

        public CardImageQueue(Func<ulong, bool, Task<CardDrawing>> generate, ILogger logger, TimeSpan? quiet = null, TimeSpan? tick = null,
            Func<long> nowMs = null)
        {
            _generate = generate;
            _logger = logger;
            _quietMs = (long)(quiet ?? DefaultQuiet).TotalMilliseconds;
            _tick = tick ?? DefaultTick;
            _nowMs = nowMs ?? (() => Environment.TickCount64);
            _lastReportMs = _nowMs();
        }

        public int Pending => _pending.Count;

        // a request for a card image that was not ready in time
        public void CountLateRequest() => Interlocked.Increment(ref _counters.LateRequests);

        public string FlushReport()
        {
            var c = Interlocked.Exchange(ref _counters, new Counters());
            var now = _nowMs();
            var minutes = (now - Interlocked.Exchange(ref _lastReportMs, now)) / 60000.0;
            var pending = Pending;

            if (c.Drawn + c.AlreadyDrawn + c.NoPicture + c.Failed + c.Removed + c.Rejected + c.LateRequests + pending == 0)
                return null;

            return FormattableString.Invariant($"Card images ({minutes:0} min): background {c.Drawn} drawn ({c.Forced} forced), {c.AlreadyDrawn} already drawn, ")
                + $"{c.NoPicture} without picture, {c.Removed} removed cards, {c.Failed} failed, {c.Rejected} not queued (queue full), {pending} waiting; "
                + $"{c.LateRequests} requests not ready in time (503)";
        }

        private void Count(CardDrawing result, bool force)
        {
            var c = _counters;
            switch (result)
            {
                case CardDrawing.Drawn:
                    Interlocked.Increment(ref c.Drawn);
                    if (force)
                        Interlocked.Increment(ref c.Forced);
                    break;
                case CardDrawing.AlreadyDrawn: Interlocked.Increment(ref c.AlreadyDrawn); break;
                case CardDrawing.NoPicture: Interlocked.Increment(ref c.NoPicture); break;
                case CardDrawing.Removed: Interlocked.Increment(ref c.Removed); break;
                default: Interlocked.Increment(ref c.Failed); break;
            }
        }

        // force: the saved images may be drawn from the card before this change, so they are drawn again even if they exist
        public bool Enqueue(ulong cardId, bool force)
        {
            if (cardId == 0)
                return false;

            if (!_pending.ContainsKey(cardId) && _pending.Count >= MaxPending)
            {
                Interlocked.Increment(ref _counters.Rejected);
                if (!_overflowLogged)
                {
                    _overflowLogged = true;
                    _logger?.LogRoutine($"Waifu: card image queue is full ({MaxPending}), further cards are drawn when they are shown");
                }
                return false;
            }

            var due = _nowMs() + _quietMs;
            _pending.AddOrUpdate(cardId, _ => new Entry(due, force), (_, old) => new Entry(due, old.Force || force));
            return true;
        }

        internal List<(ulong Id, bool Force)> TakeDue()
        {
            var now = _nowMs();
            var due = new List<(ulong, bool)>();
            foreach (var item in _pending)
            {
                if (item.Value.Due <= now && _pending.TryRemove(item))
                    due.Add((item.Key, item.Value.Force));
            }

            if (_pending.Count < MaxPending / 2)
                _overflowLogged = false;

            return due;
        }

        public void Start(CancellationToken stop = default)
        {
            _report = new Timer(_ =>
            {
                var report = FlushReport();
                if (report != null) _logger?.LogReport(report);
            }, null, ReportInterval, ReportInterval);
            stop.Register(() => _report.Dispose());

            _ = Task.Run(() => SweepAsync(stop));
            for (int i = 0; i < Workers; i++)
                _ = Task.Run(() => WorkAsync(stop));
        }

        private async Task SweepAsync(CancellationToken stop)
        {
            while (true)
            {
                await Task.Delay(_tick, stop);
                try
                {
                    foreach (var item in TakeDue())
                        _due.Writer.TryWrite(item);
                }
                catch (Exception ex)
                {
                    _logger?.LogError($"Waifu: card image queue: {ex.Message}");
                }
            }
        }

        private async Task WorkAsync(CancellationToken stop)
        {
            await foreach (var (id, force) in _due.Reader.ReadAllAsync(stop))
            {
                try
                {
                    Count(await _generate(id, force), force);
                }
                catch (Exception ex)
                {
                    Count(CardDrawing.Failed, force);
                    _logger?.LogError($"Waifu: drawing card {id} in the background: {ex.Message}");
                }
            }
        }
    }
}
