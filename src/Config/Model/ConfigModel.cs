#pragma warning disable 1591

using System.Collections.Generic;

namespace Sanakan.Config.Model
{
    public class ConfigModel
    {
        public string Prefix { get; set; }
        public string BotToken { get; set; }
        public bool Supervision { get; set; }
        public bool Demonization { get; set; }
        public bool SafariEnabled { get; set; }
        public bool AutoCleanCards { get; set; }
        public string ConnectionString { get; set; }
        public long CharPerPacket { get; set; }
        public long PacksPerDay { get; set; }
        public ConfigShinden Shinden { get; set; }
        public ConfigExp Exp { get; set; }
        public List<ulong> Dev { get; set; }
        public JwtConfig Jwt { get; set; }
        public bool GiveBanForUrlSpam { get; set; }
        public List<SanakanApiKey> ApiKeys { get; set; }
        public List<ApiApp> UserKeyApps { get; set; }
        public List<RichMessageConfig> RMConfig { get; set; }
        public List<ulong> BlacklistedGuilds { get; set; }
        public LogChannelConfig LogChannel { get; set; } = new LogChannelConfig { GuildId = 428569745579704321, ChannelId = 1556085068051513394 };
        public HeartbeatConfig Heartbeat { get; set; }
    }
}