#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Discord;
using Microsoft.EntityFrameworkCore;
using Sanakan.Database;
using Sanakan.Database.Models.Analytics;

namespace Sanakan.Services
{
    // Rebuilds the history of exchanges from the bot's exchange messages. Two formats exist: since 2026-11-08 the title
    // says "✅ Wymiana zakończona"; before, the description ended with "Wymiana zakończona!". Card ids are written as
    // **[123]** (until 2023) or **[[123](link)]**; an offer of more than 12 cards shows only their number.
    // The exchange message comes first; the players' messages around it only fill in the card ids it lacks, from the
    // "dodaj"/"usuń" written during the exchange, check the players against the command that started it, and give the
    // players' names. An exchange that is not certain (cut message, cards that cannot be told) is left out.
    public static class ExchangeImport
    {
        public const string FinishedTitle = "✅ Wymiana zakończona";
        private const string OldFinished = "Wymiana zakończona!";
        private const int DescriptionLimit = 3000;
        private const int MaxNameLength = 100;
        // the command that starts an exchange is answered within seconds
        private static readonly TimeSpan StartCommandWindow = TimeSpan.FromMinutes(1);
        // players' messages are kept this long past the oldest pending exchange, an exchange lasts minutes
        private static readonly TimeSpan KeepCommands = TimeSpan.FromHours(6);

        private static readonly Regex Offer = new Regex(@"<@!?(\d+)> oferuje:", RegexOptions.Compiled);
        private static readonly Regex CardLine = new Regex(@"^\*\*\[\[?(\d+)[\]\(]", RegexOptions.Compiled);
        private static readonly Regex OldCount = new Regex(@"^\*\*\[(\d+)\]\*\* kart$", RegexOptions.Compiled);
        private static readonly Regex NewCount = new Regex(@"^\*\*(\d+) (kartę|karty|kart)\*\*$", RegexOptions.Compiled);
        private static readonly Regex StartCommand = new Regex(@"^\S*(wymiana|exchange)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public sealed class Side
        {
            public ulong UserId { get; set; }
            public int Gave { get; set; }
            // fewer than Gave when only the number was shown
            public List<ulong> Cards { get; set; }
        }

        public sealed class Parsed
        {
            public Side User1 { get; init; }
            public Side User2 { get; init; }
            // the description was cut at Discord's limit, the second offer can miss cards
            public bool Truncated { get; init; }
        }

        public sealed class Result
        {
            public int Messages, Imported, AlreadyKnown, CardsFromCommands, SkippedCut, SkippedMissingCards, SkippedOtherPartner;
            public ulong OldestMessageId;
        }

        private enum CommandKind { Add, Remove, Start }

        private sealed class Command
        {
            public ulong Id;
            public ulong AuthorId;
            public DateTimeOffset Time;
            public CommandKind Kind;
            public List<ulong> Ids;
            public bool Added, Failed, Removed;
        }

        private sealed class Found
        {
            public IMessage Message;
            public Parsed Exchange;
            public DateTimeOffset Start, End;
        }

        public static Parsed Parse(string title, string description)
        {
            description = (description ?? "").Replace("\r", "");
            var truncated = description.Length >= DescriptionLimit && description.EndsWith("...", StringComparison.Ordinal);
            var finished = title == FinishedTitle || description.TrimEnd().EndsWith(OldFinished, StringComparison.Ordinal);
            if (!finished)
                return null;

            var offers = Offer.Matches(description);
            if (offers.Count == 1 && truncated)
                return new Parsed { Truncated = true };
            if (offers.Count != 2)
                return null;

            var user1 = ParseSide(offers[0], description.Substring(offers[0].Index + offers[0].Length, offers[1].Index - offers[0].Index - offers[0].Length));
            var user2 = ParseSide(offers[1], description.Substring(offers[1].Index + offers[1].Length));
            if (user1 == null || user2 == null || user1.UserId == user2.UserId)
                return null;

            return new Parsed { User1 = user1, User2 = user2, Truncated = truncated };
        }

        private static Side ParseSide(Match offer, string text)
        {
            if (!ulong.TryParse(offer.Groups[1].Value, out var userId) || userId == 0)
                return null;

            var cards = new List<ulong>();
            foreach (var line in text.Split('\n').Select(x => x.Trim()))
            {
                var count = OldCount.Match(line);
                if (!count.Success) count = NewCount.Match(line);
                if (count.Success)
                    return new Side { UserId = userId, Gave = int.Parse(count.Groups[1].Value), Cards = new List<ulong>() };

                var card = CardLine.Match(line);
                if (card.Success && ulong.TryParse(card.Groups[1].Value, out var id) && id != 0 && !cards.Contains(id))
                    cards.Add(id);
            }
            return new Side { UserId = userId, Gave = cards.Count, Cards = cards };
        }

        private static Command ReadCommand(IMessage message)
        {
            var words = (message.Content ?? "").Replace("\n", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length < 2)
                return null;

            var first = words[0].ToLowerInvariant();
            var command = new Command { Id = message.Id, AuthorId = message.Author.Id, Time = message.Timestamp };
            if (first == "dodaj") command.Kind = CommandKind.Add;
            else if (first == "usuń" || first == "usun") command.Kind = CommandKind.Remove;
            else if (StartCommand.IsMatch(first)) command.Kind = CommandKind.Start;
            else return null;

            if (command.Kind == CommandKind.Start)
            {
                command.Ids = message.MentionedUserIds?.Where(x => x != command.AuthorId).Distinct().ToList() ?? new List<ulong>();
                return command.Ids.Count > 0 ? command : null;
            }

            command.Ids = words.Skip(1).Select(x => ulong.TryParse(x, out var id) ? id : 0).Where(x => x != 0).ToList();
            foreach (var reaction in message.Reactions ?? new Dictionary<IEmote, ReactionMetadata>())
            {
                if (!reaction.Value.IsMe) continue;
                if (reaction.Key.Name == "📥") command.Added = true;
                else if (reaction.Key.Name == "❌") command.Failed = true;
                else if (reaction.Key.Name == "📤") command.Removed = true;
            }
            return command;
        }

        // the cards a player offered, replayed from their commands; null when a command's outcome is unclear
        private static List<ulong> Replay(IEnumerable<Command> commands)
        {
            var cards = new List<ulong>();
            foreach (var command in commands.OrderBy(x => x.Time).ThenBy(x => x.Id))
            {
                if (command.Kind == CommandKind.Add)
                {
                    // both reactions: some ids were refused, which ones is not known
                    if (command.Added && command.Failed) return null;
                    if (command.Added) cards.AddRange(command.Ids.Where(x => !cards.Contains(x)));
                }
                else if (command.Kind == CommandKind.Remove && command.Removed && command.Ids.Count > 0)
                {
                    cards.Remove(command.Ids[0]);
                }
            }
            return cards;
        }

        // true when the exchange is certain: whole message, every card known, the start command (if found) names the same players
        private static bool Complete(Found found, List<Command> commands, Result result)
        {
            var exchange = found.Exchange;
            if (exchange.Truncated)
            {
                result.SkippedCut++;
                return false;
            }

            var user1 = exchange.User1.UserId;
            var start = commands.Where(x => x.Kind == CommandKind.Start && x.AuthorId == user1
                    && x.Time <= found.Start && x.Time >= found.Start - StartCommandWindow)
                .OrderByDescending(x => x.Time).FirstOrDefault();
            if (start != null && !start.Ids.Contains(exchange.User2.UserId))
            {
                result.SkippedOtherPartner++;
                return false;
            }

            var filled = 0;
            foreach (var side in new[] { exchange.User1, exchange.User2 })
            {
                if (side.Cards.Count == side.Gave)
                    continue;

                var replayed = Replay(commands.Where(x => x.AuthorId == side.UserId && x.Kind != CommandKind.Start
                    && x.Time >= found.Start && x.Time <= found.End));
                if (replayed == null || replayed.Count != side.Gave)
                {
                    result.SkippedMissingCards++;
                    return false;
                }

                side.Cards = replayed;
                filled++;
            }

            result.CardsFromCommands += filled;
            return true;
        }

        // fetchBefore(null) gives the newest page, then the page before the given message; an empty page ends the import
        public static async Task<Result> ImportAsync(Func<DatabaseContext> createDb, Func<ulong?, Task<IReadOnlyCollection<IMessage>>> fetchBefore,
            ulong botId, ulong? startBefore = null, Action<Result> progress = null)
        {
            var result = new Result();
            DateTime? liveSince;
            using (var db = createDb())
            {
                // exchanges recorded before the messages were stored with them; messages from then on are already counted
                liveSince = await db.CardExchanges.AsNoTracking().Where(x => !x.Imported && x.MessageId == null)
                    .Select(x => (DateTime?)x.Date).MinAsync();
            }

            var commands = new List<Command>();
            var names = new Dictionary<ulong, (string Name, DateTime Seen)>();
            var pending = new List<Found>();
            var before = startBefore;
            while (true)
            {
                var page = await fetchBefore(before);
                var last = page == null || page.Count == 0;
                if (!last)
                {
                    result.Messages += page.Count;
                    before = page.Min(x => x.Id);
                    result.OldestMessageId = before.Value;

                    foreach (var message in page.OrderByDescending(x => x.Id))
                    {
                        if (message.Author == null)
                            continue;

                        if (message.Author.Id != botId)
                        {
                            // going back in time, the first name seen is the newest
                            var name = message.Author.GlobalName ?? message.Author.Username;
                            if (!string.IsNullOrWhiteSpace(name) && !names.ContainsKey(message.Author.Id))
                                names[message.Author.Id] = (name.Length > MaxNameLength ? name[..MaxNameLength] : name, message.Timestamp.LocalDateTime);

                            var command = ReadCommand(message);
                            if (command != null) commands.Add(command);
                            continue;
                        }

                        var embed = message.Embeds?.FirstOrDefault();
                        var parsed = embed == null ? null : Parse(embed.Title, embed.Description);
                        if (parsed == null)
                            continue;

                        var end = message.EditedTimestamp ?? message.Timestamp;
                        if (liveSince != null && end.LocalDateTime >= liveSince.Value)
                            continue;

                        pending.Add(new Found { Message = message, Exchange = parsed, Start = message.Timestamp, End = end });
                    }
                }

                // an exchange is complete once the messages before its start, where its command is, were read
                var oldest = last ? DateTimeOffset.MinValue : page.Min(x => x.Timestamp);
                var ready = pending.Where(x => x.Start - StartCommandWindow > oldest).ToList();
                if (ready.Count > 0)
                {
                    var certain = ready.Where(x => Complete(x, commands, result)).ToList();
                    if (certain.Count > 0)
                        await SaveAsync(createDb, certain, names, result);
                    pending.RemoveAll(ready.Contains);
                }

                var keepAfter = (pending.Count > 0 ? pending.Min(x => x.Start) : oldest) + KeepCommands;
                commands.RemoveAll(x => x.Time > keepAfter);

                progress?.Invoke(result);
                if (last)
                    break;
            }
            return result;
        }

        private static async Task SaveAsync(Func<DatabaseContext> createDb, List<Found> found, Dictionary<ulong, (string Name, DateTime Seen)> names, Result result)
        {
            using var db = createDb();
            var ids = found.Select(x => (ulong?)x.Message.Id).ToList();
            var known = (await db.CardExchanges.AsNoTracking().Where(x => ids.Contains(x.MessageId)).Select(x => x.MessageId).ToListAsync()).ToHashSet();

            var players = new HashSet<ulong>();
            foreach (var x in found)
            {
                var (message, parsed) = (x.Message, x.Exchange);
                if (known.Contains(message.Id))
                {
                    result.AlreadyKnown++;
                    continue;
                }

                var record = new CardExchange
                {
                    Date = x.End.LocalDateTime,
                    MessageId = message.Id,
                    Imported = true,
                    User1Id = parsed.User1.UserId,
                    User2Id = parsed.User2.UserId,
                    User1Gave = parsed.User1.Gave,
                    User2Gave = parsed.User2.Gave,
                };
                foreach (var card in parsed.User1.Cards)
                    record.Cards.Add(new CardExchangeCard { CardId = card, FromUserId = parsed.User1.UserId, ToUserId = parsed.User2.UserId });
                foreach (var card in parsed.User2.Cards.Where(c => !parsed.User1.Cards.Contains(c)))
                    record.Cards.Add(new CardExchangeCard { CardId = card, FromUserId = parsed.User2.UserId, ToUserId = parsed.User1.UserId });
                db.CardExchanges.Add(record);
                players.Add(record.User1Id);
                players.Add(record.User2Id);

                result.Imported++;
            }

            await RememberNamesAsync(db, names.Where(x => players.Contains(x.Key)).Select(x => (x.Key, x.Value.Name, x.Value.Seen)));
            await db.SaveChangesAsync();
        }

        // a newer name is kept over an older one
        public static async Task RememberNamesAsync(DatabaseContext db, IEnumerable<(ulong UserId, string Name, DateTime Seen)> names)
        {
            var list = names.Where(x => !string.IsNullOrWhiteSpace(x.Name)).ToList();
            if (list.Count == 0)
                return;

            var ids = list.Select(x => x.UserId).ToList();
            var stored = await db.PlayerNames.Where(x => ids.Contains(x.UserId)).ToDictionaryAsync(x => x.UserId);
            foreach (var (userId, name, seen) in list)
            {
                var trimmed = name.Length > MaxNameLength ? name[..MaxNameLength] : name;
                if (!stored.TryGetValue(userId, out var row))
                    db.PlayerNames.Add(stored[userId] = new PlayerName { UserId = userId, Name = trimmed, SeenAt = seen });
                else if (row.SeenAt <= seen)
                {
                    row.Name = trimmed;
                    row.SeenAt = seen;
                }
            }
        }

        public static string Describe(Result result, bool finished) =>
            $"{(finished ? "Import wymian zakończony" : "Import wymian trwa")}: przejrzano {result.Messages} wiadomości, "
            + $"dodano {result.Imported} wymian, już znanych {result.AlreadyKnown}.\n"
            + $"Listy kart odtworzone z komend „dodaj”/„usuń”: {result.CardsFromCommands}.\n"
            + $"Pominięte jako niepewne: ucięta wiadomość {result.SkippedCut}, nie da się ustalić kart {result.SkippedMissingCards}, "
            + $"komenda startu z innym graczem {result.SkippedOtherPartner}.\n"
            + $"Najstarsza przejrzana wiadomość: {result.OldestMessageId}";
    }
}
