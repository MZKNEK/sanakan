#pragma warning disable 1591

namespace Sanakan.Services.PocketWaifu
{
    public class ItemCountPair
    {
        public uint Item;
        public long Count;

        public override string ToString()
        {
            return $"{Item}:{Count}";
        }
    }
}