#pragma warning disable 1591

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using static System.FormattableString;

namespace Sanakan.Database
{
    // card and deck power are stored for the duel pool and the site, so they are refreshed on every save
    // instead of in each command that changes a card
    public static class PowerActivity
    {
        public static readonly SaveChangesInterceptor Interceptor = new PowerInterceptor();

        private static readonly string[] _powerProperties =
        {
            nameof(Card.Health), nameof(Card.HealthBonus), nameof(Card.Attack), nameof(Card.AttackBonus), nameof(Card.Defence),
            nameof(Card.DefenceBonus), nameof(Card.Affection), nameof(Card.RestartCnt), nameof(Card.Curse), nameof(Card.Dere),
            nameof(Card.FromFigure), nameof(Card.CritChance), nameof(Card.Image), nameof(Card.CustomImage),
        };

        // before the save: recalculates changed cards and remembers which decks need their power refreshed
        public static HashSet<ulong> Capture(DbContext context)
        {
            var decks = new HashSet<ulong>();
            foreach (var entry in context.ChangeTracker.Entries<Card>())
            {
                var card = entry.Entity;
                switch (entry.State)
                {
                    case EntityState.Added:
                        card.CalculateCardPower();
                        if (card.Active) decks.Add(card.GameDeckId);
                        break;

                    case EntityState.Deleted:
                        if (card.Active) decks.Add(card.GameDeckId);
                        break;

                    case EntityState.Modified:
                        if (_powerProperties.Any(p => entry.Property(p).IsModified))
                        {
                            card.CalculateCardPower();
                            var power = entry.Property(nameof(Card.CardPower));
                            if (!Equals(power.OriginalValue, card.CardPower))
                                power.IsModified = true;
                        }

                        var owner = entry.Property(nameof(Card.GameDeckId));
                        if (owner.IsModified || entry.Property(nameof(Card.Active)).IsModified || (card.Active && entry.Property(nameof(Card.CardPower)).IsModified))
                        {
                            decks.Add(card.GameDeckId);
                            decks.Add((ulong)owner.OriginalValue);
                        }
                        break;
                }
            }
            return decks;
        }

        // the -1 marker means the owner has to set the deck again, it stays until they do
        public static string RefreshDecksSql(IEnumerable<ulong> decks) => Invariant(
            $"UPDATE GameDecks SET DeckPower = COALESCE((SELECT SUM(c.CardPower) FROM Cards c WHERE c.GameDeckId = GameDecks.Id AND c.Active = 1), 0), ")
            + "CardsInDeck = (SELECT COUNT(*) FROM Cards c WHERE c.GameDeckId = GameDecks.Id AND c.Active = 1) "
            + Invariant($"WHERE Id IN ({string.Join(",", decks)}) AND DeckPower <> -1");

        private class PowerInterceptor : SaveChangesInterceptor
        {
            public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
            {
                Remember(eventData.Context);
                return base.SavingChanges(eventData, result);
            }

            public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
                InterceptionResult<int> result, CancellationToken cancellationToken = default)
            {
                Remember(eventData.Context);
                return base.SavingChangesAsync(eventData, result, cancellationToken);
            }

            public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
            {
                var decks = Take(eventData.Context);
                if (decks != null)
                    eventData.Context.Database.ExecuteSqlRaw(RefreshDecksSql(decks));

                return base.SavedChanges(eventData, result);
            }

            public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
                CancellationToken cancellationToken = default)
            {
                var decks = Take(eventData.Context);
                if (decks != null)
                    await eventData.Context.Database.ExecuteSqlRawAsync(RefreshDecksSql(decks), cancellationToken);

                return await base.SavedChangesAsync(eventData, result, cancellationToken);
            }

            private static void Remember(DbContext context)
            {
                if (context is DatabaseContext db)
                    db.PendingPowerDecks = Capture(context);
            }

            private static HashSet<ulong> Take(DbContext context)
            {
                if (context is not DatabaseContext db || db.PendingPowerDecks == null || db.PendingPowerDecks.Count == 0)
                    return null;

                var decks = db.PendingPowerDecks;
                db.PendingPowerDecks = null;
                return decks;
            }
        }
    }
}
