#pragma warning disable 1591

using Discord;
using Discord.Net;
using Discord.Rest;
using Sanakan.Config;
using Shinden.Logger;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Sanakan.Services
{
    public class DiscordChannelLogger : ILogger
    {
        public const int MaxMessageLength = 2000;
        private const int MaxPending = 2000;
        private const int MaxMessagesPerFlush = 5;
        private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(5);

        private const string BlockStart = "```diff\n";
        private const string BlockEnd = "\n```";

        private static readonly string[] _routinePrefixes =
        {
            "Executor: running ",
            "Executor: completed ",
            "Run cmd: ",
            "mem usage: ",
            "Reconnected!",
            // Shinden.NET
            "Processing request: ",
            "Request successful.",
            "Parsing response.",
            "Response code: ",
            "Response body: ",
            "Request body: ",
        };

        private readonly ConsoleLogger _console;
        private readonly IConfig _config;
        private readonly DiscordRestClient _rest = new DiscordRestClient();
        private readonly Timer _timer;
        private readonly SemaphoreSlim _sending = new SemaphoreSlim(1, 1);

        private readonly object _lock = new object();
        private readonly Queue<string> _pending = new Queue<string>();
        private int _dropped = 0;

        private ITextChannel _channel;
        private ulong _missingChannelId;

        public DiscordChannelLogger(ConsoleLogger console, IConfig config)
        {
            _console = console;
            _config = config;
            _timer = new Timer(_ => _ = FlushAsync(), null, FlushInterval, FlushInterval);
        }

        public void Log(string message)
        {
            _console.Log(message);

            if (!IsEnabled() || !ShouldSend(message))
                return;

            var isError = message.StartsWith(LoggerExtensions.ErrorPrefix, StringComparison.Ordinal);
            var text = isError ? message.Substring(LoggerExtensions.ErrorPrefix.Length) : message;
            var entry = $"[{DateTime.Now:HH:mm:ss}] {(isError ? "ERROR " : "")}{_console.MaskSecrets(text)}";
            if (isError)
                entry = string.Join("\n", entry.Replace("\r", "").Split('\n').Select(x => "- " + x));
            lock (_lock)
            {
                _pending.Enqueue(entry);
                while (_pending.Count > MaxPending)
                {
                    _pending.Dequeue();
                    ++_dropped;
                }
            }
        }

        public static bool ShouldSend(string message)
            => !string.IsNullOrWhiteSpace(message)
                && !_routinePrefixes.Any(x => message.StartsWith(x, StringComparison.Ordinal));

        public async Task FlushAllAsync(TimeSpan timeout)
        {
            _timer.Change(Timeout.Infinite, Timeout.Infinite);

            using var cts = new CancellationTokenSource(timeout);
            try
            {
                await _sending.WaitAsync(cts.Token).ConfigureAwait(false);
                try
                {
                    while (!cts.IsCancellationRequested && await SendPendingAsync(cts.Token).ConfigureAwait(false)) { }
                }
                finally
                {
                    _sending.Release();
                }
            }
            catch (Exception ex)
            {
                _console.Log($"DiscordLog: {ex.Message}");
            }
        }

        public static List<string> Pack(Queue<string> pending, string header, int maxMessages, int maxLength = MaxMessageLength)
        {
            var bodyLimit = maxLength - BlockStart.Length - BlockEnd.Length;
            var messages = new List<string>();
            var body = new StringBuilder();

            if (!string.IsNullOrEmpty(header))
                body.Append(Sanitize(header, bodyLimit));

            while (messages.Count < maxMessages && pending.Count > 0)
            {
                var line = Sanitize(pending.Peek(), bodyLimit);
                var needed = body.Length == 0 ? line.Length : body.Length + 1 + line.Length;
                if (needed > bodyLimit)
                {
                    messages.Add(BlockStart + body + BlockEnd);
                    body.Clear();
                    continue;
                }

                if (body.Length > 0)
                    body.Append('\n');

                body.Append(line);
                pending.Dequeue();
            }

            if (body.Length > 0)
                messages.Add(BlockStart + body + BlockEnd);

            return messages;
        }

        public static string Sanitize(string entry, int maxLength)
        {
            entry = (entry ?? "").Replace("```", "`​`​`");
            return entry.Length > maxLength ? entry[..(maxLength - 1)] + "…" : entry;
        }

        private bool IsEnabled()
        {
            var cfg = _config?.Get()?.LogChannel;
            return cfg != null && cfg.GuildId != 0 && cfg.ChannelId != 0 && cfg.ChannelId != _missingChannelId;
        }

        private async Task FlushAsync()
        {
            if (!await _sending.WaitAsync(0).ConfigureAwait(false))
                return;

            try
            {
                await SendPendingAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _console.Log($"DiscordLog: {ex.Message}");
            }
            finally
            {
                _sending.Release();
            }
        }

        private async Task<bool> SendPendingAsync(CancellationToken token)
        {
            var channel = await GetChannelAsync(token).ConfigureAwait(false);
            if (channel == null)
                return false;

            List<string> messages;
            lock (_lock)
            {
                var header = _dropped > 0 ? $"[pominięto {_dropped} wpisów - zbyt dużo logów]" : null;
                _dropped = 0;
                messages = Pack(_pending, header, MaxMessagesPerFlush);
            }

            if (messages.Count == 0)
                return false;

            var options = new RequestOptions { CancelToken = token };
            foreach (var msg in messages)
                await channel.SendMessageAsync(msg, allowedMentions: AllowedMentions.None, options: options).ConfigureAwait(false);

            return true;
        }

        private async Task<ITextChannel> GetChannelAsync(CancellationToken token)
        {
            if (!IsEnabled())
                return null;

            var config = _config.Get();
            var cfg = config.LogChannel;
            if (_channel != null && _channel.Id == cfg.ChannelId && _channel.GuildId == cfg.GuildId)
                return _channel;

            if (_rest.LoginState != LoginState.LoggedIn)
                await _rest.LoginAsync(TokenType.Bot, config.BotToken).ConfigureAwait(false);

            ITextChannel channel = null;
            try
            {
                channel = await _rest.GetChannelAsync(cfg.ChannelId, new RequestOptions { CancelToken = token }).ConfigureAwait(false) as ITextChannel;
            }
            catch (HttpException) { }

            if (channel == null || channel.GuildId != cfg.GuildId)
            {
                _missingChannelId = cfg.ChannelId;
                lock (_lock)
                {
                    _pending.Clear();
                    _dropped = 0;
                }
                _console.Log($"DiscordLog: brak dostępu do kanału {cfg.ChannelId} na serwerze {cfg.GuildId}, logi zostają tylko na konsoli.");
                return null;
            }

            _channel = channel;
            return channel;
        }
    }
}
