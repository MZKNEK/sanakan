#pragma warning disable 1591

using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Sanakan.Database
{
    // A player's card list filters by the deck and sorts by another column. MySQL then often walks that column's index
    // in order and filters the deck on the way, which for some sorts reads millions of pages instead of the player's
    // cards. Queries tagged with Tag get a hint that turns this off for them only.
    public static class CardListHint
    {
        public const string Tag = "card-list";

        public const string Hint = "/*+ SET_VAR(optimizer_switch='prefer_ordering_index=off') */";

        public static readonly DbCommandInterceptor Interceptor = new HintInterceptor();

        public static string Apply(string sql)
        {
            if (!sql.StartsWith("-- " + Tag, StringComparison.Ordinal))
                return sql;

            var select = sql.IndexOf("SELECT", StringComparison.Ordinal);
            return select < 0 ? sql : sql.Insert(select + "SELECT".Length, " " + Hint);
        }

        private class HintInterceptor : DbCommandInterceptor
        {
            public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
            {
                command.CommandText = Apply(command.CommandText);
                return result;
            }

            public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
                InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
            {
                command.CommandText = Apply(command.CommandText);
                return ValueTask.FromResult(result);
            }

            public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
            {
                command.CommandText = Apply(command.CommandText);
                return result;
            }

            public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData,
                InterceptionResult<object> result, CancellationToken cancellationToken = default)
            {
                command.CommandText = Apply(command.CommandText);
                return ValueTask.FromResult(result);
            }
        }
    }
}
