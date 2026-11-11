using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services.PocketWaifu;
using Xunit;

namespace Artifacts
{
    // the safari winner needs a profile off the blacklist with room for the card, checked without loading the collection
    public class SafariWinnerTests : IDisposable
    {
        private readonly WaifuCommandHarness _h = new WaifuCommandHarness();

        public void Dispose() => _h.Dispose();

        private User NewUser(ulong id, int cards, long max = 1000, bool blacklisted = false)
        {
            var user = new User().Default(id, _h.Time.Now());
            user.IsBlacklisted = blacklisted;
            user.GameDeck.MaxNumberOfCards = max;
            for (var i = 0; i < cards; i++)
                user.GameDeck.Cards.Add(new Card
                {
                    Id = id * 100 + (ulong)i, Character = 1, Name = "x", Rarity = Rarity.E, Health = 1, MarketValue = 1, Tags = new List<Tag>(),
                    CreationDate = _h.Time.Now()
                });
            return user;
        }

        [Fact]
        public async Task OnlyAPlayerWithRoomOffTheBlacklist_CanWin()
        {
            using (var db = await _h.CreateAsync())
            {
                db.Users.AddRange(NewUser(11, 2), NewUser(12, 3, max: 3), NewUser(13, 2, max: 3), NewUser(14, 0, blacklisted: true));
                await db.SaveChangesAsync();
            }

            using var read = _h.Open();
            Assert.True(await Spawn.CanWinSafariAsync(read, 11));
            Assert.False(await Spawn.CanWinSafariAsync(read, 12));
            Assert.True(await Spawn.CanWinSafariAsync(read, 13));
            Assert.False(await Spawn.CanWinSafariAsync(read, 14));
            Assert.False(await Spawn.CanWinSafariAsync(read, 15));
        }
    }
}
