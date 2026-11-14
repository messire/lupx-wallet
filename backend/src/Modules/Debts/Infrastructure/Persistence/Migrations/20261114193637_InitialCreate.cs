using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LupexWallet.Debts.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "debts");

            migrationBuilder.CreateTable(
                name: "creditors",
                schema: "debts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_creditors", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "debts",
                schema: "debts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    creditor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    currency_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    original_amount = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_debts", x => x.id);
                    table.CheckConstraint("ck_debts_original_amount_positive", "original_amount > 0");
                    table.ForeignKey(
                        name: "fk_debts_creditor",
                        column: x => x.creditor_id,
                        principalSchema: "debts",
                        principalTable: "creditors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "loans",
                schema: "debts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    creditor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    currency_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    amount_to_repay = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_loans", x => x.id);
                    table.CheckConstraint("ck_loans_amount_to_repay_positive", "amount_to_repay > 0");
                    table.ForeignKey(
                        name: "fk_loans_creditor",
                        column: x => x.creditor_id,
                        principalSchema: "debts",
                        principalTable: "creditors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "debt_repayments",
                schema: "debts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    wallet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    repayment_date = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    debt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_debt_repayments", x => x.id);
                    table.CheckConstraint("ck_debt_repayments_amount_positive", "amount > 0");
                    table.ForeignKey(
                        name: "fk_debt_repayments_debt",
                        column: x => x.debt_id,
                        principalSchema: "debts",
                        principalTable: "debts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "loan_payments",
                schema: "debts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    wallet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_date = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    loan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_loan_payments", x => x.id);
                    table.CheckConstraint("ck_loan_payments_amount_positive", "amount > 0");
                    table.ForeignKey(
                        name: "fk_loan_payments_loan",
                        column: x => x.loan_id,
                        principalSchema: "debts",
                        principalTable: "loans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "loan_schedule_items",
                schema: "debts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    loan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_loan_schedule_items", x => x.id);
                    table.CheckConstraint("ck_loan_schedule_items_amount_positive", "amount > 0");
                    table.ForeignKey(
                        name: "fk_loan_schedule_items_loan",
                        column: x => x.loan_id,
                        principalSchema: "debts",
                        principalTable: "loans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_creditors_name",
                schema: "debts",
                table: "creditors",
                columns: new[] { "name", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_debt_repayments_debt",
                schema: "debts",
                table: "debt_repayments",
                column: "debt_id");

            migrationBuilder.CreateIndex(
                name: "ux_debt_repayments_operation",
                schema: "debts",
                table: "debt_repayments",
                column: "operation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_debts_created_at",
                schema: "debts",
                table: "debts",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_debts_creditor",
                schema: "debts",
                table: "debts",
                column: "creditor_id");

            migrationBuilder.CreateIndex(
                name: "ix_debts_currency",
                schema: "debts",
                table: "debts",
                column: "currency_id");

            migrationBuilder.CreateIndex(
                name: "ix_loan_payments_loan",
                schema: "debts",
                table: "loan_payments",
                column: "loan_id");

            migrationBuilder.CreateIndex(
                name: "ux_loan_payments_operation",
                schema: "debts",
                table: "loan_payments",
                column: "operation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_loan_schedule_items_loan_date",
                schema: "debts",
                table: "loan_schedule_items",
                columns: new[] { "loan_id", "due_date" });

            migrationBuilder.CreateIndex(
                name: "ix_loans_created_at",
                schema: "debts",
                table: "loans",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_loans_creditor",
                schema: "debts",
                table: "loans",
                column: "creditor_id");

            migrationBuilder.CreateIndex(
                name: "ix_loans_currency",
                schema: "debts",
                table: "loans",
                column: "currency_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "debt_repayments",
                schema: "debts");

            migrationBuilder.DropTable(
                name: "loan_payments",
                schema: "debts");

            migrationBuilder.DropTable(
                name: "loan_schedule_items",
                schema: "debts");

            migrationBuilder.DropTable(
                name: "debts",
                schema: "debts");

            migrationBuilder.DropTable(
                name: "loans",
                schema: "debts");

            migrationBuilder.DropTable(
                name: "creditors",
                schema: "debts");
        }
    }
}
