#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Sanakan.Config;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services.PocketWaifu;
using Sanakan.Services.Time;

namespace Sanakan.Services.Session.Models
{
    public class ExchangeSession : Session
    {
        private enum ExchangeStatus
        {
            Add, AcceptP1, AcceptP2, End
        }

        public enum ExchangeOutcome
        {
            None, Done, Declined, Cancelled, Expired
        }

        private const string CancelledPrefix = "Wymiana anulowana: ";

        public IMessage Message { get; set; }
        public PlayerInfo P1 { get; set; }
        public PlayerInfo P2 { get; set; }

        private ExchangeStatus State;
        private ExchangeOutcome Outcome = ExchangeOutcome.None;
        private string OutcomeText;
        private ISystemTime _time;
        private TagHelper _tags;
        private IConfig _config;

        private readonly Emoji AcceptEmote = new Emoji("✅");
        private readonly Emote DeclineEmote = Emote.Parse("<:redcross:581152766655856660>");

        private readonly Emoji InEmote = new Emoji("📥");
        private readonly Emoji ErrEmote = new Emoji("❌");
        private readonly Emoji OutEmote = new Emoji("📤");

        private readonly Emoji OneEmote = new Emoji("\u0031\u20E3");
        private readonly Emoji TwoEmote = new Emoji("\u0032\u20E3");

        public IEmote[] StartReactions => new IEmote[] { OneEmote, TwoEmote };

        public ExchangeSession(IUser owner, IUser exchanger, IConfig config, ISystemTime time, TagHelper tags) : base(owner)
        {
            State = ExchangeStatus.Add;
            Event = ExecuteOn.AllEvents;
            AddParticipant(exchanger);
            RunMode = RunMode.Sync;
            TimeoutMs = 120000;
            _config = config;
            _time = time;
            _tags = tags;

            Message = null;

            OnExecute = ExecuteAction;
            OnDispose = DisposeAction;
        }

        private async Task<bool> ExecuteAction(SessionContext context, Session session)
        {
            if (P1 == null || P2 == null || Message == null)
                return true;

            await HandleMessageAsync(context);
            return await HandleReactionAsync(context);
        }

        public Embed BuildEmbed()
        {
            var adding = State == ExchangeStatus.Add && Outcome == ExchangeOutcome.None;
            var description = $"{BuildProposition(P1, adding)}\n\n{BuildProposition(P2, adding)}";
            if (!string.IsNullOrEmpty(OutcomeText))
                description += $"\n\n{OutcomeText}";

            var embed = new EmbedBuilder
            {
                Title = Title(Outcome, State == ExchangeStatus.Add),
                Color = OutcomeColor(Outcome, State == ExchangeStatus.Add),
                Description = description.TrimToLength(),
            };

            if (Outcome != ExchangeOutcome.None)
                return embed.Build();

            if (adding)
            {
                embed.AddField("Jak to działa", "Na tym kanale: `dodaj WID [WID…]`, `usuń WID`, `oznacz tag`\n"
                    + $"{OneEmote} {P1.User.Mention} kończy dodawanie\n{TwoEmote} {P2.User.Mention} kończy dodawanie");
            }
            else
            {
                var turn = State == ExchangeStatus.AcceptP1 ? P1 : P2;
                embed.AddField("Teraz kolej na", $"{turn.User.Mention}: kliknij {AcceptEmote}, żeby zaakceptować, albo {DeclineEmote}, żeby odrzucić.");
            }

            return embed.WithFooter("Wymiana wygasa po 2 minutach bez aktywności").Build();
        }

        public static string Title(ExchangeOutcome outcome, bool adding) => outcome switch
        {
            ExchangeOutcome.Done => "✅ Wymiana zakończona",
            ExchangeOutcome.Declined => "❌ Wymiana odrzucona",
            ExchangeOutcome.Cancelled => "⚠️ Wymiana anulowana",
            ExchangeOutcome.Expired => "⌛ Wymiana wygasła",
            _ => adding ? "🔄 Wymiana" : "🔄 Wymiana: potwierdzenie",
        };

        public static Color OutcomeColor(ExchangeOutcome outcome, bool adding) => outcome switch
        {
            ExchangeOutcome.Done => new Color(59, 165, 93),
            ExchangeOutcome.Cancelled => new Color(229, 72, 77),
            ExchangeOutcome.Declined or ExchangeOutcome.Expired => new Color(128, 132, 142),
            _ => adding ? new Color(232, 197, 71) : new Color(91, 141, 239),
        };

        public static string CancelReason(string error)
        {
            var reason = error.StartsWith(CancelledPrefix, StringComparison.Ordinal) ? error.Substring(CancelledPrefix.Length) : error;
            return reason.Length > 0 ? char.ToUpperInvariant(reason[0]) + reason.Substring(1) : reason;
        }

        private async Task HandleMessageAsync(SessionContext context)
        {
            if (context.ReactionAdded != null || context.ReactionRemoved != null)
                return;

            if (State != ExchangeStatus.Add)
                return;

            if (context.Message.Id == Message.Id)
                return;

            if (context.Message.Channel.Id != Message.Channel.Id)
                return;

            var cmd = context.Message?.Content;
            if (cmd == null) return;

            var splitedCmd = cmd.Replace("\n", " ").Split(" ");
            if (splitedCmd.Length < 2) return;

            var cmdType = splitedCmd[0]?.ToLower();
            if (cmdType == null) return;

            PlayerInfo thisPlayer = null;
            PlayerInfo targetPlayer = null;
            if (context.User.Id == P1.User.Id)
            {
                thisPlayer = P1;
                targetPlayer = P2;
            }
            if (context.User.Id == P2.User.Id)
            {
                thisPlayer = P2;
                targetPlayer = P1;
            }
            if (thisPlayer == null) return;

            if (cmdType.Equals("usuń") || cmdType.Equals("usun"))
            {
                var WIDStr = splitedCmd?[1];
                if (string.IsNullOrEmpty(WIDStr))
                {
                    await context.Message.AddReactionAsync(ErrEmote);
                    return;
                }

                if (ulong.TryParse(WIDStr, out var WID))
                {
                    await HandleDeleteAsync(thisPlayer, WID, context.Message);
                }
                RestartTimer();
            }
            else if (cmdType.Equals("dodaj"))
            {
                var ids = new List<ulong>();
                foreach (var WIDStr in splitedCmd)
                    if (ulong.TryParse(WIDStr, out var WID))
                        ids.Add(WID);

                if (ids.Count > 0)
                {
                    await HandleAddAsync(thisPlayer, ids, context.Message, targetPlayer);
                }
                else await context.Message.AddReactionAsync(ErrEmote);
                RestartTimer();
            }
            else if (cmdType.Equals("tag") || cmdType.Equals("oznacz"))
            {
                var tag = splitedCmd?[1];
                if (string.IsNullOrEmpty(tag) || tag.Contains(" "))
                {
                    await context.Message.AddReactionAsync(ErrEmote);
                    return;
                }

                thisPlayer.Tag = tag;
                await context.Message.AddReactionAsync(AcceptEmote);
                RestartTimer();
            }
        }

        private async Task HandleAddAsync(PlayerInfo player, List<ulong> wid, IUserMessage message, PlayerInfo target)
        {
            bool error = false;
            bool added = false;

            foreach (var id in wid)
            {
                var card = player.Dbuser.GetCard(id);
                if (card is null)
                {
                    error = true;
                    continue;
                }

                _ = card.RecoverFatigue(_time);
                if (card.IsDisallowedToExchange())
                {
                    error = true;
                    continue;
                }

                if (card.Dere == Database.Models.Dere.Yami && target.Dbuser.GameDeck.IsGood())
                {
                    error = true;
                    continue;
                }

                if (card.Dere == Database.Models.Dere.Raito && target.Dbuser.GameDeck.IsEvil())
                {
                    error = true;
                    continue;
                }

                if (player.Cards.Any(x => x.Id == card.Id))
                    continue;

                player.Cards.Add(card);
                added = true;
            }

            player.Accepted = false;

            if (added) await message.AddReactionAsync(InEmote);
            if (error) await message.AddReactionAsync(ErrEmote);

            if (await Message.Channel.GetMessageAsync(Message.Id) is IUserMessage msg)
            {
                await msg.ModifyAsync(x => x.Embed = BuildEmbed());
            }
        }

        private async Task HandleDeleteAsync(PlayerInfo player, ulong wid, IUserMessage message)
        {
            var card = player.Cards.FirstOrDefault(x => x.Id == wid);
            if (card is null)
            {
                await message.AddReactionAsync(ErrEmote);
                return;
            }

            if (!player.Cards.Any(x => x.Id == card.Id))
                return;

            player.Accepted = false;
            player.Cards.Remove(card);

            await message.AddReactionAsync(OutEmote);

            if (await Message.Channel.GetMessageAsync(Message.Id) is IUserMessage msg)
            {
                await msg.ModifyAsync(x => x.Embed = BuildEmbed());
            }
        }

        public static string BuildProposition(PlayerInfo player, bool showFinished)
        {
            var header = $"{(showFinished && player.Accepted ? "✅ " : "")}{player.User.Mention} oferuje:";

            if (player.Cards.Count == 0)
                return $"{header}\n*nic*";

            if (player.Cards.Count > 12)
                return $"{header}\n**{Polish.Count(player.Cards.Count, "kartę", "karty", "kart")}**";

            return $"{header}\n{string.Join("\n", player.Cards.Select(x => x.GetString(false, false, true)))}";
        }

        private async Task<bool> HandleReactionAsync(SessionContext context)
        {
            bool end = false;
            if (context.Message.Id != Message.Id)
                return false;

            if (await Message.Channel.GetMessageAsync(Message.Id) is IUserMessage msg)
            {
                var reaction = context.ReactionAdded ?? context.ReactionRemoved;
                if (reaction == null || msg == null) return false;

                switch (State)
                {
                    case ExchangeStatus.AcceptP1:
                        end = await HandleUserReactionInAccept(reaction, P1, msg);
                        break;

                    case ExchangeStatus.AcceptP2:
                        end = await HandleUserReactionInAccept(reaction, P2, msg);
                        break;

                    case ExchangeStatus.Add:
                        await HandleReactionInAdd(reaction, msg);
                        break;

                    default:
                    case ExchangeStatus.End:
                        break;
                }
            }

            return end;
        }

        private async Task HandleReactionInAdd(SocketReaction reaction, IUserMessage msg)
        {
            var finished = false;
            if (reaction.Emote.Equals(OneEmote) && reaction.UserId == P1.User.Id)
            {
                finished = !P1.Accepted;
                P1.Accepted = true;
                RestartTimer();
            }
            else if (reaction.Emote.Equals(TwoEmote) && reaction.UserId == P2.User.Id)
            {
                finished = !P2.Accepted;
                P2.Accepted = true;
                RestartTimer();
            }

            if (P1.Accepted && P2.Accepted)
            {
                State = ExchangeStatus.AcceptP1;

                await msg.RemoveAllReactionsAsync();
                await msg.ModifyAsync(x => x.Embed = BuildEmbed());
                await msg.AddReactionsAsync(new IEmote[] { AcceptEmote, DeclineEmote });
            }
            else if (finished)
            {
                await msg.ModifyAsync(x => x.Embed = BuildEmbed());
            }
        }

        private async Task<bool> HandleUserReactionInAccept(SocketReaction reaction, PlayerInfo player, IUserMessage msg)
        {
            bool end = false;
            bool msgCh = false;

            if (reaction.UserId == player.User.Id)
            {
                if (reaction.Emote.Equals(AcceptEmote))
                {
                    if (State == ExchangeStatus.AcceptP1)
                    {
                        msgCh = true;
                        RestartTimer();
                        State = ExchangeStatus.AcceptP2;
                    }
                    else if (State == ExchangeStatus.AcceptP2)
                    {
                        string error = null;
                        if (P1.Cards.Count > 0 || P2.Cards.Count > 0)
                        {
                            using (var db = new Database.DatabaseContext(_config))
                            {
                                error = await FinalizeExchangeAsync(db);
                            }
                        }

                        Outcome = error == null ? ExchangeOutcome.Done : ExchangeOutcome.Cancelled;
                        OutcomeText = error == null ? null : CancelReason(error);
                        State = ExchangeStatus.End;
                        msgCh = true;
                        end = true;
                    }
                }
                else if (reaction.Emote.Equals(DeclineEmote) && State != ExchangeStatus.End)
                {
                    RestartTimer();
                    Outcome = ExchangeOutcome.Declined;
                    OutcomeText = $"{player.User.Mention} odrzucił propozycję.";
                    msgCh = true;
                    end = true;
                }

                if (msg != null && msgCh) await msg.ModifyAsync(x => x.Embed = BuildEmbed());
            }
            return end;
        }

        private bool CanTransfer(Card card, User target)
        {
            if (card == null)
                return false;

            _ = card.RecoverFatigue(_time);
            return !card.IsDisallowedToExchange()
                && !(card.Dere == Dere.Yami && target.GameDeck.IsGood())
                && !(card.Dere == Dere.Raito && target.GameDeck.IsEvil());
        }

        private async Task<string> FinalizeExchangeAsync(Database.DatabaseContext db)
        {
            var user1 = await db.GetUserOrCreateAsync(P1.User.Id);
            var user2 = await db.GetUserOrCreateAsync(P2.User.Id);
            var cards1 = P1.Cards.Select(c => user1.GetCard(c.Id)).ToList();
            var cards2 = P2.Cards.Select(c => user2.GetCard(c.Id)).ToList();

            if (user1.IsBlacklisted || user2.IsBlacklisted)
                return "Wymiana anulowana: jeden z graczy znajduje się na czarnej liście.";

            if (cards1.Any(c => !CanTransfer(c, user2)) || cards2.Any(c => !CanTransfer(c, user1)))
                return "Wymiana anulowana: co najmniej jedna z oferowanych kart nie może już zostać wymieniona.";

            if ((long)user1.GameDeck.Cards.Count - cards1.Count + cards2.Count > user1.GameDeck.MaxNumberOfCards
                || (long)user2.GameDeck.Cards.Count - cards2.Count + cards1.Count > user2.GameDeck.MaxNumberOfCards)
                return "Wymiana anulowana: jeden z graczy nie ma wystarczająco miejsca na otrzymywane karty.";

            var u1Data = (user1, Math.Max(cards1.Count, 1), await db.GetTagAsync(_tags, P1.Tag, P1.User.Id), P1.User.GetUserNickInGuild());
            var u2Data = (user2, Math.Max(cards2.Count, 1), await db.GetTagAsync(_tags, P2.Tag, P2.User.Id), P2.User.GetUserNickInGuild());

            foreach (var card in cards1)
                await card.ExchangeWithAsync(u1Data, u2Data, db, _time);

            foreach (var card in cards2)
                await card.ExchangeWithAsync(u2Data, u1Data, db, _time);

            await db.SaveChangesAsync();
            QueryCache.ExpireTag(new string[] { CacheTags.User(P1.User.Id), CacheTags.User(P2.User.Id) });
            return null;
        }

        private async Task DisposeAction()
        {
            if (Message != null)
            {
                if (await Message.Channel.GetMessageAsync(Message.Id) is IUserMessage msg)
                {
                    try
                    {
                        if (Outcome == ExchangeOutcome.None && P1 != null && P2 != null)
                        {
                            Outcome = ExchangeOutcome.Expired;
                            OutcomeText = "Przez 2 minuty nikt nic nie zrobił. Karty zostały u właścicieli.";
                            await msg.ModifyAsync(x => x.Embed = BuildEmbed());
                        }

                        await msg.RemoveAllReactionsAsync();
                    }
                    catch (Exception) { }
                }

                Message = null;
                P1 = null;
                P2 = null;
            }
        }
    }
}
