using System.Collections.Generic;
using System.Linq;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Xunit;

namespace Artifacts
{
    // the cards of the profile's gallery, shared by the profile and the API of Profilator
    public class ProfileGalleryTests
    {
        private const ulong GalleryTag = 7;

        private static Card NewCard(ulong id, Rarity rarity, bool inGallery) => new Card
        {
            Id = id,
            Rarity = rarity,
            Tags = inGallery ? new List<Tag> { new Tag { Id = GalleryTag } } : new List<Tag>(),
        };

        private static User NewUser(ulong id, string order = null) => new User
        {
            Id = id,
            GameDeck = new GameDeck
            {
                CardsInGallery = 10,
                GalleryOrderedIds = order,
                Cards = new List<Card>
                {
                    NewCard(1, Rarity.C, true),
                    NewCard(2, Rarity.SSS, true),
                    NewCard(3, Rarity.A, false),
                    NewCard(4, Rarity.S, true),
                },
            },
        };

        [Fact]
        public void WithoutOrder_TaggedCardsByRarity()
        {
            var ids = NewUser(5).GetProfileGalleryCards(GalleryTag, 12).Select(x => x.Id);

            Assert.Equal(new ulong[] { 2, 4, 1 }, ids);
        }

        [Fact]
        public void WithOrder_PlayersOrderFirst_RestAfter()
        {
            var ids = NewUser(5, "1 3 99").GetProfileGalleryCards(GalleryTag, 12).Select(x => x.Id);

            Assert.Equal(new ulong[] { 1, 2, 4 }, ids);
        }

        [Fact]
        public void Count_LimitsTheCards()
        {
            Assert.Equal(2, NewUser(5).GetProfileGalleryCards(GalleryTag, 2).Count());
        }

        [Fact]
        public void TheBot_AllItsCardsByRarity()
        {
            var ids = NewUser(1).GetProfileGalleryCards(GalleryTag, 12).Select(x => x.Id);

            Assert.Equal(new ulong[] { 2, 4, 3, 1 }, ids);
        }
    }
}
