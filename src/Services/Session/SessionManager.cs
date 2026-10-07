#pragma warning disable 1591

using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Sanakan.Services.Executor;
using Shinden.Logger;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Sanakan.Services.Session
{
    public class SessionManager : IDisposable
    {
        private DiscordSocketClient _client;
        private IServiceProvider _provider;
        private IExecutor _executor;
        private ILogger _logger;
        private Timer _timer;

        private readonly object _lock = new object();
        private readonly List<ISession> _sessions = new List<ISession>();
        private readonly SemaphoreSlim _autoValidate = new SemaphoreSlim(1, 1);

        public SessionManager(DiscordSocketClient client, IExecutor executor, ILogger logger)
        {
            _client = client;
            _logger = logger;
            _executor = executor;
        }

        public void Initialize(IServiceProvider provider)
        {
            _provider = provider;

            _timer = new Timer(async _ =>
            {
                if (!_autoValidate.Wait(0))
                    return;

                try
                {
                    await AutoValidate();
                }
                finally
                {
                    _autoValidate.Release();
                }
            },
            null,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(30));

            _client.MessageReceived += HandleMessageAsync;
            _client.ReactionAdded += HandleReactionAddedAsync;
            _client.ReactionRemoved += HandleReactionRemovedAsync;
        }

        public Task<bool> TryAddSession<T>(T session) where T : ISession
        {
            lock (_lock)
            {
                if (SessionExistUnsafe(session))
                    return Task.FromResult(false);

                if (_sessions.Count < 1)
                    ToggleAutoValidation(true);

                _sessions.Add(session);
                session.MarkAsAdded();
            }

            return Task.FromResult(true);
        }

        public async Task KillSessionIfExistAsync<T>(T session) where T : ISession
        {
            ISession thisSession;
            lock (_lock)
            {
                thisSession = _sessions.FirstOrDefault(x => x.IsOwner(session.GetOwner())
                    && ((x.GetId() == null) ? (x is T) : (x.GetId() == session.GetId())));
            }

            if (thisSession != null) await DisposeAsync(thisSession).ConfigureAwait(false);
        }

        public bool SessionExist<T>(T session) where T : ISession
        {
            lock (_lock)
            {
                return SessionExistUnsafe(session);
            }
        }

        public bool SessionExist(IUser user, Type sessionType)
        {
            lock (_lock)
            {
                return _sessions.Where(x => x.IsOwner(user)).Any(x => (x.GetType() == sessionType));
            }
        }

        private bool SessionExistUnsafe<T>(T session) where T : ISession
            => _sessions.Where(x => x.IsOwner(session.GetParticipants()))
                .Any(x => ((x.GetId() == null) ? (x is T) : (x.GetId() == session.GetId())));

        private List<ISession> FindSessions(Func<ISession, bool> predicate)
        {
            lock (_lock)
            {
                return _sessions.Where(predicate).ToList();
            }
        }

        private async Task DisposeAsync(ISession session)
        {
            lock (_lock)
            {
                if (!_sessions.Remove(session))
                    return;
            }

            await session.DisposeAsync().ConfigureAwait(false);
        }

        private async Task RunSessions(List<ISession> sessions, SessionContext context)
        {
            foreach (var session in sessions)
            {
                if (!session.IsValid())
                {
                    await DisposeAsync(session);
                    continue;
                }

                session.WithLogger(_logger);

                switch (session.GetRunMode())
                {
                    case RunMode.Async:
                        _ = Task.Run(async () =>
                        {
                            if (await session.GetExecutable(context).ExecuteAsync(_provider).ConfigureAwait(false))
                                await DisposeAsync(session).ConfigureAwait(false);
                        });
                        break;

                    default:
                    case RunMode.Sync:
                        session.SetDestroyer(DisposeAsync);
                        if (!await _executor.TryAdd(session.GetExecutable(context), TimeSpan.FromSeconds(1)))
                                _logger.Log($"Sessions: {session.GetEventType()}-{session.GetOwner().Id} waiting time has been exceeded!");
                        break;
                }
            }
        }

        private async Task HandleMessageAsync(SocketMessage message)
        {
            var msg = message as SocketUserMessage;
            if (msg == null) return;

            if (msg.Author.IsBot || msg.Author.IsWebhook) return;

            var userSessions = FindSessions(x => x.IsOwner(message.Author)
                && x.GetEventType().HasFlag(ExecuteOn.Message));

            if (userSessions.Count == 0) return;

            await RunSessions(userSessions, new SessionContext(new SocketCommandContext(_client, msg))).ConfigureAwait(false);
        }

        private async Task HandleReactionAddedAsync(Cacheable<IUserMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel, SocketReaction reaction)
        {
            if (!reaction.User.IsSpecified) return;
            var user = reaction.User.Value;

            if ((user.IsBot || user.IsWebhook)) return;

            var userSessions = FindSessions(x => x.IsOwner(user)
                && x.GetEventType().HasFlag(ExecuteOn.ReactionAdded));

            if (userSessions.Count == 0) return;

            var thisUser = _client.GetUser(user.Id);
            if (thisUser == null) return;

            var chan = await channel.GetOrDownloadAsync();
            if (chan == null) return;

            var msg = await chan.GetMessageAsync(message.Id);
            if (msg == null) return;

            var thisMessage = msg as IUserMessage;
            if (thisMessage == null) return;

            await RunSessions(userSessions, new SessionContext(chan, thisUser, thisMessage, _client, reaction, true)).ConfigureAwait(false);
        }

        private async Task HandleReactionRemovedAsync(Cacheable<IUserMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel, SocketReaction reaction)
        {
            if (!reaction.User.IsSpecified) return;
            var user = reaction.User.Value;

            if ((user.IsBot || user.IsWebhook)) return;

            var userSessions = FindSessions(x => x.IsOwner(user)
                && x.GetEventType().HasFlag(ExecuteOn.ReactionRemoved));

            if (userSessions.Count == 0) return;

            var thisUser = _client.GetUser(user.Id);
            if (thisUser == null) return;

            var chan = await channel.GetOrDownloadAsync();
            if (chan == null) return;

            var msg = await chan.GetMessageAsync(message.Id);
            if (msg == null) return;

            var thisMessage = msg as IUserMessage;
            if (thisMessage == null) return;

            await RunSessions(userSessions, new SessionContext(chan, thisUser, thisMessage, _client, reaction, false)).ConfigureAwait(false);
        }

        private void ToggleAutoValidation(bool on)
        {
            if (on)
                _timer.Change(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30));
            else
                _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        private async Task AutoValidate()
        {
            lock (_lock)
            {
                if (_sessions.Count < 1)
                {
                    ToggleAutoValidation(false);
                    return;
                }
            }

            try
            {
                foreach (var session in FindSessions(x => !x.IsValid()))
                    await DisposeAsync(session);
            }
            catch(Exception ex)
            {
                _logger.LogError($"Session: autovalidate error {ex}");
            }
        }

        public void Dispose()
        {
            _timer?.Dispose();
            _autoValidate.Dispose();
        }
    }
}
