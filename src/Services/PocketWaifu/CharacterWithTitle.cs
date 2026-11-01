#pragma warning disable 1591

using Sanakan.ShindenCompat.Models;

namespace Sanakan.Services.PocketWaifu
{
    public class CharacterWithTitle
    {
        public string Title { get; set; }
        public IPersonSearch Character { get; set; }

        public override string ToString()
        {
            if (string.IsNullOrEmpty(Title))
                return Character.ToString();

            return $"{Character} ({Title})";
        }
    }
}