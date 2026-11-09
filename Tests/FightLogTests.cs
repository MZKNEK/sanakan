using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using Sanakan.Database.Models;
using Sanakan.Services.PocketWaifu;
using Sanakan.Services.PocketWaifu.Fight;
using Xunit;

namespace Artifacts
{
    public class FightLogTests
    {
        private static ulong _nextId = 1000;

        private static Card NewCard(string name, Dere dere, ulong deck, int health = 1000, int attack = 100, int defence = 50, string image = "https://example.com/a.png") => new Card
        {
            Id = _nextId++,
            Name = name,
            Dere = dere,
            Rarity = Rarity.SSS,
            Health = health,
            Attack = attack,
            Defence = defence,
            GameDeckId = deck,
            Image = image,
            Tags = new List<Tag>(),
        };

        private static CardWithHealth Fighter(Card card, double health) => new CardWithHealth { Card = card, Health = health, MaxHealth = card.Health };

        [Fact]
        public void FullLog_ShowsStartingCardsEveryAttackAndTheResult()
        {
            var rem = NewCard("Rem", Dere.Yandere, 1, image: null);
            var emilia = NewCard("Emilia", Dere.Tsundere, 2);
            var players = new List<PlayerInfo>
            {
                new PlayerInfo { Cards = new List<Card> { rem } },
                new PlayerInfo { Cards = new List<Card> { emilia } },
            };
            var round = new RoundInfo();
            var remFighter = Fighter(rem, 200);
            Waifu.Strike(round, remFighter, Fighter(emilia, 400), false);
            Waifu.Strike(round, remFighter, Fighter(emilia, 10), true);
            round.Fights.Add(new AttackInfo { AtkCardId = emilia.Id, DefCardId = rem.Id, Dodged = true });
            var fight = new FightHistory(players[0]);
            fight.Rounds.Add(round);

            var log = Waifu.GetFullFightLog(fight, players);

            Assert.StartsWith("Pojedynek: ???? vs ????", log);
            Assert.Contains("Rem (WID", log);
            Assert.Contains("zdolność: poniżej 25% życia", log);
            Assert.Contains("bez obrazka: atak i obrona słabsze o 20% w walce", log);
            Assert.Contains("Runda 1", log);
            Assert.Contains("Rem [????] → Emilia [????]: ", log);
            Assert.Contains("(szał Yandere, osłona Tsundere)", log);
            Assert.Contains("↩ kontra: Rem", log);
            Assert.Contains("✖ Emilia [????] pada", log);
            Assert.Contains("Emilia [????] → Rem [????]: unik", log);
            Assert.EndsWith("Wynik: wygrywa ????" + System.Environment.NewLine, log);
        }

        [Fact]
        public void FullLog_OfTheLongestRealisticFight_FitsInADiscordUpload()
        {
            var waifu = (Waifu)FormatterServices.GetUninitializedObject(typeof(Waifu));
            var players = new List<PlayerInfo>
            {
                new PlayerInfo { Cards = Enumerable.Range(0, 8).Select(_ => NewCard("Bardzo Długie Imię Postaci", Dere.Bodere, 1, 999, 10, 99)).ToList() },
                new PlayerInfo { Cards = Enumerable.Range(0, 8).Select(_ => NewCard("Bardzo Długie Imię Postaci", Dere.Dandere, 2, 999, 10, 99)).ToList() },
            };

            var fight = waifu.MakeFightAsync(players);
            var bytes = Encoding.UTF8.GetByteCount(Waifu.GetFullFightLog(fight, players));

            Assert.True(fight.Rounds.Count > 500);
            Assert.True(bytes < 8 * 1024 * 1024, $"{bytes} bytes");
        }
    }
}
