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

        // A text search over all cards sorted by rarity would walk the whole rarity index and read every row when
        // few cards match; scanning the table and sorting the matches keeps it as fast as without that index.
        public const string SearchTag = "card-search";

        public const string Hint = "/*+ SET_VAR(optimizer_switch='prefer_ordering_index=off') */";

        public const string SearchHint = "/*+ SET_VAR(optimizer_switch='prefer_ordering_index=off') NO_ORDER_INDEX(c "
            + DatabaseContext.CardsRarityIndex + ") */";

        public static readonly DbCommandInterceptor Interceptor = new HintInterceptor();

        public static string Apply(string sql)
        {
            // EF puts the tag comment first, newer versions after an empty line
            if (!sql.TrimStart().StartsWith("-- " + Tag, StringComparison.Ordinal))
                return sql;

            var select = sql.IndexOf("SELECT", StringComparison.Ordinal);
            if (select < 0)
                return sql;

            var search = sql.IndexOf("-- " + SearchTag, 0, select, StringComparison.Ordinal) >= 0;
            return sql.Insert(select + "SELECT".Length, " " + (search ? SearchHint : Hint));
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
