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
using Z.EntityFramework.Plus;

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
                .Include(x => x.WaifuConfig).ThenInclude(x => x.FightChannels).AsNoTracking().AsSplitQuery().FromCacheAsync(new string[] { CacheTags.Guild(guildId) })).FirstOrDefault();
        }

        public static async Task<IEnumerable<PenaltyInfo>> GetCachedFullPenalties(this Database.DatabaseContext context)
        {
            return (await context.Penalties.AsQueryable().Include(x => x.Roles).AsNoTracking().AsSplitQuery().FromCacheAsync(new string[] { CacheTags.Mute })).ToList();
        }

        public static async Task<User> GetCachedFullUserAsync(this Database.DatabaseContext context, ulong userId)
        {
            return (await context.Users.AsQueryable().Where(x => x.Id == userId).Include(x => x.Stats).Include(x => x.SMConfig).Include(x => x.TimeStatuses).Include(x => x.GameDeck).ThenInclude(x => x.PvPStats)
                .Include(x => x.GameDeck).ThenInclude(x => x.Items).Include(x => x.GameDeck).ThenInclude(x => x.Cards).ThenInclude(x => x.Tags).Include(x => x.GameDeck)
                .ThenInclude(x => x.BoosterPacks).ThenInclude(x => x.Characters).Include(x => x.GameDeck).ThenInclude(x => x.BoosterPacks).ThenInclude(x => x.RarityExcludedFromPack)
                .Include(x => x.GameDeck).ThenInclude(x => x.ExpContainer).Include(x => x.GameDeck).ThenInclude(x => x.Wishes)
                .Include(x => x.GameDeck).ThenInclude(x => x.Figures).Include(x => x.GameDeck).ThenInclude(x => x.Tags).AsNoTracking().AsSplitQuery().FromCacheAsync(new string[] { CacheTags.User(userId) })).FirstOrDefault();
        }

        public static async Task<User> GetCachedNoGameDeckUserAsync(this Database.DatabaseContext context, ulong userId)
        {
            return (await context.Users.AsQueryable().Where(x => x.Id == userId).Include(x => x.Stats).Include(x => x.SMConfig).Include(x => x.TimeStatuses)
                .AsNoTracking().AsSplitQuery().FromCacheAsync(new string[] { CacheTags.User(userId) })).FirstOrDefault();
        }

        public static async Task<User> GetCachedFullUserByShindenIdAsync(this Database.DatabaseContext context, ulong shindenId)
        {
            var userId = await context.Users.AsQueryable().AsNoTracking()
                .Where(x => shindenId != 0 && x.Shinden == shindenId).Select(x => x.Id).FirstOrDefaultAsync();

            return userId == 0 ? null : await context.GetCachedFullUserAsync(userId);
        }

        public static async Task<List<User>> GetCachedAllUsersLiteAsync(this Database.DatabaseContext context, ulong ignoreId = 0)
        {
            return (await context.Users.AsQueryable().AsNoTracking().AsSplitQuery().Where(x => x.Id != ignoreId).FromCacheAsync(new MemoryCacheEntryOptions{ AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(4) })).ToList();
        }

        public static async Task<GameDeck> GetCachedUserGameDeckAsync(this Database.DatabaseContext context, ulong userId)
        {
            return (await context.GameDecks.AsQueryable().Where(x => x.UserId == userId).Include(x => x.Cards).ThenInclude(x => x.Tags).AsNoTracking().AsSplitQuery().FromCacheAsync(new string[] { CacheTags.User(userId) })).FirstOrDefault();
        }

        public static async Task<List<User>> GetCachedAllUsersAsync(this Database.DatabaseContext context)
        {
            return (await context.Users.AsQueryable().Include(x => x.Stats).Include(x => x.SMConfig).Include(x => x.TimeStatuses).Include(x => x.GameDeck).ThenInclude(x => x.PvPStats).Include(x => x.GameDeck).ThenInclude(x => x.Wishes)
                .Include(x => x.GameDeck).ThenInclude(x => x.Items).Include(x => x.GameDeck).ThenInclude(x => x.ExpContainer).Include(x => x.GameDeck).ThenInclude(x => x.Cards).ThenInclude(x => x.Tags)
                .Include(x => x.GameDeck).ThenInclude(x => x.BoosterPacks).ThenInclude(x => x.Characters).Include(x => x.GameDeck).ThenInclude(x => x.BoosterPacks).ThenInclude(x => x.RarityExcludedFromPack)
                .Include(x => x.GameDeck).ThenInclude(x => x.Figures).AsNoTracking().AsSplitQuery()
                .FromCacheAsync(new MemoryCacheEntryOptions{ AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6) })).ToList();
        }

        public static IQueryable<User> GetQueryableAllUsers(this Database.DatabaseContext context)
        {
            return context.Users.AsQueryable().Include(x => x.Stats).Include(x => x.SMConfig).Include(x => x.TimeStatuses).Include(x => x.GameDeck).ThenInclude(x => x.PvPStats).Include(x => x.GameDeck).ThenInclude(x => x.Wishes)
                .Include(x => x.GameDeck).ThenInclude(x => x.Items).Include(x => x.GameDeck).ThenInclude(x => x.ExpContainer).Include(x => x.GameDeck).ThenInclude(x => x.Cards).ThenInclude(x => x.Tags)
                .Include(x => x.GameDeck).ThenInclude(x => x.BoosterPacks).ThenInclude(x => x.Characters).Include(x => x.GameDeck).ThenInclude(x => x.BoosterPacks).ThenInclude(x => x.RarityExcludedFromPack)
                .Include(x => x.GameDeck).ThenInclude(x => x.Figures).AsNoTracking().AsSplitQuery();
        }

        public static Task<IEnumerable<User>> QFromCacheAsync(this IQueryable<User> list)
        {
            return list.FromCacheAsync(new MemoryCacheEntryOptions{ AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6) });
        }

        public static async Task<List<GameDeck>> GetCachedPlayersForPVP(this Database.DatabaseContext context, ulong ignore = 1)
        {
            return (await context.GameDecks.AsQueryable().Where(x => x.DeckPower > UserExtension.MIN_DECK_POWER && x.DeckPower < UserExtension.MAX_DECK_POWER && x.UserId != ignore && x.CardsInDeck <= UserExtension.MAX_CARDS_IN_DECK && x.CardsInDeck > 0).AsNoTracking().AsSplitQuery().FromCacheAsync(new MemoryCacheEntryOptions{ AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(2) })).ToList();
        }

        public static async Task<User> GetUserOrCreateSimpleAsync(this Database.DatabaseContext context, ulong userId)
        {
            var user = await context.Users.AsQueryable().Where(x => x.Id == userId).Include(x => x.Stats).Include(x => x.SMConfig).Include(x => x.TimeStatuses).Include(x => x.GameDeck).ThenInclude(x => x.Wishes)
                .Include(x => x.GameDeck).ThenInclude(x => x.ExpContainer).Include(x => x.GameDeck).ThenInclude(x => x.Tags).Include(x => x.GameDeck).ThenInclude(x => x.Items).AsSplitQuery().FirstOrDefaultAsync();

            if (user == null)
            {
                user = user.Default(userId, DateTime.Now);
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

        public static async Task<User> GetUserOrCreateAsync(this Database.DatabaseContext context, ulong userId)
        {
            var user = await context.Users.AsQueryable().Where(x => x.Id == userId).Include(x => x.Stats).Include(x => x.SMConfig).Include(x => x.TimeStatuses).Include(x => x.GameDeck).ThenInclude(x => x.PvPStats).Include(x => x.GameDeck).ThenInclude(x => x.Wishes)
                .Include(x => x.GameDeck).ThenInclude(x => x.Items).Include(x => x.GameDeck).ThenInclude(x => x.Cards).ThenInclude(x => x.Tags).Include(x => x.GameDeck)
                .ThenInclude(x => x.ExpContainer).Include(x => x.GameDeck).ThenInclude(x => x.BoosterPacks).ThenInclude(x => x.Characters).Include(x => x.GameDeck)
                .ThenInclude(x => x.BoosterPacks).ThenInclude(x => x.RarityExcludedFromPack).Include(x => x.GameDeck).ThenInclude(x => x.Tags)
                .Include(x => x.GameDeck).ThenInclude(x => x.Figures).AsSplitQuery().FirstOrDefaultAsync();

            if (user == null)
            {
                user = user.Default(userId, DateTime.Now);
                await context.Users.AddAsync(user);
            }

            return user;
        }

        public static async Task RemoveUserAsync(this Database.DatabaseContext context, ulong userId, bool removeCards = false)
        {
            var fakeu = await context.GetUserOrCreateAsync(1);
            var user = await context.GetUserOrCreateAsync(userId);

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

            QueryCacheManager.ExpireTag(new string[] { CacheTags.User(userId) });
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
        public static async Task<bool> RemoveUserLeftBeforeAsync(this Database.DatabaseContext context, ulong userId, DateTime cutoff)
        {
            if (!await context.Users.AsQueryable().AnyAsync(x => x.Id == userId && x.LeftAt != null && x.LeftAt <= cutoff))
                return false;

            await context.RemoveUserAsync(userId);
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
