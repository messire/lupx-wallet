using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LupexWallet.Funds.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "funds");

            migrationBuilder.CreateTable(
                name: "funds",
                schema: "funds",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    target_amount = table.Column<decimal>(type: "numeric", nullable: false),
                    currency_id = table.Column<Guid>(type: "uuid", nullable: false),
                    linked_debt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    linked_loan_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_funds", x => x.id);
                    table.CheckConstraint("ck_funds_closed_at", "(status = 'Closed') = (closed_at IS NOT NULL)");
                    table.CheckConstraint("ck_funds_single_link", "linked_debt_id IS NULL OR linked_loan_id IS NULL");
                    table.CheckConstraint("ck_funds_status", "status IN ('Active', 'Closed')");
                    table.CheckConstraint("ck_funds_target_positive", "target_amount > 0");
                });

            migrationBuilder.CreateTable(
                name: "fund_allocations",
                schema: "funds",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    fund_id = table.Column<Guid>(type: "uuid", nullable: false),
                    wallet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fund_allocations", x => x.id);
                    table.CheckConstraint("ck_fund_allocations_amount_non_negative", "amount >= 0");
                    table.ForeignKey(
                        name: "fk_fund_allocations_funds_fund_id",
                        column: x => x.fund_id,
                        principalSchema: "funds",
                        principalTable: "funds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_fund_allocations_wallet",
                schema: "funds",
                table: "fund_allocations",
                column: "wallet_id");

            migrationBuilder.CreateIndex(
                name: "ux_fund_allocations_fund_wallet",
                schema: "funds",
                table: "fund_allocations",
                columns: new[] { "fund_id", "wallet_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_funds_created_at",
                schema: "funds",
                table: "funds",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_funds_currency",
                schema: "funds",
                table: "funds",
                column: "currency_id");

            migrationBuilder.CreateIndex(
                name: "ix_funds_linked_debt",
                schema: "funds",
                table: "funds",
                column: "linked_debt_id",
                filter: "linked_debt_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_funds_linked_loan",
                schema: "funds",
                table: "funds",
                column: "linked_loan_id",
                filter: "linked_loan_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fund_allocations",
                schema: "funds");

            migrationBuilder.DropTable(
                name: "funds",
                schema: "funds");
        }
    }
}
