using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LupexWallet.Budgeting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "budgeting");

            migrationBuilder.CreateTable(
                name: "budgets",
                schema: "budgeting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    month = table.Column<DateOnly>(type: "date", nullable: false),
                    currency_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_budgets", x => x.id);
                    table.CheckConstraint("ck_budgets_month_first_day", "EXTRACT(DAY FROM month) = 1");
                });

            migrationBuilder.CreateTable(
                name: "expense_categories",
                schema: "budgeting",
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
                    table.PrimaryKey("pk_expense_categories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "budget_category_limits",
                schema: "budgeting",
                columns: table => new
                {
                    expense_category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    budget_id = table.Column<Guid>(type: "uuid", nullable: false),
                    limit_amount = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_budget_category_limits", x => new { x.budget_id, x.expense_category_id });
                    table.CheckConstraint("ck_budget_category_limits_limit_non_negative", "limit_amount >= 0");
                    table.ForeignKey(
                        name: "fk_budget_category_limits_budget",
                        column: x => x.budget_id,
                        principalSchema: "budgeting",
                        principalTable: "budgets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_budget_category_limits_expense_category",
                        column: x => x.expense_category_id,
                        principalSchema: "budgeting",
                        principalTable: "expense_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "expense_category_operation_types",
                schema: "budgeting",
                columns: table => new
                {
                    operation_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expense_category_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expense_category_operation_types", x => new { x.expense_category_id, x.operation_type_id });
                    table.ForeignKey(
                        name: "fk_expense_category_operation_types_category",
                        column: x => x.expense_category_id,
                        principalSchema: "budgeting",
                        principalTable: "expense_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_budget_category_limits_category",
                schema: "budgeting",
                table: "budget_category_limits",
                column: "expense_category_id");

            migrationBuilder.CreateIndex(
                name: "ix_budgets_currency",
                schema: "budgeting",
                table: "budgets",
                column: "currency_id");

            migrationBuilder.CreateIndex(
                name: "ux_budgets_month_currency",
                schema: "budgeting",
                table: "budgets",
                columns: new[] { "month", "currency_id" },
                unique: true);

            // schema.md: unique among active categories, case-insensitive (as reference_data dictionaries).
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX ux_expense_categories_active_name ON budgeting.expense_categories (lower(name)) WHERE is_active;");

            migrationBuilder.CreateIndex(
                name: "ux_expense_category_operation_types_operation_type",
                schema: "budgeting",
                table: "expense_category_operation_types",
                column: "operation_type_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "budget_category_limits",
                schema: "budgeting");

            migrationBuilder.DropTable(
                name: "expense_category_operation_types",
                schema: "budgeting");

            migrationBuilder.DropTable(
                name: "budgets",
                schema: "budgeting");

            migrationBuilder.DropTable(
                name: "expense_categories",
                schema: "budgeting");
        }
    }
}
