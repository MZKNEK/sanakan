#pragma warning disable 1591

namespace Sanakan.Database.Models.Management
{
    public enum ModifierType
    {
        Constant, Growing
    }

    public class MuteModifier
    {
        public ulong Id { get; set; }
        public ulong User { get; set; }
        public ulong Guild { get; set; }
        public ModifierType Type { get; set; }
        public long Value { get; set; }
        public long Count { get; set; }
    }
}
