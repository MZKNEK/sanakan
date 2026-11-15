#pragma warning disable 1591

using System;

namespace Sanakan.Database.Models
{
    public enum ProfileImageUse
    {
        Header = 0,
        Style = 1,
        Overlay = 2,
        Ultra = 3,
        Frame = 4,
    }

    public enum ProfileUnlockSource
    {
        Bought = 0,
        // what the player had set with the old profile commands
        Migration = 1,
        Admin = 2,
    }

    // the profile of layers of a player (Services/Profiles/ProfileLayout as JSON); a player without one has the default
    public class UserProfile
    {
        public ulong UserId { get; set; }
        public string Layout { get; set; }
        // raised on every save, so two editors cannot overwrite each other unnoticed
        public int Version { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    // a one-time unlock of a profile option, e.g. "overlay:2" or "frame:Water"
    public class ProfileUnlock
    {
        public ulong UserId { get; set; }
        public string Feature { get; set; }
        public ProfileUnlockSource Source { get; set; }
        // the currency and price paid, null and 0 when it was not bought
        public Services.PocketWaifu.CurrencyType? Currency { get; set; }
        public long Price { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    // a picture of a player's profile, kept as a file of the bot; the layout points at it by its id
    public class ProfilePicture
    {
        public ulong Id { get; set; }
        public ulong UserId { get; set; }
        public ProfileImageUse Use { get; set; }
        public string File { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool Animated { get; set; }
        public string Sha256 { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
