#pragma warning disable 1591

using System;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Sanakan.Config;
using Shinden.Logger;

namespace Sanakan.Services
{
    public class Daemonizer
    {
        private static readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(40);

        private readonly object _lock = new object();
        private CancellationTokenSource _cts { get; set; }
        private Func<ConnectionState> _state { get; set; }
        private Action<int> _exit { get; set; }
        private TimeSpan _timeout { get; set; }
        private ILogger _logger { get; set; }
        private IConfig _config { get; set; }

        public Daemonizer(DiscordSocketClient client, ILogger logger, IConfig config)
            : this(() => client.ConnectionState, logger, config, _defaultTimeout, Environment.Exit)
        {
            client.Connected += () => { HandleConnected(); return Task.CompletedTask; };
            client.Disconnected += _ => { HandleDisconnected(); return Task.CompletedTask; };
        }

        public Daemonizer(Func<ConnectionState> state, ILogger logger, IConfig config, TimeSpan timeout, Action<int> exit)
        {
            _state = state;
            _logger = logger;
            _config = config;
            _timeout = timeout;
            _exit = exit;
            _cts = new CancellationTokenSource();
        }

        public void HandleConnected()
        {
            lock (_lock)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = new CancellationTokenSource();
            }
        }

        public void HandleDisconnected()
        {
            CancellationToken token;
            lock (_lock)
            {
                token = _cts.Token;
            }

            _ = WatchReconnectAsync(token);
        }

        private async Task WatchReconnectAsync(CancellationToken token)
        {
            try
            {
                await Task.Delay(_timeout, token);

                if (!_config.Get().Demonization) return;
                _logger.Log("Disconnected! Running demonization check.");

                await Task.Delay(_timeout, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (_state() == ConnectionState.Connected)
            {
                _logger.Log("Reconnected!");
                return;
            }

            _logger.LogError("Timeout! Shutting down!");
            _exit(1);
        }
    }
}
