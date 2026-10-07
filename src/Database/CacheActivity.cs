#pragma warning disable 1591

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Sanakan.Database.Models;
using Sanakan.Database.Models.Configuration;
using Sanakan.Database.Models.Management;
using Z.EntityFramework.Plus;

namespace Sanakan.Database
{
    // Centralne unieważnianie cache na podstawie faktycznie zmienionych encji (ChangeTracker),
    // zamiast ręcznych ExpireTag w każdym miejscu. Ręczne wywołania mogą zostać jako wsparcie.
    // Tagi: user-{id}, character-{id}, config-{id} oraz mute/quiz.
    public static class CacheActivity
    {
        public static readonly SaveChangesInterceptor Interceptor = new CacheInterceptor();

        private class CacheInterceptor : SaveChangesInterceptor
        {
            public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
            {
                Expire(eventData.Context);
                return base.SavingChanges(eventData, result);
            }

            public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
                InterceptionResult<int> result, CancellationToken cancellationToken = default)
            {
                Expire(eventData.Context);
                return base.SavingChangesAsync(eventData, result, cancellationToken);
            }

            private static void Expire(DbContext context)
            {
                if (context == null) return;
                if (context is DatabaseContext db && db.SuppressCacheInvalidation) return;

                var users = new HashSet<ulong>();
                var characters = new HashSet<ulong>();
                var guilds = new HashSet<ulong>();
                var cardIds = new HashSet<ulong>();
                var tagIds = new HashSet<ulong>();
                var waifuIds = new HashSet<ulong>();
                var packIds = new HashSet<ulong>();
                var mute = false;
                var quiz = false;

                foreach (var entry in context.ChangeTracker.Entries())
                {
                    if (entry.State != EntityState.Added && entry.State != EntityState.Modified && entry.State != EntityState.Deleted)
                        continue;

                    switch (entry.Entity)
                    {
                        case Card card:
                            characters.Add(card.Character);
                            users.Add(card.GameDeckId);
                            if (entry.State == EntityState.Modified)
                                characters.Add(entry.OriginalValues.GetValue<ulong>(nameof(Card.Character)));
                            break;

                        case TagCardRelation relation:
                            cardIds.Add(relation.CardId);
                            break;

                        case BoosterPackCharacter character:
                            packIds.Add(character.BoosterPackId);
                            break;

                        case RarityExcluded excluded:
                            packIds.Add(excluded.BoosterPackId);
                            break;

                        case Tag tag:
                            users.Add(tag.GameDeckId);
                            tagIds.Add(tag.Id);
                            break;

                        case Item item: users.Add(item.GameDeckId); break;
                        case BoosterPack pack: users.Add(pack.GameDeckId); break;
                        case CardPvPStats pvp: users.Add(pvp.GameDeckId); break;
                        case WishlistObject wish: users.Add(wish.GameDeckId); break;
                        case Figure figure: users.Add(figure.GameDeckId); break;
                        case ExpContainer exp: users.Add(exp.GameDeckId); break;
                        case UserStats stats: users.Add(stats.UserId); break;
                        case TimeStatus time: users.Add(time.UserId); break;
                        case SlotMachineConfig sm: users.Add(sm.UserId); break;
                        case GameDeck deck: users.Add(deck.UserId); break;
                        case User user: users.Add(user.Id); break;
                        case PenaltyInfo _: mute = true; break;
                        case MuteModifier _: mute = true; break;
                        case OwnedRole _: mute = true; break;

                        case Question _: quiz = true; break;
                        case Answer _: quiz = true; break;

                        case GuildOptions guild: guilds.Add(guild.Id); break;
                        case Waifu waifu: guilds.Add(waifu.GuildOptionsId); break;
                        case CommandChannel cmd: guilds.Add(cmd.GuildOptionsId); break;
                        case LevelRole lvl: guilds.Add(lvl.GuildOptionsId); break;
                        case ModeratorRoles mod: guilds.Add(mod.GuildOptionsId); break;
                        case MyLand land: guilds.Add(land.GuildOptionsId); break;
                        case Raport raport: guilds.Add(raport.GuildOptionsId); break;
                        case SelfRole self: guilds.Add(self.GuildOptionsId); break;
                        case WithoutExpChannel we: guilds.Add(we.GuildOptionsId); break;
                        case WithoutMsgCntChannel wm: guilds.Add(wm.GuildOptionsId); break;
                        case WithoutSupervisionChannel ws: guilds.Add(ws.GuildOptionsId); break;
                        case WaifuCommandChannel wc: waifuIds.Add(wc.WaifuId); break;
                        case WaifuFightChannel wf: waifuIds.Add(wf.WaifuId); break;
                    }

                    // zmiana właściciela (np. wymiana/duel) - czyścimy też cache poprzedniego gracza
                    if (entry.State == EntityState.Modified
                        && entry.Metadata.FindProperty(nameof(Card.GameDeckId)) != null
                        && entry.OriginalValues[nameof(Card.GameDeckId)] is ulong previousOwner)
                    {
                        users.Add(previousOwner);
                    }
                }

                if (cardIds.Count > 0)
                {
                    var ids = cardIds.ToList();
                    foreach (var card in context.Set<Card>().AsNoTracking()
                        .Where(x => ids.Contains(x.Id)).Select(x => new { x.Character, x.GameDeckId }).ToList())
                    {
                        characters.Add(card.Character);
                        users.Add(card.GameDeckId);
                    }
                }

                if (tagIds.Count > 0)
                {
                    var ids = tagIds.ToList();
                    foreach (var card in context.Set<Card>().AsNoTracking()
                        .Where(x => x.Tags.Any(t => ids.Contains(t.Id))).Select(x => new { x.Character, x.GameDeckId }).ToList())
                    {
                        characters.Add(card.Character);
                        users.Add(card.GameDeckId);
                    }
                }

                if (waifuIds.Count > 0)
                {
                    var ids = waifuIds.ToList();
                    foreach (var guildId in context.Set<Waifu>().AsNoTracking()
                        .Where(x => ids.Contains(x.Id)).Select(x => x.GuildOptionsId).Distinct().ToList())
                        guilds.Add(guildId);
                }

                if (packIds.Count > 0)
                {
                    var ids = packIds.ToList();
                    foreach (var deckId in context.Set<BoosterPack>().AsNoTracking()
                        .Where(x => ids.Contains(x.Id)).Select(x => x.GameDeckId).Distinct().ToList())
                        users.Add(deckId);
                }

                var tags = new List<string>();
                tags.AddRange(users.Select(CacheTags.User));
                tags.AddRange(characters.Select(CacheTags.Character));
                tags.AddRange(guilds.Select(CacheTags.Guild));
                if (mute) tags.Add(CacheTags.Mute);
                if (quiz) tags.Add(CacheTags.Quiz);

                if (tags.Count > 0)
                    QueryCacheManager.ExpireTag(tags.ToArray());
            }
        }
    }
}
