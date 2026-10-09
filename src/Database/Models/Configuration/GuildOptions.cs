#pragma warning disable 1591

using System.Collections.Generic;

namespace Sanakan.Database.Models.Configuration
{
    public class GuildOptions
    {
        public ulong Id { get; set; }
        public ulong MuteRole { get; set; }
        public ulong ModMuteRole { get; set; }
        public ulong UserRole { get; set; }
        public ulong AdminRole { get; set; }
        public ulong SemiAdminRole { get; set; }
        public ulong TesterRole { get; set; }
        public ulong GlobalEmotesRole { get; set; }
        public ulong WaifuRole { get; set; }
        public ulong NotificationChannel { get; set; }
        public ulong RaportChannel { get; set; }
        public ulong QuizChannel { get; set; }
        public ulong ToDoChannel { get; set; }
        public ulong NsfwChannel { get; set; }
        public ulong LogChannel { get; set; }
        public ulong GreetingChannel { get; set; }
        public ulong NitroRole { get; set; }
        public ulong AlwaysBanChannel { get; set; }
        public string WelcomeMessage { get; set; }
        public string WelcomeMessagePW { get; set; }
        public string GoodbyeMessage { get; set; }
        public long SafariLimit { get; set; }
        public bool Supervision { get; set; }
        public bool ChaosMode { get; set; }
        public string Prefix { get; set; }
        public string NoUserRoleHelp { get; set; }

        public virtual Waifu WaifuConfig { get; set; }

        public virtual ICollection<WithoutSupervisionChannel> ChannelsWithoutSupervision { get; set; } = new List<WithoutSupervisionChannel>();
        public virtual ICollection<WithoutMsgCntChannel> IgnoredChannels { get; set; } = new List<WithoutMsgCntChannel>();
        public virtual ICollection<WithoutExpChannel> ChannelsWithoutExp { get; set; } = new List<WithoutExpChannel>();
        public virtual ICollection<CommandChannel> CommandChannels { get; set; } = new List<CommandChannel>();
        public virtual ICollection<ModeratorRoles> ModeratorRoles { get; set; } = new List<ModeratorRoles>();
        public virtual ICollection<LevelRole> RolesPerLevel { get; set; } = new List<LevelRole>();
        public virtual ICollection<SelfRole> SelfRoles { get; set; } = new List<SelfRole>();
        public virtual ICollection<Raport> Raports { get; set; } = new List<Raport>();
        public virtual ICollection<MyLand> Lands { get; set; } = new List<MyLand>();
    }
}
