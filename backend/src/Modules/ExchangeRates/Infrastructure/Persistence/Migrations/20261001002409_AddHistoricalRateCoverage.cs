using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LupexWallet.ExchangeRates.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHistoricalRateCoverage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "historical_rate_coverage",
                schema: "exchange_rates",
                columns: table => new
                {
                    from_currency_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_currency_id = table.Column<Guid>(type: "uuid", nullable: false),
                    covered_from = table.Column<DateOnly>(type: "date", nullable: false),
                    covered_to = table.Column<DateOnly>(type: "date", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_historical_rate_coverage", x => new { x.from_currency_id, x.to_currency_id });
                    table.CheckConstraint("ck_historical_coverage_distinct_currencies", "from_currency_id <> to_currency_id");
                    table.CheckConstraint("ck_historical_coverage_range", "covered_from <= covered_to");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "historical_rate_coverage",
                schema: "exchange_rates");
        }
    }
}
