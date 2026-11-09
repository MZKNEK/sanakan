#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using static System.FormattableString;

namespace Sanakan.Services.PocketWaifu
{
    public record PowerRecalculationResult(int Cards, int ChangedCards, int Decks, int ChangedDecks);

    public record PowerRecalculationProgress(int CardsDone, int Cards, int ChangedCards, int DecksDone, int DecksToWrite);

    // card and deck power are stored for the site and the duel pool, so a formula change needs them recalculated;
    // only the columns the formula needs are read, only changed values are written, and batches are spaced out
    public static class PowerRecalculation
    {
        public static async Task<PowerRecalculationResult> RecalculateAllAsync(Func<Database.DatabaseContext> createContext,
            int batchSize = 1000, TimeSpan? pause = null, Action<PowerRecalculationProgress> progress = null)
        {
            var delay = pause ?? TimeSpan.FromMilliseconds(50);

            List<ulong> cardIds;
            Dictionary<ulong, double> storedDeckPower;
            using (var db = createContext())
            {
                cardIds = await db.Cards.AsQueryable().AsNoTracking().Select(x => x.Id).ToListAsync();
                storedDeckPower = await db.GameDecks.AsQueryable().AsNoTracking().Select(x => new { x.Id, x.DeckPower })
                    .ToDictionaryAsync(x => x.Id, x => x.DeckPower);
            }

            var deckPower = storedDeckPower.Keys.ToDictionary(x => x, _ => new List<double>());
            var changedCards = 0;
            var cardsDone = 0;

            foreach (var chunk in cardIds.Chunk(batchSize))
            {
                using var db = createContext();
                var cards = await db.Cards.AsQueryable().AsNoTracking().Where(x => chunk.Contains(x.Id)).Select(x => new Card
                {
                    Id = x.Id, GameDeckId = x.GameDeckId, Active = x.Active, FromFigure = x.FromFigure, Dere = x.Dere, Curse = x.Curse,
                    Health = x.Health, HealthBonus = x.HealthBonus, Attack = x.Attack, AttackBonus = x.AttackBonus,
                    Defence = x.Defence, DefenceBonus = x.DefenceBonus, Affection = x.Affection, RestartCnt = x.RestartCnt,
                    CardPower = x.CardPower, CritChance = x.CritChance,
                    // only whether there is a picture matters for the power, the address itself is not needed
                    Image = x.Image != null || x.CustomImage != null ? "set" : null,
                }).ToListAsync();

                var changed = new List<(ulong Id, double Power)>();
                foreach (var card in cards)
                {
                    var stored = card.CardPower;
                    var power = card.CalculateCardPower();
                    if (Math.Abs(stored - power) > 1e-6)
                        changed.Add((card.Id, power));

                    if (card.Active && deckPower.ContainsKey(card.GameDeckId))
                        deckPower[card.GameDeckId].Add(power);
                }

                if (changed.Count > 0)
                    await UpdateAsync(db, "Cards", "CardPower", changed);

                changedCards += changed.Count;
                cardsDone += chunk.Length;
                progress?.Invoke(new PowerRecalculationProgress(cardsDone, cardIds.Count, changedCards, 0, -1));
                await Task.Delay(delay);
            }

            // -1 marks a deck its owner has to set again, it stays until they do
            var changedDecks = deckPower.Select(x => (x.Key, Value: UserExtension.CombineCardPowers(x.Value)))
                .Where(x => storedDeckPower[x.Key] != -1 && Math.Abs(storedDeckPower[x.Key] - x.Value) > 1e-6).ToList();
            var decksDone = 0;
            progress?.Invoke(new PowerRecalculationProgress(cardsDone, cardIds.Count, changedCards, 0, changedDecks.Count));
            foreach (var chunk in changedDecks.Chunk(batchSize))
            {
                using var db = createContext();
                await UpdateAsync(db, "GameDecks", "DeckPower", chunk);
                decksDone += chunk.Length;
                progress?.Invoke(new PowerRecalculationProgress(cardsDone, cardIds.Count, changedCards, decksDone, changedDecks.Count));
                await Task.Delay(delay);
            }

            QueryCache.ExpireTag(ContextExtension.PvpPlayersCacheTag);
            return new PowerRecalculationResult(cardIds.Count, changedCards, storedDeckPower.Count, changedDecks.Count);
        }

        public static string ProgressText(PowerRecalculationProgress p, TimeSpan elapsed)
        {
            static string Duration(TimeSpan t) => t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes} min {t.Seconds} s" : $"{t.Seconds} s";

            if (p.DecksToWrite < 0)
            {
                var percent = p.Cards == 0 ? 100 : p.CardsDone * 100 / p.Cards;
                var left = p.CardsDone == 0 ? "" : $", zostało ok. {Duration(elapsed / p.CardsDone * (p.Cards - p.CardsDone))}";
                return $"Przeliczanie mocy kart: {p.CardsDone} / {p.Cards} ({percent}%), zmieniona moc: {p.ChangedCards}.\nTrwa {Duration(elapsed)}{left}.";
            }

            return $"Karty przeliczone ({p.Cards}, zmieniona moc: {p.ChangedCards}).\nZapisywanie mocy talii: {p.DecksDone} / {p.DecksToWrite}. Trwa {Duration(elapsed)}.";
        }

        // one statement per batch instead of one per row; the values are numbers formatted by the bot, not user input
        private static Task UpdateAsync(Database.DatabaseContext db, string table, string column, IReadOnlyCollection<(ulong Id, double Value)> rows)
        {
            var sql = new StringBuilder($"UPDATE {table} SET {column} = CASE Id");
            foreach (var (id, value) in rows)
                sql.Append(Invariant($" WHEN {id} THEN {value:R}"));

            sql.Append(Invariant($" END WHERE Id IN ({string.Join(",", rows.Select(x => x.Id))})"));
            return db.Database.ExecuteSqlRawAsync(sql.ToString());
        }
    }
}
