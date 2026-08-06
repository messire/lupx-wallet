using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LupexWallet.ExchangeRates.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "exchange_rates");

            migrationBuilder.CreateTable(
                name: "historical_exchange_rates",
                schema: "exchange_rates",
                columns: table => new
                {
                    from_currency_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_currency_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rate_date = table.Column<DateOnly>(type: "date", nullable: false),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    rate = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_historical_exchange_rates", x => new { x.from_currency_id, x.to_currency_id, x.rate_date });
                    table.CheckConstraint("ck_historical_rates_distinct_currencies", "from_currency_id <> to_currency_id");
                    table.CheckConstraint("ck_historical_rates_positive", "rate > 0");
                });

            migrationBuilder.CreateTable(
                name: "latest_exchange_rates",
                schema: "exchange_rates",
                columns: table => new
                {
                    from_currency_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_currency_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    rate = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_latest_exchange_rates", x => new { x.from_currency_id, x.to_currency_id });
                    table.CheckConstraint("ck_latest_rates_distinct_currencies", "from_currency_id <> to_currency_id");
                    table.CheckConstraint("ck_latest_rates_positive", "rate > 0");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "historical_exchange_rates",
                schema: "exchange_rates");

            migrationBuilder.DropTable(
                name: "latest_exchange_rates",
                schema: "exchange_rates");
        }
    }
}
