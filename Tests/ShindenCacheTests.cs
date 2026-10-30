using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Sanakan.Database.Models;
using Sanakan.Services.Cache;
using Sanakan.Services.PocketWaifu;
using Xunit;

namespace Artifacts
{
    public class ShindenCacheTests : IDisposable
    {
        private static readonly TimeSpan Day = TimeSpan.FromDays(1);
        private static readonly TimeSpan Retry = TimeSpan.FromMinutes(5);

        private readonly string _dir = Path.Combine(Path.GetTempPath(), "sanakan-cache-" + Guid.NewGuid().ToString("N"));
        private readonly DateTime _now = new FixedTime().Now();

        public void Dispose()
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, true);
        }

        private string FileIn(string name) => Path.Combine(_dir, name);

        private static Func<Task<T>> Returns<T>(T value, Action onCall = null) => () =>
        {
            onCall?.Invoke();
            return Task.FromResult(value);
        };

        private static Func<Task<T>> Throws<T>(Action onCall = null) => () =>
        {
            onCall?.Invoke();
            throw new Exception("NotFound");
        };

        [Fact]
        public async Task CachedValue_FreshValue_IsNotFetchedAgain()
        {
            var cache = new CachedValue<List<ulong>>(Day, Retry);
            int calls = 0;

            await cache.GetAsync(Returns(new List<ulong> { 1 }, () => calls++), _now);
            var value = await cache.GetAsync(Returns(new List<ulong> { 2 }, () => calls++), _now.AddHours(23));

            Assert.Equal(1, calls);
            Assert.Equal(new List<ulong> { 1 }, value);
        }

        [Fact]
        public async Task CachedValue_FailedRefresh_KeepsOldValueAndWaitsBeforeRetry()
        {
            var cache = new CachedValue<List<ulong>>(Day, Retry);
            int calls = 0;
            await cache.GetAsync(Returns(new List<ulong> { 1 }), _now);

            var afterDay = _now.Add(Day);
            var value = await cache.GetAsync(Throws<List<ulong>>(() => calls++), afterDay);
            Assert.Equal(new List<ulong> { 1 }, value);
            Assert.Equal("NotFound", cache.LastError);

            value = await cache.GetAsync(Throws<List<ulong>>(() => calls++), afterDay.AddMinutes(4));
            Assert.Equal(new List<ulong> { 1 }, value);
            Assert.Equal(1, calls);

            value = await cache.GetAsync(Returns(new List<ulong> { 2 }, () => calls++), afterDay.Add(Retry));
            Assert.Equal(new List<ulong> { 2 }, value);
            Assert.Equal(2, calls);
            Assert.Null(cache.LastError);
        }

        [Fact]
        public async Task CachedValue_NullResult_KeepsOldValue()
        {
            var cache = new CachedValue<List<ulong>>(Day, Retry);
            await cache.GetAsync(Returns(new List<ulong> { 1 }), _now);

            var value = await cache.GetAsync(Returns<List<ulong>>(null), _now.Add(Day));

            Assert.Equal(new List<ulong> { 1 }, value);
            Assert.NotNull(cache.LastError);
        }

        [Fact]
        public async Task CachedValue_Empty_RetriesAfterAMinute()
        {
            var cache = new CachedValue<List<ulong>>(Day, TimeSpan.FromHours(1));
            int calls = 0;

            Assert.Null(await cache.GetAsync(Throws<List<ulong>>(() => calls++), _now));
            Assert.Null(await cache.GetAsync(Throws<List<ulong>>(() => calls++), _now.AddSeconds(30)));
            Assert.Equal(1, calls);

            Assert.NotNull(await cache.GetAsync(Returns(new List<ulong> { 1 }, () => calls++), _now.AddMinutes(1)));
            Assert.Equal(2, calls);
        }

        [Fact]
        public async Task CachedValue_IsRestoredFromFileAfterRestart()
        {
            var file = FileIn("pool.json");
            await new CachedValue<List<ulong>>(Day, Retry, file).GetAsync(Returns(new List<ulong> { 7, 8 }), _now);

            int calls = 0;
            var restored = new CachedValue<List<ulong>>(Day, Retry, file);
            var value = await restored.GetAsync(Returns(new List<ulong> { 9 }, () => calls++), _now.AddHours(1));

            Assert.Equal(0, calls);
            Assert.Equal(new List<ulong> { 7, 8 }, value);
            Assert.Equal(_now, restored.UpdatedAt);
        }

        [Fact]
        public async Task CachedValue_RestoredStaleValue_IsUsedWhenRefreshFails()
        {
            var file = FileIn("pool.json");
            await new CachedValue<List<ulong>>(Day, Retry, file).GetAsync(Returns(new List<ulong> { 7 }), _now);

            var restored = new CachedValue<List<ulong>>(Day, Retry, file);
            var value = await restored.GetAsync(Throws<List<ulong>>(), _now.AddDays(3));

            Assert.Equal(new List<ulong> { 7 }, value);
        }

        [Fact]
        public async Task CachedValue_BrokenFile_IsIgnored()
        {
            var file = FileIn("pool.json");
            Directory.CreateDirectory(_dir);
            File.WriteAllText(file, "{ not json");
            var logger = new ListLogger();

            var cache = new CachedValue<List<ulong>>(Day, Retry, file, logger);
            var value = await cache.GetAsync(Returns(new List<ulong> { 1 }), _now);

            Assert.Equal(new List<ulong> { 1 }, value);
            Assert.NotEmpty(logger.Messages);
            Assert.Equal(new List<ulong> { 1 }, await new CachedValue<List<ulong>>(Day, Retry, file).GetAsync(Throws<List<ulong>>(), _now));
        }

        [Fact]
        public async Task KeyedCache_UnreachableWithoutStaleValue_Throws()
        {
            var cache = new KeyedCache<ulong, string>(Day, Retry, Day);

            await Assert.ThrowsAsync<Exception>(() => cache.GetAsync(1, Throws<string>(), _now));
        }

        [Fact]
        public async Task KeyedCache_Unreachable_ReturnsStaleAndPausesOtherRefreshes()
        {
            var cache = new KeyedCache<ulong, string>(Day, Retry, TimeSpan.FromDays(30));
            await cache.GetAsync(1, Returns("one"), _now);
            await cache.GetAsync(2, Returns("two"), _now);
            int calls = 0;

            var later = _now.Add(Day);
            Assert.Equal("one", await cache.GetAsync(1, Throws<string>(() => calls++), later));
            Assert.Equal("two", await cache.GetAsync(2, Throws<string>(() => calls++), later));
            Assert.Equal(1, calls);

            Assert.Equal("TWO", await cache.GetAsync(2, Returns("TWO", () => calls++), later.Add(Retry)));
            Assert.Equal(2, calls);
        }

        [Fact]
        public async Task KeyedCache_NotFound_KeepsStaleOnlyForThatKey()
        {
            var cache = new KeyedCache<ulong, string>(Day, Retry, TimeSpan.FromDays(30));
            await cache.GetAsync(1, Returns("one"), _now);
            await cache.GetAsync(2, Returns("two"), _now);
            int calls = 0;

            var later = _now.Add(Day);
            Assert.Equal("one", await cache.GetAsync(1, Returns<string>(null, () => calls++), later));
            Assert.Equal("one", await cache.GetAsync(1, Returns<string>(null, () => calls++), later.AddMinutes(1)));
            Assert.Equal("TWO", await cache.GetAsync(2, Returns("TWO", () => calls++), later));
            Assert.Equal(2, calls);
        }

        [Fact]
        public async Task KeyedCache_NotFoundWithoutStaleValue_ReturnsNullAndIsNotRemembered()
        {
            var cache = new KeyedCache<ulong, string>(Day, Retry, Day);

            Assert.Null(await cache.GetAsync(1, Returns<string>(null), _now));
            Assert.Equal("one", await cache.GetAsync(1, Returns("one"), _now));
        }

        [Fact]
        public async Task KeyedCache_DropsEntriesNotRefreshedForKeepFor()
        {
            var cache = new KeyedCache<ulong, string>(TimeSpan.FromHours(8), Retry, Day);
            await cache.GetAsync(1, Returns("one"), _now);

            await cache.GetAsync(2, Returns("two"), _now.Add(Day));

            Assert.Null(cache.Peek(1));
            Assert.Equal("two", cache.Peek(2));
        }

        [Fact]
        public async Task KeyedCache_IsRestoredFromFileAfterRestart()
        {
            var file = FileIn("names.json");
            var cache = new KeyedCache<ulong, string>(Day, Retry, TimeSpan.FromDays(30), file);
            await cache.GetAsync(1, Returns("one"), _now);
            await cache.GetAsync(2, Returns("two"), _now.AddMinutes(1));
            await cache.SaveAsync(_now.AddMinutes(1));

            int calls = 0;
            var restored = new KeyedCache<ulong, string>(Day, Retry, TimeSpan.FromDays(30), file);
            Assert.Equal("one", restored.Peek(1));
            Assert.Equal("two", await restored.GetAsync(2, Returns("x", () => calls++), _now.AddHours(1)));
            Assert.Equal(0, calls);
        }

        [Fact]
        public async Task KeyedCache_SavesWithoutExplicitCall()
        {
            var file = FileIn("names.json");
            var cache = new KeyedCache<ulong, string>(Day, Retry, TimeSpan.FromDays(30), file);

            await cache.GetAsync(1, Returns("one"), _now);

            Assert.Equal("one", new KeyedCache<ulong, string>(Day, Retry, TimeSpan.FromDays(30), file).Peek(1));
        }

        [Fact]
        public void PoolCharacter_KeepsDataAfterSerialization()
        {
            var original = new Shinden.Models.Entities.CharacterInfoTitle(15, "Lamperouge", "Lelouch", 1234, "Code Geass");

            var copy = JsonConvert.DeserializeObject<PoolCharacter>(JsonConvert.SerializeObject(new PoolCharacter(original)));

            Assert.Equal(original.Id, copy.Id);
            Assert.Equal(original.ToString(), copy.ToString());
            Assert.Equal(original.TitleName, copy.TitleName);
            Assert.Equal(original.HasImage, copy.HasImage);
            Assert.Equal(original.PictureUrl, copy.PictureUrl);
            Assert.Equal(original.CharacterUrl, copy.CharacterUrl);
        }

        [Fact]
        public async Task ShindenService_UsesSavedPoolWhenShindenIsUnavailable()
        {
            var character = new PoolCharacter(new Shinden.Models.Entities.CharacterInfoTitle(15, "Lamperouge", "Lelouch", 1234, "Code Geass"));
            await CacheFile.WriteAsync(FileIn("pool-anime.json"), new { UpdatedAt = _now.AddDays(-3), Value = new List<PoolCharacter> { character } });

            var time = new FixedTime();
            var shinden = new Sanakan.Services.Shinden(null, null, null, new ListLogger(), time, _dir);

            var random = await shinden.GetRandomCharacterAsync(CharacterPoolType.Anime);
            Assert.Equal(15ul, random.CharInfo.Id);
            Assert.Equal(character.ToString(), random.CharInfo.ToString());

            Assert.NotNull(await shinden.FindInPoolAsync(CharacterPoolType.Anime, 15));
            Assert.Null(await shinden.FindInPoolAsync(CharacterPoolType.Anime, 16));
        }

        [Fact]
        public async Task ShindenService_NoPoolAndNoConnection_ReturnsError()
        {
            var shinden = new Sanakan.Services.Shinden(null, null, null, new ListLogger(), new FixedTime());

            var random = await shinden.GetRandomCharacterAsync(CharacterPoolType.All);

            Assert.Null(random.CharInfo);
            Assert.False(string.IsNullOrEmpty(random.Code));
            Assert.Null(await shinden.FindInPoolAsync(CharacterPoolType.All, 1));
            Assert.Null(await shinden.GetCharacterInfoAsync(1));
            Assert.Equal(0ul, await shinden.GetRandomSeasonTitleAsync());
        }
    }
}
