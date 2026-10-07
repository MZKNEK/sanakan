#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Sanakan;
using Sanakan.Database;
using Sanakan.Database.Models;
using Sanakan.Database.Models.Configuration;
using Sanakan.Database.Models.Management;
using Sanakan.Extensions;
using Xunit;
using Z.EntityFramework.Plus;

namespace Artifacts
{
    public class CacheActivityTests
    {
        private class SqliteContext : DatabaseContext
        {
            private readonly string _path;

            public SqliteContext(string path) : base(new FakeConfig()) => _path = path;

            protected override void OnConfiguring(DbContextOptionsBuilder builder)
                => builder.UseSqlite($"Data Source={_path};Foreign Keys=False").AddInterceptors(CacheActivity.Interceptor);
        }

        private static string NewDb() => Path.Combine(Path.GetTempPath(), $"sanakan-cache-{Guid.NewGuid():N}.db");

        private static async Task<SqliteContext> OpenAsync(string db)
        {
            var ctx = new SqliteContext(db);
            await ctx.Database.EnsureCreatedAsync();
            return ctx;
        }

        private static async Task SeedCardAsync(string db, ulong id, ulong character, ulong deck)
        {
            using var ctx = await OpenAsync(db);
            ctx.Cards.Add(new Card { Id = id, Character = character, GameDeckId = deck });
            await ctx.SaveChangesAsync();
        }

        private static async Task<int> CachedCardCountAsync(string db, Func<IQueryable<Card>, IQueryable<Card>> filter, string tag)
        {
            using var ctx = await OpenAsync(db);
            var list = await filter(ctx.Cards.AsQueryable().AsNoTracking()).FromCacheAsync(new[] { tag });
            return list.Count();
        }

        private static async Task<int> CachedQuestionCountAsync(string db)
        {
            using var ctx = await OpenAsync(db);
            var list = await ctx.Questions.AsQueryable().AsNoTracking().FromCacheAsync(new[] { CacheTags.Quiz });
            return list.Count();
        }

        private static async Task<int> CachedPenaltyCountAsync(string db)
        {
            using var ctx = await OpenAsync(db);
            var list = await ctx.Penalties.AsQueryable().AsNoTracking().FromCacheAsync(new[] { CacheTags.Mute });
            return list.Count();
        }

        private static async Task<ulong> CachedGuildWaifuRoleAsync(string db, ulong guild)
        {
            using var ctx = await OpenAsync(db);
            var config = await ctx.GetCachedGuildFullConfigAsync(guild);
            return config?.WaifuRole ?? 0;
        }

        [Fact]
        public async Task CharacterCache_IsInvalidatedWhenCardAdded()
        {
            var db = NewDb();
            const ulong character = 70001;
            Func<IQueryable<Card>, IQueryable<Card>> byCharacter = q => q.Where(x => x.Character == character);

            await SeedCardAsync(db, 1, character, 1);
            Assert.Equal(1, await CachedCardCountAsync(db, byCharacter, CacheTags.Character(character)));

            await SeedCardAsync(db, 2, character, 2);
            Assert.Equal(2, await CachedCardCountAsync(db, byCharacter, CacheTags.Character(character)));
        }

        [Fact]
        public async Task CharacterCache_IsInvalidatedWhenAppearanceChanges()
        {
            var db = NewDb();
            const ulong oldCharacter = 70011, newCharacter = 70012;

            await SeedCardAsync(db, 1, oldCharacter, 1);
            Assert.Equal(1, await CachedCardCountAsync(db, q => q.Where(x => x.Character == oldCharacter), CacheTags.Character(oldCharacter)));
            Assert.Equal(0, await CachedCardCountAsync(db, q => q.Where(x => x.Character == newCharacter), CacheTags.Character(newCharacter)));

            using (var ctx = await OpenAsync(db))
            {
                var card = await ctx.Cards.FirstAsync(x => x.Id == 1);
                card.Character = newCharacter;
                await ctx.SaveChangesAsync();
            }

            Assert.Equal(0, await CachedCardCountAsync(db, q => q.Where(x => x.Character == oldCharacter), CacheTags.Character(oldCharacter)));
            Assert.Equal(1, await CachedCardCountAsync(db, q => q.Where(x => x.Character == newCharacter), CacheTags.Character(newCharacter)));
        }

        [Fact]
        public async Task UserCache_IsInvalidatedForNewAndPreviousOwner_WhenCardMoves()
        {
            var db = NewDb();
            const ulong oldOwner = 80001, newOwner = 80002;

            await SeedCardAsync(db, 10, 1, oldOwner);
            await SeedCardAsync(db, 11, 2, oldOwner);
            Assert.Equal(2, await CachedCardCountAsync(db, q => q.Where(x => x.GameDeckId == oldOwner), CacheTags.User(oldOwner)));

            using (var ctx = await OpenAsync(db))
            {
                var card = await ctx.Cards.FirstAsync(x => x.Id == 10);
                card.GameDeckId = newOwner;
                await ctx.SaveChangesAsync();
            }

            Assert.Equal(1, await CachedCardCountAsync(db, q => q.Where(x => x.GameDeckId == oldOwner), CacheTags.User(oldOwner)));
        }

        [Fact]
        public async Task Suppression_DisablesInvalidation()
        {
            var db = NewDb();
            const ulong character = 70003;
            Func<IQueryable<Card>, IQueryable<Card>> byCharacter = q => q.Where(x => x.Character == character);

            await SeedCardAsync(db, 1, character, 1);
            Assert.Equal(1, await CachedCardCountAsync(db, byCharacter, CacheTags.Character(character)));

            using (var ctx = await OpenAsync(db))
            {
                ctx.SuppressCacheInvalidation = true;
                ctx.Cards.Add(new Card { Id = 2, Character = character, GameDeckId = 2 });
                await ctx.SaveChangesAsync();
            }

            // brak unieważnienia -> cache nadal zwraca stary stan
            Assert.Equal(1, await CachedCardCountAsync(db, byCharacter, CacheTags.Character(character)));
        }

        [Fact]
        public async Task QuizCache_IsInvalidatedWhenQuestionAdded()
        {
            var db = NewDb();

            using (var ctx = await OpenAsync(db))
            {
                ctx.Questions.Add(new Question { Id = 1 });
                await ctx.SaveChangesAsync();
            }
            Assert.Equal(1, await CachedQuestionCountAsync(db));

            using (var ctx = await OpenAsync(db))
            {
                ctx.Questions.Add(new Question { Id = 2 });
                await ctx.SaveChangesAsync();
            }
            Assert.Equal(2, await CachedQuestionCountAsync(db));
        }

        [Fact]
        public async Task MuteCache_IsInvalidatedWhenPenaltyAdded()
        {
            var db = NewDb();

            using (var ctx = await OpenAsync(db))
            {
                ctx.Penalties.Add(new PenaltyInfo { Id = 1, User = 1, Guild = 1, Type = PenaltyType.Mute });
                await ctx.SaveChangesAsync();
            }
            Assert.Equal(1, await CachedPenaltyCountAsync(db));

            using (var ctx = await OpenAsync(db))
            {
                ctx.Penalties.Add(new PenaltyInfo { Id = 2, User = 2, Guild = 1, Type = PenaltyType.Mute });
                await ctx.SaveChangesAsync();
            }
            Assert.Equal(2, await CachedPenaltyCountAsync(db));
        }

        [Fact]
        public async Task GuildCache_IsInvalidatedWhenConfigChanges()
        {
            var db = NewDb();
            const ulong guild = 90001;

            using (var ctx = await OpenAsync(db))
            {
                ctx.Guilds.Add(new GuildOptions { Id = guild });
                await ctx.SaveChangesAsync();
            }
            Assert.Equal(0UL, await CachedGuildWaifuRoleAsync(db, guild));

            using (var ctx = await OpenAsync(db))
            {
                var config = await ctx.Guilds.FirstAsync(x => x.Id == guild);
                config.WaifuRole = 4242;
                await ctx.SaveChangesAsync();
            }

            Assert.Equal(4242UL, await CachedGuildWaifuRoleAsync(db, guild));
        }

        [Fact]
        public async Task CharacterCache_IsInvalidatedWhenTagIsRenamed()
        {
            var db = NewDb();
            const ulong character = 70021;
            const ulong tagId = 500;

            using (var ctx = await OpenAsync(db))
            {
                var tag = new Tag { Id = tagId, Name = "stara", GameDeckId = 1 };
                var card = new Card { Id = 1, Character = character, GameDeckId = 1, Tags = new List<Tag> { tag } };
                ctx.Cards.Add(card);
                await ctx.SaveChangesAsync();
            }

            Assert.Equal("stara", await CachedTagNameAsync(db, character));

            using (var ctx = await OpenAsync(db))
            {
                var tag = await ctx.Tags.FirstAsync(x => x.Id == tagId);
                tag.Name = "nowa";
                await ctx.SaveChangesAsync();
            }

            Assert.Equal("nowa", await CachedTagNameAsync(db, character));
        }

        [Fact]
        public async Task GuildCache_IsPerGuild_NotSharedAcrossGuilds()
        {
            var db = NewDb();
            const ulong guildA = 91001, guildB = 91002;

            using (var ctx = await OpenAsync(db))
            {
                ctx.Guilds.Add(new GuildOptions { Id = guildA });
                ctx.Guilds.Add(new GuildOptions { Id = guildB });
                await ctx.SaveChangesAsync();
            }

            // rozgrzewamy cache gildii A
            Assert.Equal(0UL, await CachedGuildWaifuRoleAsync(db, guildA));

            // zmieniamy tylko gildie B
            using (var ctx = await OpenAsync(db))
            {
                var config = await ctx.Guilds.FirstAsync(x => x.Id == guildB);
                config.WaifuRole = 777;
                await ctx.SaveChangesAsync();
            }

            // A nie może się zmienić, B musi być świeże (dawniej wspólny wpis całej tabeli)
            Assert.Equal(0UL, await CachedGuildWaifuRoleAsync(db, guildA));
            Assert.Equal(777UL, await CachedGuildWaifuRoleAsync(db, guildB));
        }

        private static async Task<string> CachedTagNameAsync(string db, ulong character)
        {
            using var ctx = await OpenAsync(db);
            var cards = await ctx.Cards.AsQueryable().Include(x => x.Tags).AsNoTracking()
                .Where(x => x.Character == character).FromCacheAsync(new[] { CacheTags.Character(character) });
            return cards.First().Tags.First().Name;
        }
    }
}
