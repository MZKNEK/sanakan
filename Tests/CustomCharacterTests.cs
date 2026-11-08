using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sanakan.Database;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.ShindenCompat;
using Xunit;

namespace Artifacts
{
    public class CustomCharacterTests : IDisposable
    {
        private const ulong Custom = CustomCharacters.IdStart + 3;
        private readonly SqliteConnection _connection = new SqliteConnection("Data Source=:memory:");

        private sealed class TestDatabase : DatabaseContext
        {
            private readonly SqliteConnection _connection;
            public TestDatabase(SqliteConnection connection) : base(new FakeConfig()) => _connection = connection;
            protected override void OnConfiguring(DbContextOptionsBuilder builder) => builder.UseSqlite(_connection);
        }

        public CustomCharacterTests()
        {
            _connection.Open();
            using var db = Open();
            db.Database.EnsureCreated();
            var user = new User().Default(5, DateTime.Now);
            user.GameDeck.Cards.Add(new Card { Character = Custom, Name = "Mira Kowalska", Title = "Sanakan", Unique = true, Tags = new List<Tag>() });
            db.Users.Add(user);
            db.SaveChanges();
        }

        public void Dispose() => _connection.Dispose();

        private DatabaseContext Open() => new TestDatabase(_connection);

        [Theory]
        [InlineData(50453, false)]
        [InlineData(CustomCharacters.IdStart, false)]
        [InlineData(CustomCharacters.IdStart + 1, true)]
        public void CustomIds_StartAboveShinden(ulong id, bool custom) => Assert.Equal(custom, CustomCharacters.IsCustom(id));

        [Fact]
        public async Task ShindenClient_NeverAsksShindenAboutCustomIds()
        {
            // a null api would throw if the request were sent
            var response = await new ShindenClient(null).GetCharacterInfoAsync(Custom);

            Assert.Equal(HttpStatusCode.NotFound, response.Code);
        }

        [Fact]
        public async Task CustomCharacter_IsDescribedFromItsCard()
        {
            var shinden = new Sanakan.Services.Shinden(null, null, null, new ListLogger(), new FixedTime())
            {
                CustomCharacterInfo = async id =>
                {
                    using var db = Open();
                    return await db.GetCustomCharacterInfoAsync(id);
                },
            };

            var info = await shinden.GetCharacterInfoAsync(Custom);

            Assert.Equal(Custom, info.Id);
            Assert.Equal("Mira Kowalska", info.ToString());
            Assert.Null(await shinden.GetCharacterInfoAsync(Custom + 1));
        }
    }
}
