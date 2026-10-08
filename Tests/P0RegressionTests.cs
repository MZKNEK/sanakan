using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Microsoft.EntityFrameworkCore;
using Moq;
using Sanakan.Api.Models;
using Sanakan.Config;
using Sanakan.Config.Model;
using Sanakan.Database;
using Sanakan.Database.Models;
using Sanakan.Database.Models.Configuration;
using Sanakan.Extensions;
using Sanakan.Preconditions;
using Sanakan.Services.PocketWaifu;
using Sanakan.TypeReaders;
using Xunit;

namespace Artifacts
{
    public class ItemCountPairTypeReaderTests
    {
        private static TypeReaderResult Read(string input)
            => new ItemCountPairTypeReader().ReadAsync(null, input, null).GetAwaiter().GetResult();

        private static ItemCountPair Value(TypeReaderResult r) => (ItemCountPair)r.Values.First().Value;

        [Fact]
        public void WithoutCount_MeansWholeStack()
        {
            var result = Read("3");

            Assert.True(result.IsSuccess);
            Assert.Equal((uint)3, Value(result).Item);
            Assert.Equal((uint)0, Value(result).Count);
        }

        [Fact]
        public void WithCount_IsParsed()
        {
            var result = Read("3:5");

            Assert.True(result.IsSuccess);
            Assert.Equal((uint)3, Value(result).Item);
            Assert.Equal((uint)5, Value(result).Count);
        }

        [Fact]
        public void ForcePrefix_IsParsed()
        {
            var result = Read("!3:2");

            Assert.True(result.IsSuccess);
            Assert.True(Value(result).Force);
            Assert.Equal((uint)3, Value(result).Item);
            Assert.Equal((uint)2, Value(result).Count);
        }

        [Theory]
        [InlineData("3:abc")]
        [InlineData("3:-5")]
        [InlineData("3:1.5")]
        [InlineData("3:0")]
        public void InvalidCount_IsRejected(string input)
        {
            var result = Read(input);

            Assert.False(result.IsSuccess);
            Assert.Equal(CommandError.ParseFailed, result.Error);
        }

        [Theory]
        [InlineData("abc")]
        [InlineData(":5")]
        public void InvalidItem_IsRejected(string input)
        {
            Assert.False(Read(input).IsSuccess);
        }
    }

    public class ConfigCollectionDefaultsTests
    {
        [Fact]
        public void GuildOptions_HasInitialisedCollections()
        {
            var config = new GuildOptions();

            Assert.NotNull(config.SelfRoles);
            Assert.NotNull(config.ModeratorRoles);
            Assert.NotNull(config.RolesPerLevel);
            Assert.NotNull(config.Lands);
            Assert.NotNull(config.Raports);
            Assert.NotNull(config.CommandChannels);
            Assert.NotNull(config.IgnoredChannels);
            Assert.NotNull(config.ChannelsWithoutExp);
            Assert.NotNull(config.ChannelsWithoutSupervision);
        }

        [Fact]
        public void WaifuConfig_HasInitialisedChannels()
        {
            var waifu = new Sanakan.Database.Models.Configuration.Waifu();

            Assert.NotNull(waifu.CommandChannels);
            Assert.NotNull(waifu.FightChannels);
        }
    }

    public class DevPreconditionNullTests
    {
        private class Provider : IServiceProvider
        {
            private readonly object _service;
            public Provider(object service) => _service = service;
            public object GetService(Type serviceType) => serviceType == typeof(IConfig) ? _service : null;
        }

        private static ICommandContext Context()
        {
            var user = new Mock<IUser>();
            user.SetupGet(x => x.Id).Returns(1);
            var ctx = new Mock<ICommandContext>();
            ctx.SetupGet(x => x.User).Returns(user.Object);
            return ctx.Object;
        }

        [Fact]
        public async Task RequireDev_WithNullDevList_DoesNotThrowAndDenies()
        {
            var config = new FakeConfig();
            config.Model.Dev = null;

            var result = await new RequireDev().CheckPermissionsAsync(Context(), null, new Provider(config));

            Assert.False(result.IsSuccess);
        }

        [Fact]
        public async Task RequireDevOrTester_WithNullDevList_DoesNotThrow()
        {
            var config = new FakeConfig();
            config.Model.Dev = null;

            var result = await new RequireDevOrTester().CheckPermissionsAsync(Context(), null, new Provider(config));

            Assert.False(result.IsSuccess);
        }
    }

    public class CardBoosterPackModelTests
    {
        [Fact]
        public void ToRealPack_WithoutPool_DoesNotThrow()
        {
            var pack = new CardBoosterPack { Count = 1, Name = "x" }.ToRealPack();

            Assert.Equal(0UL, pack.Title);
        }

        [Fact]
        public void ToRealPack_ListPoolWithNullCharacters_DoesNotThrow()
        {
            var pack = new CardBoosterPack
            {
                Count = 1,
                Pool = new BoosterPackPool { Type = CardsPoolType.List, Character = null },
            }.ToRealPack();

            Assert.Empty(pack.Characters);
        }

        [Fact]
        public void ToRealPack_TitlePool_UsesTitleId()
        {
            var pack = new CardBoosterPack
            {
                Count = 1,
                Pool = new BoosterPackPool { Type = CardsPoolType.Title, TitleId = 77 },
            }.ToRealPack();

            Assert.Equal(77UL, pack.Title);
        }
    }

    public class ShindenIdZeroTests
    {
        private class SqliteContext : DatabaseContext
        {
            private readonly string _path;
            public SqliteContext(string path) : base(new FakeConfig()) => _path = path;

            protected override void OnConfiguring(DbContextOptionsBuilder builder)
                => builder.UseSqlite($"Data Source={_path}");
        }

        private static string NewDb() => Path.Combine(Path.GetTempPath(), $"sanakan-shinden-{Guid.NewGuid():N}.db");

        private static async Task<SqliteContext> SeedAsync(string db)
        {
            var ctx = new SqliteContext(db);
            await ctx.Database.EnsureCreatedAsync();
            ctx.Users.Add(new User { Id = 1, Shinden = 0 });
            ctx.Users.Add(new User { Id = 2, Shinden = 42 });
            await ctx.SaveChangesAsync();
            return ctx;
        }

        [Fact]
        public async Task GetCachedFullUserByShindenId_Zero_ReturnsNull()
        {
            var db = NewDb();
            try
            {
                using var ctx = await SeedAsync(db);
                Assert.Null(await ctx.GetCachedFullUserByShindenIdAsync(0));
            }
            finally
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                try { File.Delete(db); } catch { }
            }
        }

        [Fact]
        public async Task GetCachedFullUserByShindenId_FindsLinkedUser()
        {
            var db = NewDb();
            try
            {
                using var ctx = await SeedAsync(db);
                var user = await ctx.GetCachedFullUserByShindenIdAsync(42);

                Assert.NotNull(user);
                Assert.Equal(2UL, user.Id);
            }
            finally
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                try { File.Delete(db); } catch { }
            }
        }
    }
}
