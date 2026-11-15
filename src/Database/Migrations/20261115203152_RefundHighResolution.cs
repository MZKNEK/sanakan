using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sanakan.Database.Migrations
{
    // The profile of twice the size is taken back: every player who bought it gets the recorded price back
    // in the currency paid (CurrencyType: TC = 0, SC = 4) and the unlock goes. Layouts keep "hd", which is ignored.
    /// <inheritdoc />
    public partial class RefundHighResolution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Refund(0, "TcCnt"));
            migrationBuilder.Sql(Refund(4, "ScCnt"));
            migrationBuilder.Sql("DELETE FROM `ProfileUnlocks` WHERE `Feature` = 'hd';");
        }

        private static string Refund(int currency, string column)
            => "UPDATE `Users` AS u JOIN (SELECT `UserId`, SUM(`Price`) AS `Paid` FROM `ProfileUnlocks` "
                + $"WHERE `Feature` = 'hd' AND `Currency` = {currency} AND `Price` > 0 GROUP BY `UserId`) AS r ON r.`UserId` = u.`Id` "
                + $"SET u.`{column}` = u.`{column}` + r.`Paid`;";

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
