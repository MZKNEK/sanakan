#pragma warning disable 1591

using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Sanakan.Api.Models
{
    public class ProfileFeatureView
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public long Sc { get; set; }
        public long Tc { get; set; }
        public bool TcOnly { get; set; }
    }

    public class ProfilePresetView
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public JToken Layout { get; set; }
    }

    public class ProfileFrameView
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public bool Free { get; set; }
        public bool Selectable { get; set; }
    }

    // everything Profilator needs to know without a player: prices, ready layouts, frames, limits
    public class ProfileCatalogView
    {
        public int LayoutVersion { get; set; }
        public int MaxStickers { get; set; }
        public int MaxOverlays { get; set; }
        public int MaxLayoutJson { get; set; }
        public int MaxPictureBytes { get; set; }
        public List<ProfileFeatureView> Features { get; set; }
        public List<ProfilePresetView> Presets { get; set; }
        public List<ProfileFrameView> Frames { get; set; }
    }

    public class ProfilePictureView
    {
        public ulong Id { get; set; }
        public string Use { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool Animated { get; set; }
    }

    public class ProfileCardView
    {
        public ulong Id { get; set; }
        public string Name { get; set; }
        public string Rarity { get; set; }
        public bool Animated { get; set; }
        public string Url { get; set; }
    }

    public class ProfileRecentView
    {
        public string Title { get; set; }
        public string Progress { get; set; }
        public double? Ratio { get; set; }
        public string Cover { get; set; }
    }

    public class ProfileListView
    {
        public ulong[] Statuses { get; set; }
        public string Total { get; set; }
        public string Units { get; set; }
        public string Time { get; set; }
        public string Mean { get; set; }
        public List<ProfileRecentView> Recent { get; set; }
    }

    // what the profile shows of the player, for Profilator's own drawing
    public class ProfileDataView
    {
        public string Nick { get; set; }
        public string Rank { get; set; }
        public string Avatar { get; set; }
        public string Color { get; set; }
        public long Top { get; set; }
        public long Level { get; set; }
        public double LevelProgress { get; set; }
        public ulong Messages { get; set; }
        public long Sc { get; set; }
        public long Tc { get; set; }
        public long Ac { get; set; }
        public long Ct { get; set; }
        public long Pc { get; set; }
        public int Cards { get; set; }
        public long Limit { get; set; }
        public string Karma { get; set; }
        public Dictionary<string, int> Rarities { get; set; }
        public int Ultimates { get; set; }
        public int CustomImages { get; set; }
        public int CustomBorders { get; set; }
        public ProfileCardView Waifu { get; set; }
        public List<ProfileCardView> Gallery { get; set; }
        public ProfileListView Anime { get; set; }
        public ProfileListView Manga { get; set; }
    }

    public class ProfileEditorView
    {
        public JToken Layout { get; set; }
        // what a save must send back; 0 while the player has no stored layout
        public int Version { get; set; }
        public List<string> Unlocks { get; set; }
        public List<ProfilePictureView> Pictures { get; set; }
        // the cards that can go on the profile as stickers
        public List<ProfileCardView> Cards { get; set; }
        public ProfileDataView Data { get; set; }
    }

    public class ProfileQuoteView
    {
        public List<string> Errors { get; set; }
        public List<ProfileFeatureView> Missing { get; set; }
        public long Sc { get; set; }
        public long Tc { get; set; }
        public long WalletSc { get; set; }
        public long WalletTc { get; set; }
        // something of the layout is sold only for TC, so the save is paid in TC
        public bool TcOnly { get; set; }
    }

    public class ProfileSaveRequest
    {
        public JToken Layout { get; set; }
        // "sc" or "tc"; only needed when something is to be paid
        public string Currency { get; set; }
        public int Version { get; set; }
    }

    public class ProfileSaveView
    {
        public int Version { get; set; }
        public string Currency { get; set; }
        public long Paid { get; set; }
        public List<string> Unlocked { get; set; }
    }
}
