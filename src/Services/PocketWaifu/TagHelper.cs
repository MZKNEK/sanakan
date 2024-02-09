#pragma warning disable 1591

using System.Collections.Generic;
using System.Linq;
using Sanakan.Database;
using Sanakan.Database.Models;
using Sanakan.Extensions;

namespace Sanakan.Services.PocketWaifu
{
    public enum TagType
    {
        Favorite, Gallery, Reservation, Exchange, TrashBin
    }

    public class TagHelper
    {
        private readonly Dictionary<TagType, TagIcon> _baseTags = new Dictionary<TagType, TagIcon>();

        public TagHelper(DatabaseContext db)
        {
            var setup = new List<(TagType type, string  name, string icon, ulong id)>
            {
                (TagType.Favorite,    "ulubione",   "💗", 0),
                (TagType.Gallery,     "galeria",    "📌", 0),
                (TagType.Reservation, "rezerwacja", "📝", 0),
                (TagType.Exchange,    "wymiana",    "🔄", 0),
                (TagType.TrashBin,    "kosz",       "🗑️", 0),
            };

            var botUser = db.GetUserOrCreateAsync(1).GetAwaiter().GetResult();
            foreach (var tag in setup)
            {
                var thisTag = botUser.GameDeck.Tags.FirstOrDefault(x => x.Name.Equals(tag.name, System.StringComparison.CurrentCultureIgnoreCase));
                if (thisTag is null)
                {
                    thisTag = new Tag { Name = tag.name };
                    botUser.GameDeck.Tags.Add(thisTag);
                    db.SaveChanges();
                }

                _baseTags.Add(tag.type, new TagIcon(thisTag.Id, tag.name, tag.icon));
            }
        }

        public bool IsSimilar(string tag) => _baseTags.Any(x => x.Value.Name.Contains(tag, System.StringComparison.CurrentCultureIgnoreCase))
            || _baseTags.Any(x => tag.Contains(x.Value.Name, System.StringComparison.CurrentCultureIgnoreCase));

        public List<string> GetAllIcons(Card card) => _baseTags.Where(x => card.Tags.Any(t => t.Id == x.Value.Id)).Select(x => x.Value.Icon).ToList();
        public ulong GetTagId(string name) => _baseTags.Where(x => x.Value.Name.Equals(name, System.StringComparison.CurrentCultureIgnoreCase)).Select(x => x.Value.Id).FirstOrDefault();
        public bool HasTag(Card card, TagType type) => card.Tags.Any(x => x.Id == _baseTags[type].Id);
        public bool HasTag(Card card, TagIcon tag) => card.Tags.Any(x => x.Id == tag.Id);
        public string GetIcon(TagType type) => _baseTags[type].Icon;
        public ulong GetTagId(TagType type) => _baseTags[type].Id;
        public TagIcon GetTag(TagType type) => _baseTags[type];
    }
}