#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Sanakan.Database.Models;
using Sanakan.Database.Models.Configuration;
using Sanakan.Database.Models.Management;

namespace Sanakan.Extensions
{
    public static class ContextExtension
    {
        public static async Task<GuildOptions> GetGuildConfigOrCreateAsync(this Database.DatabaseContext context, ulong guildId, bool dontCreate = false)
        {
            var config = await context.Guilds.AsQueryable().Include(x => x.IgnoredChannels).Include(x => x.ChannelsWithoutExp).Include(x => x.ChannelsWithoutSupervision).Include(x => x.CommandChannels).Include(x => x.SelfRoles)
                .Include(x => x.Lands).Include(x => x.ModeratorRoles).Include(x => x.RolesPerLevel).Include(x => x.WaifuConfig).ThenInclude(x => x.CommandChannels).Include(x => x.Raports)
                .Include(x => x.WaifuConfig).ThenInclude(x => x.FightChannels).AsSplitQuery().FirstOrDefaultAsync(x => x.Id == guildId);

            if (config == null && !dontCreate)
            {
                config = new GuildOptions
                {
                    Id = guildId,
                    SafariLimit = 50,
                    WaifuConfig = new Waifu()
                };
                await context.Guilds.AddAsync(config);
            }
            return config;
        }

        public static async Task<GuildOptions> GetCachedGuildFullConfigAsync(this Database.DatabaseContext context, ulong guildId)
        {
            return (await context.Guilds.AsQueryable().Where(x => x.Id == guildId).Include(x => x.IgnoredChannels).Include(x => x.ChannelsWithoutExp).Include(x => x.ChannelsWithoutSupervision).Include(x => x.CommandChannels).Include(x => x.SelfRoles)
                .Include(x => x.Lands).Include(x => x.ModeratorRoles).Include(x => x.RolesPerLevel).Include(x => x.WaifuConfig).ThenInclude(x => x.CommandChannels).Include(x => x.Raports)
                .Include(x => x.WaifuConfig).ThenInclude(x => x.FightChannels).AsNoTracking().AsSplitQuery().FromCacheAsync(context, $"guild-full-{guildId}", CacheTags.Guild(guildId))).FirstOrDefault();
        }

        public static async Task<IEnumerable<PenaltyInfo>> GetCachedFullPenalties(this Database.DatabaseContext context)
        {
            return (await context.Penalties.AsQueryable().Include(x => x.Roles).AsNoTracking().AsSplitQuery().FromCacheAsync(context, "penalties-full", CacheTags.Mute)).ToList();
        }

        public static async Task<User> GetCachedFullUserAsync(this Database.DatabaseContext context, ulong userId)
        {
            return (await context.Users.AsQueryable().Where(x => x.Id == userId).Include(x => x.Stats).Include(x => x.SMConfig).Include(x => x.TimeStatuses).Include(x => x.GameDeck).ThenInclude(x => x.PvPStats)
                .Include(x => x.GameDeck).ThenInclude(x => x.Items).Include(x => x.GameDeck).ThenInclude(x => x.Cards).ThenInclude(x => x.Tags).Include(x => x.GameDeck)
                .ThenInclude(x => x.BoosterPacks).ThenInclude(x => x.Characters).Include(x => x.GameDeck).ThenInclude(x => x.BoosterPacks).ThenInclude(x => x.RarityExcludedFromPack)
                .Include(x => x.GameDeck).ThenInclude(x => x.ExpContainer).Include(x => x.GameDeck).ThenInclude(x => x.Wishes)
                .Include(x => x.GameDeck).ThenInclude(x => x.Figures).Include(x => x.GameDeck).ThenInclude(x => x.Tags).AsNoTracking().AsSplitQuery().FromCacheAsync(context, $"user-full-{userId}", CacheTags.User(userId))).FirstOrDefault();
        }

        public static async Task<User> GetCachedNoGameDeckUserAsync(this Database.DatabaseContext context, ulong userId)
        {
            return (await context.Users.AsQueryable().Where(x => x.Id == userId).Include(x => x.Stats).Include(x => x.SMConfig).Include(x => x.TimeStatuses)
                .AsNoTracking().AsSplitQuery().FromCacheAsync(context, $"user-nodeck-{userId}", CacheTags.User(userId))).FirstOrDefault();
        }

        public static async Task<User> GetCachedFullUserByShindenIdAsync(this Database.DatabaseContext context, ulong shindenId)
        {
            var userId = await context.Users.AsQueryable().AsNoTracking()
                .Where(x => shindenId != 0 && x.Shinden == shindenId).Select(x => x.Id).FirstOrDefaultAsync();

            return userId == 0 ? null : await context.GetCachedFullUserAsync(userId);
        }

        public static async Task<List<User>> GetCachedAllUsersLiteAsync(this Database.DatabaseContext context, ulong ignoreId = 0)
        {
            return (await context.Users.AsQueryable().AsNoTracking().AsSplitQuery().Where(x => x.Id != ignoreId).FromCacheAsync(context, $"users-lite-{ignoreId}", new MemoryCacheEntryOptions{ AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(4) })).ToList();
        }

        public static async Task<GameDeck> GetCachedUserGameDeckAsync(this Database.DatabaseContext context, ulong userId)
        {
            return (await context.GameDecks.AsQueryable().Where(x => x.UserId == userId).Include(x => x.Cards).ThenInclude(x => x.Tags).AsNoTracking().AsSplitQuery().FromCacheAsync(context, $"user-deck-{userId}", CacheTags.User(userId))).FirstOrDefault();
        }

        public static async Task<List<User>> GetCachedAllUsersAsync(this Database.DatabaseContext context)
        {
            return (await context.Users.AsQueryable().Include(x => x.Stats).Include(x => x.SMConfig).Include(x => x.TimeStatuses).Include(x => x.GameDeck).ThenInclude(x => x.PvPStats).Include(x => x.GameDeck).ThenInclude(x => x.Wishes)
                .Include(x => x.GameDeck).ThenInclude(x => x.Items).Include(x => x.GameDeck).ThenInclude(x => x.ExpContainer).Include(x => x.GameDeck).ThenInclude(x => x.Cards).ThenInclude(x => x.Tags)
                .Include(x => x.GameDeck).ThenInclude(x => x.BoosterPacks).ThenInclude(x => x.Characters).Include(x => x.GameDeck).ThenInclude(x => x.BoosterPacks).ThenInclude(x => x.RarityExcludedFromPack)
                .Include(x => x.GameDeck).ThenInclude(x => x.Figures).AsNoTracking().AsSplitQuery()
                .FromCacheAsync(context, "users-all-full", new MemoryCacheEntryOptions{ AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6) })).ToList();
        }

        public const string PvpPlayersCacheTag = "pvp-players";

        public static async Task<List<GameDeck>> GetCachedPlayersForPVP(this Database.DatabaseContext context, ulong ignore = 1)
        {
            // one shared entry instead of one copy per player, the caller is excluded in memory
            var all = await context.GameDecks.AsQueryable().Where(x => x.DeckPower > UserExtension.MIN_DECK_POWER && x.DeckPower < UserExtension.MAX_DECK_POWER && x.CardsInDeck <= UserExtension.MAX_CARDS_IN_DECK && x.CardsInDeck > 0).AsNoTracking().AsSplitQuery().FromCacheAsync(context, "pvp-players", new MemoryCacheEntryOptions{ AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15) }, PvpPlayersCacheTag);
            return all.Where(x => x.UserId != ignore).ToList();
        }

        public static async Task<User> GetUserOrCreateSimpleAsync(this Database.DatabaseContext context, ulong userId, Services.Time.ISystemTime time)
        {
            var user = await context.Users.AsQueryable().Where(x => x.Id == userId).Include(x => x.Stats).Include(x => x.SMConfig).Include(x => x.TimeStatuses).Include(x => x.GameDeck).ThenInclude(x => x.Wishes)
                .Include(x => x.GameDeck).ThenInclude(x => x.ExpContainer).Include(x => x.GameDeck).ThenInclude(x => x.Tags).Include(x => x.GameDeck).ThenInclude(x => x.Items).AsSplitQuery().FirstOrDefaultAsync();

            if (user == null)
            {
                user = user.Default(userId, time.Now());
                await context.Users.AddAsync(user);
            }

            return user;
        }

        public static async Task<Tag> GetTagAsync(this Database.DatabaseContext context, Services.PocketWaifu.TagHelper helper, string tag, ulong ownerId)
        {
            if (string.IsNullOrEmpty(tag)) return null;
            var internalTagId = helper.GetTagId(tag);
            return internalTagId != 0 ? await context.Tags.AsQueryable().FirstOrDefaultAsync(x => x.Id == internalTagId)
                : await context.Tags.AsQueryable().FirstOrDefaultAsync(x => x.Name.Equals(tag) && x.GameDeckId == ownerId);
        }

        internal static async Task<Dictionary<ulong, long>> CountUserCardsByTagsAsync(this Database.DatabaseContext context, ulong userId, IEnumerable<ulong> tagIds)
        {
            var ids = tagIds.Distinct().ToList();
            return await context.TagCardRelations.AsQueryable().AsNoTracking()
                .Where(x => x.Card.GameDeckId == userId && ids.Contains(x.TagId))
                .GroupBy(x => x.TagId).Select(x => new { x.Key, Count = x.LongCount() })
                .ToDictionaryAsync(x => x.Key, x => x.Count);
        }

        public static async Task<User> GetUserOrCreateAsync(this Database.DatabaseContext context, ulong userId, Services.Time.ISystemTime time)
        {
            var user = await context.Users.AsQueryable().Where(x => x.Id == userId).Include(x => x.Stats).Include(x => x.SMConfig).Include(x => x.TimeStatuses).Include(x => x.GameDeck).ThenInclude(x => x.PvPStats).Include(x => x.GameDeck).ThenInclude(x => x.Wishes)
                .Include(x => x.GameDeck).ThenInclude(x => x.Items).Include(x => x.GameDeck).ThenInclude(x => x.Cards).ThenInclude(x => x.Tags).Include(x => x.GameDeck)
                .ThenInclude(x => x.ExpContainer).Include(x => x.GameDeck).ThenInclude(x => x.BoosterPacks).ThenInclude(x => x.Characters).Include(x => x.GameDeck)
                .ThenInclude(x => x.BoosterPacks).ThenInclude(x => x.RarityExcludedFromPack).Include(x => x.GameDeck).ThenInclude(x => x.Tags)
                .Include(x => x.GameDeck).ThenInclude(x => x.Figures).AsSplitQuery().FirstOrDefaultAsync();

            if (user == null)
            {
                user = user.Default(userId, time.Now());
                await context.Users.AddAsync(user);
            }

            return user;
        }

        public static async Task RemoveUserAsync(this Database.DatabaseContext context, ulong userId, Services.Time.ISystemTime time, bool removeCards = false)
        {
            // cards of removed users go to this account; its own cards are not needed here
            var fakeu = await context.GetUserOrCreateSimpleAsync(1, time);
            var user = await context.GetUserOrCreateAsync(userId, time);

            if (!removeCards)
            {
                foreach (var card in user.GameDeck.Cards)
                {
                    card.InCage = false;
                    card.Tags.Clear();
                    card.LastIdOwner = userId;
                    card.GameDeckId = fakeu.GameDeck.Id;
                }
            }

            if (!user.GameDeck.WishlistIsPrivate)
                await context.ChangeWishlistCountsAsync(user.GameDeck.Wishes, -1);

            context.Users.Remove(user);
            await context.SaveChangesAsync();

            QueryCache.ExpireTag(new string[] { CacheTags.User(userId) });
        }

        public static async Task MarkUserLeftAsync(this Database.DatabaseContext context, ulong userId, DateTime now)
        {
            var user = await context.Users.AsQueryable().FirstOrDefaultAsync(x => x.Id == userId);
            if (user == null) return;

            user.LeftAt = now;
            await context.SaveChangesAsync();
        }

        public static async Task<bool> ClearUserLeftAsync(this Database.DatabaseContext context, ulong userId)
        {
            var user = await context.Users.AsQueryable().FirstOrDefaultAsync(x => x.Id == userId && x.LeftAt != null);
            if (user == null) return false;

            user.LeftAt = null;
            await context.SaveChangesAsync();
            return true;
        }

        public static Task<List<ulong>> GetUsersLeftBeforeAsync(this Database.DatabaseContext context, DateTime cutoff)
        {
            return context.Users.AsQueryable().AsNoTracking().Where(x => x.LeftAt != null && x.LeftAt <= cutoff).Select(x => x.Id).ToListAsync();
        }

        // checked again right before removal, the user could have come back since the scan
        public static async Task<bool> RemoveUserLeftBeforeAsync(this Database.DatabaseContext context, ulong userId, DateTime cutoff, Services.Time.ISystemTime time)
        {
            if (!await context.Users.AsQueryable().AnyAsync(x => x.Id == userId && x.LeftAt != null && x.LeftAt <= cutoff))
                return false;

            await context.RemoveUserAsync(userId, time);
            return true;
        }

        // a made-up character exists as long as any card of it does, its name comes from that card
        public static async Task<ShindenCompat.Models.ICharacterInfo> GetCustomCharacterInfoAsync(this Database.DatabaseContext context, ulong characterId)
        {
            var name = await context.Cards.AsQueryable().AsNoTracking().Where(x => x.Character == characterId).Select(x => x.Name).FirstOrDefaultAsync();
            if (name == null)
                return null;

            var space = name.IndexOf(' ');
            return new ShindenCompat.Models.Entities.CharacterInfo
            {
                Id = characterId,
                FirstName = space < 0 ? name : name[..space],
                LastName = space < 0 ? "" : name[(space + 1)..],
            };
        }

        // users without a leave date are skipped by the daily cleanup, e.g. those who left while the bot was offline
        public static Task<List<ulong>> GetUsersWithoutLeaveDateAsync(this Database.DatabaseContext context)
        {
            return context.Users.AsQueryable().AsNoTracking().Where(x => x.LeftAt == null && x.Id != 1).Select(x => x.Id).ToListAsync();
        }

        // checked again right before removal, the user could have come back or got a leave date since the scan
        public static async Task<bool> RemoveUserWithoutLeaveDateAsync(this Database.DatabaseContext context, ulong userId, Services.Time.ISystemTime time)
        {
            if (userId == 1 || !await context.Users.AsQueryable().AnyAsync(x => x.Id == userId && x.LeftAt == null))
                return false;

            await context.RemoveUserAsync(userId, time);
            return true;
        }

        public static async Task<bool> IsUserMutedOnGuildAsync(this Database.DatabaseContext context, ulong userId, ulong guildId)
        {
            return await context.Penalties.AsQueryable().AsNoTracking().AnyAsync(x => x.User == userId && x.Guild == guildId && x.Type == PenaltyType.Mute);
        }

        public static async Task<User> GetBaseUserAndDontTrackAsync(this Database.DatabaseContext context, ulong userId)
        {
            return await context.Users.AsQueryable().AsNoTracking().Include(x => x.Stats).Include(x => x.GameDeck).ThenInclude(x => x.Tags)
                .Include(x => x.GameDeck).ThenInclude(x => x.ExpContainer).FirstOrDefaultAsync(x => x.Id == userId);
        }

        public static async Task<User> GetUserAndDontTrackAsync(this Database.DatabaseContext context, ulong userId)
        {
            return await context.Users.AsQueryable().Include(x => x.Stats).Include(x => x.SMConfig).Include(x => x.TimeStatuses).Include(x => x.GameDeck).ThenInclude(x => x.PvPStats).Include(x => x.GameDeck).ThenInclude(x => x.Wishes)
                .Include(x => x.GameDeck).ThenInclude(x => x.Items).Include(x => x.GameDeck).ThenInclude(x => x.Cards).ThenInclude(x => x.Tags).Include(x => x.GameDeck)
                .ThenInclude(x => x.ExpContainer).Include(x => x.GameDeck).ThenInclude(x => x.BoosterPacks).ThenInclude(x => x.Characters).Include(x => x.GameDeck)
                .ThenInclude(x => x.BoosterPacks).ThenInclude(x => x.RarityExcludedFromPack)
                .Include(x => x.GameDeck).ThenInclude(x => x.Figures).AsNoTracking().AsSplitQuery().FirstOrDefaultAsync(x => x.Id == userId);
        }

        // the user row with its stats, slot machine settings, time statuses and the deck row, without any deck collection
        public static async Task<User> GetUserOrCreateLightAsync(this Database.DatabaseContext context, ulong userId, Services.Time.ISystemTime time)
        {
            var user = await context.Users.AsQueryable().Where(x => x.Id == userId).Include(x => x.Stats).Include(x => x.SMConfig)
                .Include(x => x.TimeStatuses).Include(x => x.GameDeck).FirstOrDefaultAsync();

            if (user == null)
            {
                user = user.Default(userId, time.Now());
                await context.Users.AddAsync(user);
            }

            return user;
        }

        // For commands working on a few cards: items, figures, the experience container and the packs (without their
        // characters) are loaded, of the cards only the given ones. Collections left out stay null, so code that
        // reaches for them fails loudly instead of acting on missing data.
        public static async Task<User> GetUserOrCreateWithCardsAsync(this Database.DatabaseContext context, ulong userId, Services.Time.ISystemTime time, params ulong[] cardIds)
        {
            var user = await context.Users.AsQueryable().Where(x => x.Id == userId).Include(x => x.Stats).Include(x => x.SMConfig).Include(x => x.TimeStatuses)
                .Include(x => x.GameDeck).ThenInclude(x => x.Items).Include(x => x.GameDeck).ThenInclude(x => x.Figures)
                .Include(x => x.GameDeck).ThenInclude(x => x.ExpContainer).Include(x => x.GameDeck).ThenInclude(x => x.BoosterPacks)
                .Include(x => x.GameDeck).ThenInclude(x => x.Cards.Where(c => cardIds.Contains(c.Id))).ThenInclude(x => x.Tags)
                .AsSplitQuery().FirstOrDefaultAsync();

            if (user == null)
            {
                user = user.Default(userId, time.Now());
                await context.Users.AddAsync(user);
            }

            return user;
        }

        // a duel needs the deck row and its active cards, not the whole collection nor the fight history
        public static async Task<User> GetUserOrCreateWithActiveCardsAsync(this Database.DatabaseContext context, ulong userId, Services.Time.ISystemTime time)
        {
            var user = await context.Users.AsQueryable().Where(x => x.Id == userId).Include(x => x.Stats).Include(x => x.SMConfig).Include(x => x.TimeStatuses)
                .Include(x => x.GameDeck).ThenInclude(x => x.ExpContainer)
                .Include(x => x.GameDeck).ThenInclude(x => x.Cards.Where(c => c.Active)).ThenInclude(x => x.Tags)
                .AsSplitQuery().FirstOrDefaultAsync();

            if (user == null)
            {
                user = user.Default(userId, time.Now());
                await context.Users.AddAsync(user);
            }

            return user;
        }

        // the free card needs only the ultimate cards (cooldown reduction); the card limit is checked with CountCardsAsync
        public static async Task<User> GetUserOrCreateWithUltimateCardsAsync(this Database.DatabaseContext context, ulong userId, Services.Time.ISystemTime time)
        {
            var user = await context.Users.AsQueryable().Where(x => x.Id == userId).Include(x => x.Stats).Include(x => x.SMConfig).Include(x => x.TimeStatuses)
                .Include(x => x.GameDeck).ThenInclude(x => x.ExpContainer)
                .Include(x => x.GameDeck).ThenInclude(x => x.Cards.Where(c => c.FromFigure))
                .AsSplitQuery().FirstOrDefaultAsync();

            if (user == null)
            {
                user = user.Default(userId, time.Now());
                await context.Users.AddAsync(user);
            }

            return user;
        }

        public static Task<int> CountCardsAsync(this Database.DatabaseContext context, ulong userId)
            => context.Cards.AsQueryable().CountAsync(x => x.GameDeckId == userId);

        public static Task<int> CountCardsOnExpeditionAsync(this Database.DatabaseContext context, ulong userId)
            => context.Cards.AsQueryable().CountAsync(x => x.GameDeckId == userId && x.Expedition != CardExpedition.None);

        // the karma reversal reward flips the yami and raito cards, no other reward touches the collection
        public static async Task<User> GetUserOrCreateForLotteryAsync(this Database.DatabaseContext context, ulong userId, Services.Time.ISystemTime time)
        {
            var user = await context.GetUserOrCreateWithCardsAsync(userId, time);
            await context.LoadCardsAsync(user.GameDeck, x => x.Dere == Dere.Yami || x.Dere == Dere.Raito);
            return user;
        }

        // adds the player's matching cards to the already loaded part of the collection
        public static async Task LoadCardsAsync(this Database.DatabaseContext context, GameDeck deck, System.Linq.Expressions.Expression<Func<Card, bool>> filter)
        {
            if (context.Entry(deck).State == EntityState.Added) return;

            var deckId = deck.Id;
            await context.Cards.AsQueryable().Where(x => x.GameDeckId == deckId).Where(filter).Include(x => x.Tags).LoadAsync();
        }

        // fills the deck's wishlist with only the entry for this character
        public static Task LoadCharacterWishAsync(this Database.DatabaseContext context, GameDeck deck, ulong characterId)
            => context.LoadCharacterWishesAsync(deck, new[] { characterId });

        public static async Task LoadCharacterWishesAsync(this Database.DatabaseContext context, GameDeck deck, IEnumerable<ulong> characterIds)
        {
            deck.Wishes ??= new List<WishlistObject>();
            if (context.Entry(deck).State == EntityState.Added) return;

            var ids = characterIds.Distinct().ToList();
            var deckId = deck.Id;
            await context.Wishes.AsQueryable().Where(x => x.GameDeckId == deckId && x.Type == WishlistObjectType.Character && ids.Contains(x.ObjectId)).LoadAsync();
        }

        // the receiving player's wishes that these cards fulfil: the card itself or its character
        public static async Task LoadWishesForCardsAsync(this Database.DatabaseContext context, GameDeck deck, IEnumerable<Card> cards)
        {
            deck.Wishes ??= new List<WishlistObject>();
            if (context.Entry(deck).State == EntityState.Added) return;

            var cardIds = cards.Select(x => x.Id).Distinct().ToList();
            var characterIds = cards.Select(x => x.Character).Distinct().ToList();
            var deckId = deck.Id;
            await context.Wishes.AsQueryable().Where(x => x.GameDeckId == deckId && ((x.Type == WishlistObjectType.Card && cardIds.Contains(x.ObjectId))
                || (x.Type == WishlistObjectType.Character && characterIds.Contains(x.ObjectId)))).LoadAsync();
        }

        // opening packs needs the packs with their contents; the new cards are only added, the collection is counted with CountCardsAsync
        public static async Task<User> GetUserOrCreateWithBoosterPacksAsync(this Database.DatabaseContext context, ulong userId, Services.Time.ISystemTime time)
        {
            var user = await context.Users.AsQueryable().Where(x => x.Id == userId).Include(x => x.Stats).Include(x => x.SMConfig).Include(x => x.TimeStatuses)
                .Include(x => x.GameDeck).ThenInclude(x => x.ExpContainer)
                .Include(x => x.GameDeck).ThenInclude(x => x.BoosterPacks).ThenInclude(x => x.Characters)
                .Include(x => x.GameDeck).ThenInclude(x => x.BoosterPacks).ThenInclude(x => x.RarityExcludedFromPack)
                .AsSplitQuery().FirstOrDefaultAsync();

            if (user == null)
            {
                user = user.Default(userId, time.Now());
                await context.Users.AddAsync(user);
            }

            user.GameDeck.Cards ??= new List<Card>();
            return user;
        }

        public static Task<User> GetUserWithItemsAsync(this Database.DatabaseContext context, ulong userId)
            => context.Users.AsQueryable().Include(x => x.GameDeck).ThenInclude(x => x.Items).AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId);

        public static async Task<int> RecalculateWishlistCountsAsync(this Database.DatabaseContext context, DateTime now)
        {
            var counts = await context.WishlistCountData.ToListAsync();
            var wishlists = await context.GameDecks.AsQueryable().AsSplitQuery().Include(x => x.User).Include(x => x.Wishes).AsNoTracking()
                .Where(x => !x.WishlistIsPrivate).ToListAsync();

            var decksByCharacter = wishlists.SelectMany(x => x.Wishes.Where(c => c.Type == WishlistObjectType.Character),
                (deck, wish) => (deck, wish.ObjectId)).ToLookup(x => x.ObjectId, x => x.deck);

            int updated = 0;
            foreach (var w in counts)
            {
                var decks = decksByCharacter[w.Id].ToList();
                var count = decks.Count;
                var aCount = decks.Count(x => x.IsUserActive(now));
                if (w.Count == count && w.ACount == aCount)
                    continue;

                w.Count = count;
                w.ACount = aCount;
                updated++;
            }

            if (updated > 0)
                await context.SaveChangesAsync();

            return updated;
        }

        public static async Task ChangeWishlistCountsAsync(this Database.DatabaseContext context, IEnumerable<WishlistObject> wishes, int by)
        {
            var characters = wishes.Where(x => x.AffectsWishlistCount()).ToList();
            if (characters.Count == 0) return;

            var ids = characters.Select(x => x.ObjectId).Distinct().ToList();
            var counts = await context.WishlistCountData.AsQueryable().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
            foreach (var wish in characters)
            {
                if (!counts.TryGetValue(wish.ObjectId, out var ww))
                {
                    ww = new Database.Models.Analytics.WishlistCount { Id = wish.ObjectId, Name = wish.ObjectName };
                    await context.WishlistCountData.AddAsync(ww);
                    counts.Add(ww.Id, ww);
                }

                ww.Count = Math.Max(0, ww.Count + by);
                ww.ACount = Math.Max(0, ww.ACount + by);
            }
        }

        public static Database.Models.Analytics.WishlistCount CreateOrChangeWishlistCountBy(this Database.DatabaseContext context, ulong id, string name, int by = 1, int aBy = 1)
        {
            var ww = context.WishlistCountData.AsQueryable().FirstOrDefault(x => x.Id == id);
            if (ww == null)
            {
                ww = new Database.Models.Analytics.WishlistCount
                {
                    Id = id,
                    Name = name,
                    Count = by < 0 ? 0 : by,
                    ACount = aBy < 0 ? 0 : aBy
                };
                context.WishlistCountData.Add(ww);
                return ww;
            }

            ww.Count += by;
            ww.ACount += aBy;
            if (ww.Count < 0)
                ww.Count = 0;

            if (ww.ACount < 0)
                ww.ACount = 0;

            return ww;
        }

        public static async Task<bool> CreateOrChangeWishlistCountByAsync(this Database.DatabaseContext context, ulong id, string name, int by = 1, int aBy = 1, bool setTo = false)
        {
            var ww = await context.WishlistCountData.AsQueryable().FirstOrDefaultAsync(x => x.Id == id);
            if (ww == null)
            {
                ww = new Database.Models.Analytics.WishlistCount
                {
                    Id = id,
                    Name = name,
                    Count = by < 0 ? 0 : by,
                    ACount = aBy < 0 ? 0 : aBy
                };
                await context.WishlistCountData.AddAsync(ww);
                return false;
            }

            bool update = setTo && (ww.Count == by);
            update = update || (setTo && (ww.ACount == aBy));

            if (setTo)
            {
                ww.Count = by;
                ww.ACount = aBy;
            }
            else
            {
                ww.Count += by;
                ww.ACount += aBy;
            }

            if (ww.Count < 0)
                ww.Count = 0;

            if (ww.ACount < 0)
                ww.ACount = 0;

            if (ww.Name != name)
                ww.Name = name;

            return update;
        }

        public static async Task<Database.Models.Analytics.WishlistCount> CreateOrChangeWishlistCountByAsync(this DbSet<Database.Models.Analytics.WishlistCount> wwCount, ulong id, string name, int by = 1, int aBy = 1, bool setTo = false)
        {
            var ww = await wwCount.AsQueryable().FirstOrDefaultAsync(x => x.Id == id);
            if (ww == null)
            {
                ww = new Database.Models.Analytics.WishlistCount
                {
                    Id = id,
                    Name = name,
                    Count = by < 0 ? 0 : by,
                    ACount = aBy < 0 ? 0 : aBy
                };
                await wwCount.AddAsync(ww);
                return ww;
            }

            if (setTo)
            {
                ww.Count = by;
                ww.ACount = aBy;
            }
            else
            {
                ww.Count += by;
                ww.ACount += aBy;
            }

            if (ww.Count < 0)
                ww.Count = 0;

            if (ww.ACount < 0)
                ww.ACount = 0;

            if (ww.Name != name)
                ww.Name = name;

            return ww;
        }
    }
}
