#pragma warning disable 1591

using Sanakan.Api.Models;

namespace Sanakan.Services.PocketWaifu
{
    public class TagIcon
    {
        public TagIcon(ulong id, string name, string icon)
        {
            Id = id;
            Name = name;
            Icon = icon;
        }

        public ulong Id { get; }
        public string Name { get; }
        public string Icon { get; }

        public TagIdPair ToView() => new TagIdPair { Id = Id, Name = Name };
    }
}