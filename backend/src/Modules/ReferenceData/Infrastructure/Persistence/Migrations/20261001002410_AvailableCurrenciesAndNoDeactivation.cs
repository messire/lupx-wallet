using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LupexWallet.ReferenceData.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AvailableCurrenciesAndNoDeactivation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Guard: a deactivated currency no longer has a representation (ADR-0004 §7). Fail the
            // migration instead of silently turning such a row into an active currency.
            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM reference_data.currencies WHERE is_active = false) THEN
        RAISE EXCEPTION 'reference_data.currencies contains deactivated currencies (is_active = false); remove or reactivate them before applying this migration';
    END IF;
END
$$;");

            migrationBuilder.CreateTable(
                name: "available_currencies",
                schema: "reference_data",
                columns: table => new
                {
                    code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    refreshed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_available_currencies", x => x.code);
                });

            migrationBuilder.DropColumn(
                name: "is_active",
                schema: "reference_data",
                table: "currencies");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "available_currencies",
                schema: "reference_data");

            // The original column is NOT NULL without a default. Every existing row is active (Up
            // rejects deactivated ones), so the default only backfills them and is dropped right after.
            migrationBuilder.Sql(
                "ALTER TABLE reference_data.currencies ADD COLUMN is_active boolean NOT NULL DEFAULT true;");
            migrationBuilder.Sql(
                "ALTER TABLE reference_data.currencies ALTER COLUMN is_active DROP DEFAULT;");
        }
    }
}
