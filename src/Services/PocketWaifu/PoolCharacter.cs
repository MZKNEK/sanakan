#pragma warning disable 1591

using Shinden.Models;

namespace Sanakan.Services.PocketWaifu
{
    // Serializable copy of a pool entry, the library type cannot be rebuilt from its public data.
    public class PoolCharacter : ICharacterInfoTitle
    {
        public PoolCharacter() { }

        public PoolCharacter(ICharacterInfoTitle character)
        {
            Id = character.Id;
            Name = character.ToString();
            FirstName = character.FirstName;
            LastName = character.LastName;
            TitleName = character.TitleName;
            HasImage = character.HasImage;
            PictureUrl = character.PictureUrl;
            CharacterUrl = character.CharacterUrl;
        }

        public ulong Id { get; set; }
        public string Name { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string TitleName { get; set; }
        public bool HasImage { get; set; }
        public string PictureUrl { get; set; }
        public string CharacterUrl { get; set; }

        public override string ToString() => Name;
    }
}
