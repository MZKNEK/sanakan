#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Sanakan.Config;
using Sanakan.Services.Time;

namespace Sanakan.Services
{
    public class EmoteCounter
    {
        private ISystemTime _time;
        private DiscordSocketClient _client;
        private EmotesStats _stats;

        public class EmotesStats
        {
            public Dictionary<string, long> Counter;
            public DateTime Start;
        }

        public EmoteCounter(DiscordSocketClient client, ISystemTime time)
        {
            _time = time;
            _client = client;
            _stats = new EmotesStats { Counter = new Dictionary<string, long>(), Start = _time.Now() };
            LoadDumpedData();

            _client.MessageReceived += HandleMessageAsync;
        }

        public EmotesStats GetEmotesStats() => _stats;

        public void ResetStats()
        {
            _stats.Counter.Clear();
            _stats.Start = _time.Now();
        }

        private Task HandleMessageAsync(SocketMessage message)
        {
            var msg = message as SocketUserMessage;
            if (msg == null) return Task.CompletedTask;

            if (msg.Author.IsBot || msg.Author.IsWebhook)
                return Task.CompletedTask;

            foreach (var tag in msg.Tags)
            {
                if (tag.Type == TagType.Emoji)
                {
                    if (tag.Value is Emote em)
                    {
                        if (!_stats.Counter.ContainsKey($"{em}"))
                        {
                            _stats.Counter.Add($"{em}", 1);
                            continue;
                        }
                        _stats.Counter[$"{em}"] += 1;
                    }
                }
            }
            return Task.CompletedTask;
        }

        public void DumpData()
        {
            try
            {
                var file = GetReader();
                file.Save(_stats);
            }
            catch (Exception) { }
        }

        private void LoadDumpedData()
        {
            try
            {
                var file = GetReader();
                if (file.Exist())
                {
                    var oldData = file.Load<EmotesStats>();
                    if (oldData != null && oldData?.Counter?.Count > 0)
                    {
                        _stats = oldData;
                    }
                    file.Delete();
                }
            }
            catch (Exception) { }
        }

        private JsonFileReader GetReader() => new Config.JsonFileReader("./dumpEmotes.json");
    }
}