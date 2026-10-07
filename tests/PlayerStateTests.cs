using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Sanakan.Api;
using Sanakan.Api.Controllers;
using Sanakan.Api.Models;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Xunit;

namespace Artifacts
{
    public class PlayerStateTests
    {
        private static readonly DateTime Now = new DateTime(2026, 10, 3, 12, 0, 0);

        private static User NewUser(params TimeStatus[] statuses) => new User
        {
            Id = 42,
            TimeStatuses = statuses.ToList(),
            GameDeck = new GameDeck { Cards = new List<Card>(), Wishes = new List<WishlistObject>(), Figures = new List<Figure>() },
        };

        private static TimeStatus Status(StatusType type, long value, DateTime? endsAt, bool claimed = false)
            => new TimeStatus { Type = type, IValue = value, EndsAt = endsAt ?? DateTime.MinValue, BValue = claimed };

        private static Card ActiveCard(ulong id, int attack = 50, int defence = 30, int health = 300, bool active = true)
            => new Card { Id = id, Name = $"c{id}", Attack = attack, Defence = defence, Health = health, Active = active, Dere = Dere.Deredere, Tags = new List<Tag>() };

        // misje

        [Fact]
        public void Missions_ShowProgressCompletionAndClaim()
        {
            var user = NewUser(
                Status(StatusType.DPacket, 1, Now.AddHours(5)),
                Status(StatusType.DExpeditions, 3, Now.AddHours(5)),
                Status(StatusType.DPvp, 5, Now.AddHours(5), claimed: true));

            var missions = UserMissions.From(user, Now);

            var packet = missions.Daily.Single(x => x.Type == StatusType.DPacket);
            Assert.Equal(1, packet.Progress);
            Assert.Equal(2, packet.Required);
            Assert.False(packet.Completed);

            var exp = missions.Daily.Single(x => x.Type == StatusType.DExpeditions);
            Assert.True(exp.Completed);
            Assert.False(exp.Claimed);

            Assert.True(missions.Daily.Single(x => x.Type == StatusType.DPvp).Claimed);
            Assert.Equal(new[] { StatusType.WDaily, StatusType.WCardPlus }.OrderBy(x => x), missions.Weekly.Select(x => x.Type).OrderBy(x => x));
        }

        [Fact]
        public void Missions_ExpiredProgressIsZero_AndUserIsNotModified()
        {
            var user = NewUser(Status(StatusType.DPacket, 2, Now.AddHours(-1)));

            var missions = UserMissions.From(user, Now);

            var packet = missions.Daily.Single(x => x.Type == StatusType.DPacket);
            Assert.Equal(0, packet.Progress);
            Assert.Null(packet.ResetsAt);
            Assert.Single(user.TimeStatuses);
        }

        // limity

        [Fact]
        public void Limits_CooldownActiveAndAvailable()
        {
            var user = NewUser(Status(StatusType.Daily, 0, Now.AddHours(3)), Status(StatusType.Hourly, 0, Now.AddMinutes(-5)));

            var limits = UserLimits.From(user, Now, 5);

            var daily = limits.Cooldowns.Single(x => x.Type == StatusType.Daily);
            Assert.False(daily.Available);
            Assert.Equal(Now.AddHours(3), daily.AvailableAt);

            var hourly = limits.Cooldowns.Single(x => x.Type == StatusType.Hourly);
            Assert.True(hourly.Available);
            Assert.Null(hourly.AvailableAt);

            Assert.True(limits.Cooldowns.Single(x => x.Type == StatusType.Card).Available);
        }

        [Fact]
        public void Limits_FreeCardCooldownUsesUltimateReduction()
        {
            var user = NewUser(Status(StatusType.Card, 0, Now.AddHours(2)));
            user.GameDeck.Cards = Enumerable.Range(1, 5).Select(i => new Card { Id = (ulong)i, FromFigure = true, Quality = Quality.Alpha }).ToList();

            // 5 kart ultimate skraca czas o 5h, więc 2h pozostałe się kończą
            Assert.True(UserLimits.From(user, Now, 0).Cooldowns.Single(x => x.Type == StatusType.Card).Available);
        }

        [Fact]
        public void Limits_PacketCounter_DefaultLimitAndClamp()
        {
            var user = NewUser(Status(StatusType.Packet, 5, Now.AddHours(10)));

            var packet = UserLimits.From(user, Now, 0).Counters.Single(x => x.Type == StatusType.Packet);

            Assert.Equal(3, packet.Max);
            Assert.Equal(3, packet.Used);
        }

        [Fact]
        public void Limits_PvpCounter_ZeroWhenStatusExpired()
        {
            var user = NewUser(Status(StatusType.Pvp, 0, Now.AddHours(-1)));
            user.GameDeck.PVPDailyGamesPlayed = 7;

            Assert.Equal(0, UserLimits.From(user, Now, 0).Counters.Single(x => x.Type == StatusType.Pvp).Used);

            user.TimeStatuses = new List<TimeStatus> { Status(StatusType.Pvp, 0, Now.AddHours(5)) };
            var pvp = UserLimits.From(user, Now, 0).Counters.Single(x => x.Type == StatusType.Pvp);
            Assert.Equal(7, pvp.Used);
            Assert.Equal(10, pvp.Max);
        }

        // talia

        [Fact]
        public void Deck_NoActiveCards_CannotPlay()
        {
            var deck = UserDeck.From(NewUser(), Now);

            Assert.Equal(DeckPowerStatus.NotEnoughtCards, deck.PvpDeckStatus);
            Assert.False(deck.CanPlayPvp);
            Assert.Empty(deck.Cards);
        }

        [Fact]
        public void Deck_TooManyCards()
        {
            var user = NewUser();
            user.GameDeck.Cards = Enumerable.Range(1, 9).Select(i => ActiveCard((ulong)i)).ToList();

            Assert.Equal(DeckPowerStatus.TooManyCards, UserDeck.From(user, Now).PvpDeckStatus);
        }

        [Fact]
        public void Deck_PowerIsSumOfActiveCards_AndStatusFollowsLimits()
        {
            var user = NewUser();
            user.GameDeck.Cards = new List<Card> { ActiveCard(1), ActiveCard(2), ActiveCard(3, active: false) };

            var deck = UserDeck.From(user, Now);
            var expected = user.GameDeck.Cards.Where(x => x.Active).Sum(x => x.CalculateCardPower());

            Assert.Equal(2, deck.Cards.Count);
            Assert.Equal(expected, deck.Power, 6);

            var status = expected > deck.MaxPower ? DeckPowerStatus.TooHigh : expected < deck.MinPower ? DeckPowerStatus.TooLow : DeckPowerStatus.Ok;
            Assert.Equal(status, deck.PvpDeckStatus);
        }

        [Fact]
        public void Deck_DailyLimitReached_CannotPlay()
        {
            var user = NewUser(Status(StatusType.Pvp, 0, Now.AddHours(5)));
            user.GameDeck.PVPDailyGamesPlayed = 10;
            user.GameDeck.Cards = Enumerable.Range(1, 4).Select(i => ActiveCard((ulong)i, attack: 100, defence: 60)).ToList();

            var deck = UserDeck.From(user, Now);

            Assert.Equal(10, deck.PvpGamesToday);
            Assert.False(deck.CanPlayPvp);
        }

        [Fact]
        public void Deck_DoesNotChangeStoredDeckPower()
        {
            var user = NewUser();
            user.GameDeck.DeckPower = 123;
            user.GameDeck.Cards = new List<Card> { ActiveCard(1) };

            UserDeck.From(user, Now);

            Assert.Equal(123, user.GameDeck.DeckPower);
        }

        // życzenia i figurki

        [Fact]
        public void Wishlist_IncludesPrivateFlagAndEntries()
        {
            var user = NewUser();
            user.GameDeck.WishlistIsPrivate = true;
            user.GameDeck.Wishes = new List<WishlistObject>
            {
                new WishlistObject { Type = WishlistObjectType.Character, ObjectId = 5, ObjectName = "Asuna", Entry = WishlistEntryType.Persistent },
                new WishlistObject { Type = WishlistObjectType.Card, ObjectId = 77, ObjectName = "karta" },
            };

            var wishlist = UserWishlist.From(user.GameDeck);

            Assert.True(wishlist.IsPrivate);
            Assert.Equal(2, wishlist.Entries.Count);
            Assert.True(wishlist.Entries[0].Persistent);
            Assert.False(wishlist.Entries[1].Persistent);
        }

        [Fact]
        public void Figure_MissingPartsAreNull_AndCardIdOnlyWhenComplete()
        {
            var fig = new Figure { Id = 3, Name = "Megumin", SkeletonQuality = Quality.Beta, HeadQuality = Quality.Gamma, CreatedCardId = 999, IsComplete = false };

            var dto = UserFigure.From(fig);

            Assert.Equal(Quality.Beta, dto.SkeletonQuality);
            Assert.Equal(Quality.Gamma, dto.Parts[FigurePart.Head]);
            Assert.Null(dto.Parts[FigurePart.Body]);
            Assert.Equal(7, dto.Parts.Count);
            Assert.Null(dto.CreatedCardId);
            Assert.False(dto.AllPartsInstalled);
            Assert.False(dto.CanCreateUltimateCard);

            fig.IsComplete = true;
            Assert.Equal(999ul, UserFigure.From(fig).CreatedCardId);
        }

        // walidacja

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("dwa słowa")]
        [InlineData("tab\tbed")]
        public void TagName_Invalid(string name)
        {
            Assert.Equal(400, (PlayerController.ValidateTagName(name) as ObjectResult)?.StatusCode);
        }

        [Fact]
        public void TagName_TooLong_And_Valid()
        {
            Assert.NotNull(PlayerController.ValidateTagName(new string('a', PlayerController.MaxTagNameLength + 1)));
            Assert.Null(PlayerController.ValidateTagName("konie"));
        }

        [Fact]
        public void CardList_Validation()
        {
            Assert.NotNull(PlayerController.ValidateCardList(null));
            Assert.NotNull(PlayerController.ValidateCardList(new List<ulong>()));
            Assert.NotNull(PlayerController.ValidateCardList(Enumerable.Range(0, PlayerController.MaxCardsPerOperation + 1).Select(x => (ulong)x).ToList()));
            Assert.Null(PlayerController.ValidateCardList(new List<ulong> { 1, 2 }));
        }

        // ślad w logach

        private static HttpContext Context(string method, string path, int status, params Claim[] claims)
        {
            var ctx = new DefaultHttpContext();
            ctx.Request.Method = method;
            ctx.Request.Path = path;
            ctx.Response.StatusCode = status;
            if (claims.Length > 0)
                ctx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
            return ctx;
        }

        [Fact]
        public void Audit_PlayerChangeWithKey_IsLoggedWithApp()
        {
            var entry = ApiAudit.Describe(Context("PUT", "/api/waifu/waifu/5", 200,
                new Claim("DiscordId", "123"), new Claim("Player", "waifu_player"), new Claim("UserKeyApp", "strona")), 12);

            Assert.Equal("API: u123 app:strona PUT /api/waifu/waifu/5 -> 200 (12ms)", entry);
        }

        [Fact]
        public void Audit_PlayerWithJwt_IsMarkedAsToken()
        {
            var entry = ApiAudit.Describe(Context("POST", "/api/waifu/tags", 201, new Claim("DiscordId", "123")), 1);
            Assert.Contains("u123 app:token", entry);
        }

        [Fact]
        public void Audit_SiteChange_IsLogged()
        {
            var entry = ApiAudit.Describe(Context("PUT", "/api/user/discord/1/tc", 200, new Claim(ClaimTypes.Webpage, "Shinden")), 3);
            Assert.StartsWith("API: site:Shinden PUT", entry);
        }

        [Theory]
        [InlineData("GET")]
        [InlineData("HEAD")]
        [InlineData("OPTIONS")]
        public void Audit_ReadsAreNotLogged(string method)
        {
            Assert.Null(ApiAudit.Describe(Context(method, "/api/waifu/deck", 200, new Claim("DiscordId", "1")), 1));
        }

        [Fact]
        public void Audit_AnonymousIsNotLogged()
        {
            Assert.Null(ApiAudit.Describe(Context("POST", "/api/waifu/total/cards/0/10", 200), 1));
        }
    }
}
