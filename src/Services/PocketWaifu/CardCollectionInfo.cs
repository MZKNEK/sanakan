#pragma warning disable 1591

using System.Collections.Generic;
using Sanakan.Database.Models;

namespace Sanakan.Services.PocketWaifu
{
    public class CardCollectionInfo
    {
        public long TotalCardsCount { get; set; }
        public long UltimateCardsCount { get; set; }
        public int UniqueCardsCount { get; set; }
        public int ScalpelCount { get; set; }
        public int ScissorsCount { get; set; }
        public int CameraCount { get; set; }
        public int RestartCount { get; set; }
        public int OverflowCount { get; set; }
        public int TotalKCount { get; set; }
        public int TotalActiveKCCount { get; set; }
        public double TotalNormalCardPower { get; set; }
        public double TotalUltimateCardPower { get; set; }
        public Dictionary<string, long> CardsByRarityAndQuality { get; set; }
        public List<Card> CardsOnExpeditions { get; set; }
        public Card MostPowerfulCard { get; set; }
        public Card CardWithMostRestarts { get; set; }
        public Card CardWithMostKC { get; set; }
        public Card CardWithMostActiveKC { get; set; }

        public CardCollectionInfo()
        {
            CardsByRarityAndQuality = new Dictionary<string, long>();
            CardsOnExpeditions = new List<Card>();
            MostPowerfulCard = null;
            CardWithMostRestarts = null;
            CardWithMostKC = null;
            CardWithMostActiveKC = null;
        }
    }
}

