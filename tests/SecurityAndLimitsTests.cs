#pragma warning disable 1591

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Sanakan.Api.Controllers;
using Sanakan.Api.Models;
using Xunit;

namespace Artifacts
{
    public class WaifuFilterValidationTests
    {
        private static WaifuController NewController()
            => ControllerHarness.Attach(new WaifuController(null, null, new FakeExecutor(), null, new FakeConfig(), new FixedTime(),
                new MemoryCache(new MemoryCacheOptions()), null, null));

        [Fact]
        public async Task TotalCards_NullFilter_Returns400()
        {
            var result = await NewController().GetCardsWithOffsetAndFilterAsync(0, 10, null);
            Assert.Equal(400, result.StatusOf());
        }

        [Fact]
        public async Task TotalCards_LongSearchText_Returns400()
        {
            var filter = new CardsQueryFilter { SearchText = new string('a', 500) };
            var result = await NewController().GetCardsWithOffsetAndFilterAsync(0, 10, filter);
            Assert.Equal(400, result.StatusOf());
        }

        [Fact]
        public async Task TotalCards_TooManyCardIds_Returns400()
        {
            var filter = new CardsQueryFilter { CardIds = Enumerable.Range(0, 5000).Select(x => (ulong)x).ToList() };
            var result = await NewController().GetCardsWithOffsetAndFilterAsync(0, 10, filter);
            Assert.Equal(400, result.StatusOf());
        }

        [Fact]
        public async Task UserCards_TooManyCharIds_Returns400()
        {
            var filter = new CardsQueryFilter { CharIds = Enumerable.Range(0, 5000).Select(x => (ulong)x).ToList() };
            var result = await NewController().GetUsersCardsByShindenIdWithOffsetAndFilterAsync(1, 0, 10, filter);
            Assert.Equal(400, result.StatusOf());
        }

        [Fact]
        public async Task UserActivity_TooManyUsers_Returns400()
        {
            var users = Enumerable.Range(0, 5000).Select(x => (ulong)x).ToList();
            var result = await NewController().GetUsersActivitiesAsync(10, users);
            Assert.Equal(400, result.StatusOf());
        }
    }
}
