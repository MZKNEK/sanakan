#pragma warning disable 1591

using Discord;
using Discord.Commands;
using Sanakan.Services.Executor;
using Shinden.Logger;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Sanakan.Services.Session
{
    public class Session : ISession
    {
        protected List<IUser> _owners = new List<IUser>();

        private Func<ISession, Task> OnSyncEnd { get; set; }
        private Stopwatch _timer { get; set; }
        private ILogger _logger { get; set; }
        private bool _added { get; set; }
        private volatile bool _disposed;

        public Session(IUser owner)
        {
            _owners.Add(owner);
            _added = false;

            RunMode = RunMode.Sync;
            TimeoutMs = 30000;

            OnDispose = null;
            OnExecute = null;
            OnSyncEnd = null;
            Id = null;
        }

        // Session
        public string Id { get; set; }
        public RunMode RunMode { get; set; }
        public ExecuteOn Event { get; set; }
        public long TimeoutMs { get; set; }
        public Func<Task> OnDispose { get; set; }
        public Func<SessionContext, Session, Task<bool>> OnExecute { get; set; }
        public void AddParticipant(IUser participant) => _owners.Add(participant);

        public void RestartTimer()
        {
            if (_added && TimeoutMs > 0)
                _timer = Stopwatch.StartNew();
        }

        // ISession
        public string GetId() => Id;
        public RunMode GetRunMode() => RunMode;
        public ExecuteOn GetEventType() => Event;
        public IUser GetOwner() => _owners.First();
        public void WithLogger(ILogger logger) => _logger = logger;
        public IEnumerable<IUser> GetParticipants() => _owners.AsReadOnly();
        public bool IsOwner(IUser user) => _owners.Any(x => x.Id == user.Id);
        public void SetDestroyer(Func<ISession, Task> destroyer) => OnSyncEnd = destroyer;
        public bool IsOwner(IEnumerable<IUser> user) => _owners.Any(x => user.Any(z => z.Id == x.Id));

        public void MarkAsAdded()
        {
            if (!_added)
            {
                _added = true;

                if (TimeoutMs > 0)
                    _timer = Stopwatch.StartNew();
            }
        }

        public bool IsValid()
        {
            if (_disposed) return false;
            if (_timer == null) return true;
            return _timer.ElapsedMilliseconds <= TimeoutMs;
        }

        public async Task DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            var onDispose = OnDispose;
            OnDispose = null;
            OnExecute = null;
            OnSyncEnd = null;
            _timer = null;

            if (onDispose != null)
                await onDispose();
        }

        public IExecutable GetExecutable(SessionContext context)
        {
            var task = new Executable($"session-{this}", new Func<Task<bool>>(async () =>
            {
                var onExecute = OnExecute;
                if (onExecute == null)
                    return true;

                try
                {
                    var res = await onExecute(context, this).ConfigureAwait(false);
                    var onSyncEnd = OnSyncEnd;
                    if (res && RunMode == RunMode.Sync && onSyncEnd != null)
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await Task.Delay(500);
                                await onSyncEnd(this);
                            }
                            catch (Exception ex)
                            {
                                _logger?.LogError($"Session: onSyncEnd: {ex}");
                            }
                        });
                    }

                    return res;
                }
                catch (Exception ex)
                {
                    if (_logger != null)
                    {
                        string sessionName = Id ?? this.ToString();
                        _logger.LogError($"In {sessionName} session: {ex}");
                    }
                    return false;
                }
            }), _owners.First().Id);

            foreach (var u in _owners)
                task.AddOwner(u.Id);

            return task;
        }
    }
}