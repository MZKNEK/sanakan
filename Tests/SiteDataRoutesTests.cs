using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Sanakan.Api.Controllers;
using Sanakan.Api.Models;
using Sanakan.Config.Model;
using Sanakan.Database;
using Sanakan.Database.Models;
using Sanakan.Database.Models.Analytics;
using Sanakan.Database.Models.Management;
using Sanakan.Extensions;
using Xunit;

namespace Artifacts
{
    public class SiteDataRoutesTests : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"sanakan-site-data-{Guid.NewGuid():N}.db");
        private readonly FakeConfig _config = new FakeConfig();
        // cache entries expire relative to the real clock
        private readonly FixedTime _time = new FixedTime { Value = DateTime.Now };

        public SiteDataRoutesTests()
        {
            _config.Model.DatabaseProvider = DatabaseProvider.Sqlite;
            _config.Model.ConnectionString = $"Data Source={_path}";
            using var db = new DatabaseContext(_config);
            db.EnsureReady();
        }

        public void Dispose()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(_path); } catch (IOException) { }
        }

        private WaifuController Waifu() => ControllerHarness.Attach(new WaifuController(null, null, new FakeExecutor(), null, _config, _time, null, null, null, null));
        private ModerationController Moderation() => ControllerHarness.Attach(new ModerationController(null, _config, _time));

        private User AddUser(DatabaseContext db, ulong id, ulong shinden)
        {
            var user = new User().Default(id, _time.Now());
            user.Shinden = shinden;
            db.Users.Add(user);
            return user;
        }

        private static Card NewCard(ulong deck, ulong character, bool ultimate = false, Quality quality = Quality.Broken) => new Card
        {
            GameDeckId = deck, Character = character, Name = $"Card {character}", Title = "Title", FromFigure = ultimate, Quality = quality,
            Rarity = Rarity.E, Health = 10, Attack = 10, Defence = 10, CreationDate = DateTime.Now, Tags = new List<Tag>(),
        };

        [Fact]
        public async Task Stats_GivesUnopenedPacksAndCounters()
        {
            using (var db = new DatabaseContext(_config))
            {
                var user = AddUser(db, 1, 11);
                user.Stats.CreatedCardsFromItems = 3;
                user.Stats.SlotMachineGames = 40;
                user.Stats.IncomeInSc = 500;
                user.Stats.ScLost = 200;
                user.Stats.RightAnswers = 7;
                user.Stats.TotalAnswers = 9;
                user.Stats.TurnamentsWon = 2;
                user.GameDeck.BoosterPacks.Add(new BoosterPack { Name = "a", CardCnt = 2, Characters = new List<BoosterPackCharacter>(), RarityExcludedFromPack = new List<RarityExcluded>() });
                user.GameDeck.BoosterPacks.Add(new BoosterPack { Name = "b", CardCnt = 2, Characters = new List<BoosterPackCharacter>(), RarityExcludedFromPack = new List<RarityExcluded>() });
                AddUser(db, 2, 22).IsBlacklisted = true;
                db.SaveChanges();
            }

            int packs;
            using (var db = new DatabaseContext(_config))
                packs = db.BoosterPacks.Count(x => x.GameDeckId == 1);

            var stats = (await Waifu().GetUserSiteStatsAsync(11)).Value;
            Assert.True(packs >= 2);
            Assert.Equal(packs, stats.UnopenedBoosterPacks);
            Assert.Equal(3, stats.CreatedCardsFromItems);
            Assert.Equal(40, stats.SlotMachineGames);
            Assert.Equal(500, stats.IncomeInSc);
            Assert.Equal(200, stats.ScLost);
            Assert.Equal(7, stats.RightAnswers);
            Assert.Equal(9, stats.TotalAnswers);
            Assert.Equal(2, stats.TurnamentsWon);

            Assert.Equal(404, ((ObjectResult)(await Waifu().GetUserSiteStatsAsync(99)).Result).StatusCode);
            Assert.Equal(403, ((ObjectResult)(await Waifu().GetUserSiteStatsAsync(22)).Result).StatusCode);
            Assert.Equal(404, ((ObjectResult)(await Waifu().GetUserSiteStatsAsync(0)).Result).StatusCode);
        }

        [Fact]
        public async Task CharacterCounts_CountShindenAccountsAndWishlistsAndPage()
        {
            using (var db = new DatabaseContext(_config))
            {
                AddUser(db, 1, 11);
                AddUser(db, 2, 22);
                AddUser(db, 3, 0);
                db.SaveChanges();

                db.Cards.AddRange(NewCard(1, 100), NewCard(1, 100), NewCard(2, 100), NewCard(3, 100), NewCard(3, 200), NewCard(2, 300));
                db.WishlistCountData.AddRange(
                    new WishlistCount { Id = 100, Count = 5, ACount = 2, Name = "A" },
                    new WishlistCount { Id = 400, Count = 1, ACount = 0, Name = "D" },
                    new WishlistCount { Id = 500, Count = 0, ACount = 0, Name = "E" });
                db.SaveChanges();
            }

            var all = (await Waifu().GetCharacterCountsAsync()).Value.ToList();
            Assert.Equal(new ulong[] { 100, 300, 400 }, all.Select(x => x.CharacterId));

            var first = all[0];
            Assert.Equal(2, first.Owners);
            Assert.Equal(5, first.Wishlists);
            Assert.Equal(2, first.ActiveWishlists);
            Assert.Equal((1, 0, 0), (all[1].Owners, all[1].Wishlists, all[1].ActiveWishlists));
            Assert.Equal((0, 1, 0), (all[2].Owners, all[2].Wishlists, all[2].ActiveWishlists));

            var page = (await Waifu().GetCharacterCountsAsync(100, 1)).Value.ToList();
            Assert.Equal(300UL, Assert.Single(page).CharacterId);
            Assert.Equal(400UL, Assert.Single((await Waifu().GetCharacterCountsAsync(300, 5)).Value).CharacterId);
            Assert.Empty((await Waifu().GetCharacterCountsAsync(400, 5)).Value);
        }

        [Fact]
        public async Task FreeCardStatuses_AreShortenedByUltimateCardsLikeTheBot()
        {
            var now = _time.Now();
            using (var db = new DatabaseContext(_config))
            {
                // waits 10 h, one beta ultimate card takes 2 h off
                AddUser(db, 1, 11).TimeStatuses.Add(new TimeStatus { Type = StatusType.Card, EndsAt = now.AddHours(10) });
                // waits 1 h, but the reduction already ended it
                AddUser(db, 2, 22).TimeStatuses.Add(new TimeStatus { Type = StatusType.Card, EndsAt = now.AddHours(1) });
                // ended long ago
                AddUser(db, 3, 33).TimeStatuses.Add(new TimeStatus { Type = StatusType.Card, EndsAt = now.AddDays(-2) });
                // no Shinden account
                AddUser(db, 4, 0).TimeStatuses.Add(new TimeStatus { Type = StatusType.Card, EndsAt = now.AddHours(5) });
                // another status only
                AddUser(db, 5, 55).TimeStatuses.Add(new TimeStatus { Type = StatusType.Daily, EndsAt = now.AddHours(5) });
                db.SaveChanges();

                db.Cards.AddRange(NewCard(1, 100, true, Quality.Beta), NewCard(1, 101),
                    NewCard(2, 100, true, Quality.Beta), NewCard(2, 101, true, Quality.Beta));
                db.SaveChanges();
            }

            var statuses = (await Waifu().GetFreeCardStatusesAsync()).Value.ToList();
            var only = Assert.Single(statuses);
            Assert.Equal(11UL, only.ShindenId);
            Assert.Equal(now.AddHours(8), only.EndsAt);
            Assert.Equal(1UL, only.DiscordId);
            Assert.Equal(now.AddHours(10 - UserExtension.FreeCardCooldownHours), only.TakenAt);
            Assert.Null(only.OnServer);

            using (var db = new DatabaseContext(_config))
            {
                var user = await db.GetCachedFullUserAsync(1);
                var limits = UserLimits.From(user, now, 3);
                Assert.Equal(limits.Cooldowns.Single(x => x.Type == StatusType.Card).AvailableAt, only.EndsAt);
            }
        }

        private sealed class MembersWaifuController : WaifuController
        {
            private readonly HashSet<ulong> _members;
            public MembersWaifuController(FakeConfig config, FixedTime time, params ulong[] members)
                : base(null, null, new FakeExecutor(), null, config, time, null, null, null, null) => _members = members.ToHashSet();
            protected override Func<ulong, bool> MainGuildMembers() => _members.Contains;
        }

        [Fact]
        public async Task FreeCardStatuses_WithDays_AddPlayersWhoTookTheCardRecently()
        {
            var now = _time.Now();
            var hours = UserExtension.FreeCardCooldownHours;
            using (var db = new DatabaseContext(_config))
            {
                // waiting now
                AddUser(db, 1, 11).TimeStatuses.Add(new TimeStatus { Type = StatusType.Card, EndsAt = now.AddHours(5) });
                // took it 3 days ago, can take again
                AddUser(db, 2, 22).TimeStatuses.Add(new TimeStatus { Type = StatusType.Card, EndsAt = now.AddDays(-3).AddHours(hours) });
                // took it 8 days ago
                AddUser(db, 3, 33).TimeStatuses.Add(new TimeStatus { Type = StatusType.Card, EndsAt = now.AddDays(-8).AddHours(hours) });
                // took it 20 h ago, the reduction already ended the wait
                AddUser(db, 4, 44).TimeStatuses.Add(new TimeStatus { Type = StatusType.Card, EndsAt = now.AddHours(hours - 20) });
                db.SaveChanges();
                db.Cards.AddRange(NewCard(4, 100, true, Quality.Omega));
                db.SaveChanges();
            }

            var controller = ControllerHarness.Attach(new MembersWaifuController(_config, _time, 1, 4));

            var waiting = (await controller.GetFreeCardStatusesAsync()).Value.ToList();
            Assert.Equal(new ulong[] { 11 }, waiting.Select(x => x.ShindenId));

            var week = (await controller.GetFreeCardStatusesAsync(7)).Value.OrderBy(x => x.ShindenId).ToList();
            Assert.Equal(new ulong[] { 11, 22, 44 }, week.Select(x => x.ShindenId));
            Assert.Equal(new bool?[] { true, false, true }, week.Select(x => x.OnServer));
            Assert.Equal(now.AddDays(-3), week[1].TakenAt);
            Assert.Equal(now.AddHours(-20), week[2].TakenAt);
            Assert.True(week[2].EndsAt < now);
            Assert.Equal(now.AddHours(5), week[0].EndsAt);

            var month = (await controller.GetFreeCardStatusesAsync(30)).Value;
            Assert.Equal(4, month.Count());

            Assert.Equal(400, ((ObjectResult)(await controller.GetFreeCardStatusesAsync(31)).Result).StatusCode);
            Assert.Equal(400, ((ObjectResult)(await controller.GetFreeCardStatusesAsync(-1)).Result).StatusCode);
        }

        [Fact]
        public async Task ActivePenalties_GiveOnlyOngoingPenaltiesOfTheGuildAndUsers()
        {
            var now = _time.Now();
            using (var db = new DatabaseContext(_config))
            {
                db.Penalties.AddRange(
                    new PenaltyInfo { User = 1, Guild = 10, Type = PenaltyType.Mute, Reason = "spam", StartDate = now.AddHours(-1), DurationInHours = 24 },
                    new PenaltyInfo { User = 2, Guild = 10, Type = PenaltyType.Ban, Reason = "raid", StartDate = now.AddDays(-1), DurationInHours = 24 * 365 },
                    new PenaltyInfo { User = 1, Guild = 10, Type = PenaltyType.Mute, Reason = "old", StartDate = now.AddDays(-3), DurationInHours = 24 },
                    new PenaltyInfo { User = 1, Guild = 20, Type = PenaltyType.Mute, Reason = "other guild", StartDate = now, DurationInHours = 24 },
                    new PenaltyInfo { User = 3, Guild = 10, Type = PenaltyType.Mute, Reason = "not asked", StartDate = now, DurationInHours = 24 });
                db.SaveChanges();
            }

            var query = new ModerationController.ActivePenaltiesQuery { Guild = 10, Users = new List<ulong> { 1, 2, 2, 4 } };
            var penalties = (await Moderation().GetActivePenaltiesAsync(query)).Value.ToList();

            Assert.Equal(new[] { ("raid", PenaltyType.Ban), ("spam", PenaltyType.Mute) }, penalties.Select(x => (x.Reason, x.Type)));
            var mute = penalties.Single(x => x.User == 1);
            Assert.Equal(24, mute.DurationInHours);
            Assert.Equal(now.AddHours(-1), mute.StartDate);
            Assert.Equal(now.AddHours(23), mute.End);

            Assert.Empty((await Moderation().GetActivePenaltiesAsync(new ModerationController.ActivePenaltiesQuery { Guild = 10, Users = new List<ulong>() })).Value);

            var tooMany = new ModerationController.ActivePenaltiesQuery { Guild = 10, Users = Enumerable.Range(1, ModerationController.MaxPenaltyUsers + 1).Select(x => (ulong)x).ToList() };
            Assert.Equal(400, ((ObjectResult)(await Moderation().GetActivePenaltiesAsync(tooMany)).Result).StatusCode);
        }

        [Theory]
        [InlineData(typeof(WaifuController), nameof(WaifuController.GetUserSiteStatsAsync), "user/{id}/stats")]
        [InlineData(typeof(WaifuController), nameof(WaifuController.GetCharacterCountsAsync), "characters/counts")]
        [InlineData(typeof(WaifuController), nameof(WaifuController.GetFreeCardStatusesAsync), "statuses/card")]
        [InlineData(typeof(ModerationController), nameof(ModerationController.GetActivePenaltiesAsync), "penalties/active")]
        public void NewRoutes_NeedTheSitePermission(Type controller, string action, string template)
        {
            var method = controller.GetMethod(action);
            Assert.Equal(template, method.GetCustomAttributes(typeof(HttpMethodAttribute), true).Cast<HttpMethodAttribute>().Single().Template);
            Assert.Equal("Site", method.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single().Policy);
            Assert.Empty(method.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
        }
    }
}
