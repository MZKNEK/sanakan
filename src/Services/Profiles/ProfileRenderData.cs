#pragma warning disable 1591

using System;
using System.Collections.Generic;
using Sanakan.Database.Models;

namespace Sanakan.Services.Profiles
{
    public enum KarmaLook { None, Neutral, Angel, Demon }

    public class ProfileRecentTitle
    {
        public string Title { get; set; }
        public string Progress { get; set; }
        public double? Ratio { get; set; }
        public string CoverUrl { get; set; }
    }

    // a list on Shinden as the panel shows it, the numbers already formatted
    public class ProfileListStats
    {
        // in progress, completed, skipped, on hold, dropped, in plan
        public ulong[] Statuses { get; set; } = new ulong[6];
        public string Total { get; set; } = "0";
        public string Units { get; set; } = "0";
        public string Time { get; set; } = "—";
        public string Mean { get; set; } = "—";
        public List<ProfileRecentTitle> Recent { get; set; } = new List<ProfileRecentTitle>();
    }

    // everything the profile shows of a player, gathered before drawing
    public class ProfileRenderData
    {
        public string Nick { get; set; } = "";
        public string Rank { get; set; } = "";
        public string AvatarUrl { get; set; }
        // "#rrggbb"
        public string RoleColor { get; set; } = "#546E7A";

        public long Top { get; set; }
        public long Level { get; set; }
        // how far into the level, 0 to 1
        public double LevelProgress { get; set; }
        public ulong Messages { get; set; }
        public long Sc { get; set; }
        public long Tc { get; set; }
        public long Ac { get; set; }
        public long Ct { get; set; }
        public long Pc { get; set; }

        public int Cards { get; set; }
        public long Limit { get; set; }
        public KarmaLook Karma { get; set; }
        public Dictionary<Rarity, int> Rarities { get; set; } = new Dictionary<Rarity, int>();
        public int Ultimates { get; set; }
        public int CustomImages { get; set; }
        public int CustomBorders { get; set; }

        public Card Waifu { get; set; }
        public List<Card> Gallery { get; set; } = new List<Card>();
        public Func<ulong, Card> Card { get; set; } = _ => null;

        public ProfileListStats Anime { get; set; }
        public ProfileListStats Manga { get; set; }

        // a picture of the player by its id: a local path or a link, null when it is gone
        public Func<long, string> ImagePath { get; set; } = _ => null;
    }
}
