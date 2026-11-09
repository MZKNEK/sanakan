#pragma warning disable 1591

using System.Data.Common;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Sanakan.Database
{
    // Makes SQLite compare text like MySQL's default collation: case and accents are ignored
    // (the same rules as the in-memory card search in WaifuController).
    public static class SqliteText
    {
        // Built-in name, so a connection without the override can still read the schema.
        public const string Collation = "NOCASE";

        private const CompareOptions Options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
        private static readonly CompareInfo Compare = CultureInfo.InvariantCulture.CompareInfo;

        public static readonly DbConnectionInterceptor Interceptor = new ConnectionSetup();

        public static void Register(SqliteConnection connection)
        {
            connection.CreateCollation(Collation, (x, y) => Compare.Compare(x, y, Options));
            // EF translates string.Contains/IndexOf to instr()
            connection.CreateFunction<string, string, long?>("instr",
                (text, part) => text == null || part == null ? null : Compare.IndexOf(text, part, Options) + 1, isDeterministic: true);
            RegisterPow(connection);
        }

        // MySQL has it built in, the deck power refresh needs it
        public static void RegisterPow(SqliteConnection connection)
            => connection.CreateFunction<double?, double?, double?>("pow",
                (x, y) => x == null || y == null ? null : System.Math.Pow(x.Value, y.Value), isDeterministic: true);

        private class ConnectionSetup : DbConnectionInterceptor
        {
            public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
            {
                if (connection is SqliteConnection sqlite)
                    Register(sqlite);
            }

            public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData,
                CancellationToken cancellationToken = default)
            {
                ConnectionOpened(connection, eventData);
                return Task.CompletedTask;
            }
        }
    }
}
