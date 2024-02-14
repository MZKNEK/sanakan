#pragma warning disable 1591

namespace Sanakan.Services.PocketWaifu
{
    public enum CurrencyType
    {
        TC, CT, PC, AC, SC
    }

    public class CurrencyCost
    {
        public CurrencyCost(int cost, CurrencyType type)
        {
            Cost = cost;
            Type = type;
        }

        public CurrencyType Type { get; }
        public int Cost { get; }

        public override string ToString()
        {
            return $"{Cost} {Type}";
        }
    }
}