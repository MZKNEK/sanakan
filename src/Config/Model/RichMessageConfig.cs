#pragma warning disable 1591

using System;
using Sanakan.Api.Models;

namespace Sanakan.Config.Model
{
    public class RichMessageConfig
    {
        public ulong RoleId { get; set; }
        public ulong GuildId { get; set; }
        public ulong ChannelId { get; set; }
        public RichMessageType Type { get; set; }
        public string WebHookUrl { get; set; }

        public override string ToString()
        {
            if (!string.IsNullOrEmpty(WebHookUrl)) return $"Webhook:\nTyp: {Type}\nUrl: {MaskUrl(WebHookUrl)}";
            return $"Serwer: {GuildId}\nRola: {RoleId}\nKanał: {ChannelId}\nTyp: {Type}";
        }

        private static string MaskUrl(string url)
            => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? $"{uri.Scheme}://{uri.Host}/****" : "****";
    }
}
