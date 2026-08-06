using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LupexWallet.Wallets.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "wallets");

            migrationBuilder.CreateTable(
                name: "wallets",
                schema: "wallets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    wallet_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose_description = table.Column<string>(type: "text", nullable: true),
                    currency_id = table.Column<Guid>(type: "uuid", nullable: false),
                    accounting_start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    include_in_total = table.Column<bool>(type: "boolean", nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    color = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    icon = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    current_balance_amount = table.Column<decimal>(type: "numeric", nullable: false),
                    initial_balance_amount = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wallets", x => x.id);
                    table.CheckConstraint("ck_wallets_primary_included_in_total", "NOT is_primary OR include_in_total");
                    table.CheckConstraint("ck_wallets_primary_not_archived", "NOT (is_primary AND is_archived)");
                });

            migrationBuilder.CreateIndex(
                name: "ix_wallets_currency",
                schema: "wallets",
                table: "wallets",
                column: "currency_id");

            migrationBuilder.CreateIndex(
                name: "ix_wallets_display_order",
                schema: "wallets",
                table: "wallets",
                column: "display_order",
                filter: "NOT is_archived");

            migrationBuilder.CreateIndex(
                name: "ix_wallets_wallet_type",
                schema: "wallets",
                table: "wallets",
                column: "wallet_type_id");

            migrationBuilder.CreateIndex(
                name: "ux_wallets_single_primary",
                schema: "wallets",
                table: "wallets",
                column: "is_primary",
                unique: true,
                filter: "is_primary");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wallets",
                schema: "wallets");
        }
    }
}
