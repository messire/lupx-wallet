using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LupexWallet.Planning.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "planning");

            migrationBuilder.CreateTable(
                name: "planned_transactions",
                schema: "planning",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    direction = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    amount = table.Column<decimal>(type: "numeric", nullable: false),
                    currency_id = table.Column<Guid>(type: "uuid", nullable: false),
                    wallet_id = table.Column<Guid>(type: "uuid", nullable: true),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    recurrence_frequency = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    recurrence_interval = table.Column<int>(type: "integer", nullable: true),
                    recurrence_until = table.Column<DateOnly>(type: "date", nullable: true),
                    recurrence_count = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_planned_transactions", x => x.id);
                    table.CheckConstraint("ck_planned_transactions_amount_positive", "amount > 0");
                    table.CheckConstraint("ck_planned_transactions_recurrence_complete", "(recurrence_frequency IS NULL) = (recurrence_interval IS NULL)");
                    table.CheckConstraint("ck_planned_transactions_recurrence_end", "(recurrence_until IS NULL OR recurrence_count IS NULL) AND (recurrence_frequency IS NOT NULL OR (recurrence_until IS NULL AND recurrence_count IS NULL))");
                    table.CheckConstraint("ck_planned_transactions_recurrence_numbers", "(recurrence_interval IS NULL OR recurrence_interval >= 1) AND (recurrence_count IS NULL OR recurrence_count >= 1)");
                });

            migrationBuilder.CreateTable(
                name: "occurrence_executions",
                schema: "planning",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurrence_date = table.Column<DateOnly>(type: "date", nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric", nullable: false),
                    executed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    planned_transaction_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_occurrence_executions", x => x.id);
                    table.CheckConstraint("ck_occurrence_executions_amount_positive", "amount > 0");
                    table.ForeignKey(
                        name: "fk_occurrence_executions_planned_transactions_planned_transact",
                        column: x => x.planned_transaction_id,
                        principalSchema: "planning",
                        principalTable: "planned_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "occurrence_overrides",
                schema: "planning",
                columns: table => new
                {
                    occurrence_date = table.Column<DateOnly>(type: "date", nullable: false),
                    planned_transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_skipped = table.Column<bool>(type: "boolean", nullable: false),
                    moved_to_date = table.Column<DateOnly>(type: "date", nullable: true),
                    amount = table.Column<decimal>(type: "numeric", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_occurrence_overrides", x => new { x.planned_transaction_id, x.occurrence_date });
                    table.CheckConstraint("ck_occurrence_overrides_amount_positive", "amount IS NULL OR amount > 0");
                    table.ForeignKey(
                        name: "fk_occurrence_overrides_planned_transactions_planned_transacti",
                        column: x => x.planned_transaction_id,
                        principalSchema: "planning",
                        principalTable: "planned_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_occurrence_executions_occurrence",
                schema: "planning",
                table: "occurrence_executions",
                columns: new[] { "planned_transaction_id", "occurrence_date" });

            migrationBuilder.CreateIndex(
                name: "ux_occurrence_executions_operation",
                schema: "planning",
                table: "occurrence_executions",
                column: "operation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_planned_transactions_created",
                schema: "planning",
                table: "planned_transactions",
                columns: new[] { "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_planned_transactions_currency",
                schema: "planning",
                table: "planned_transactions",
                column: "currency_id");

            migrationBuilder.CreateIndex(
                name: "ix_planned_transactions_operation_type",
                schema: "planning",
                table: "planned_transactions",
                column: "operation_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_planned_transactions_wallet",
                schema: "planning",
                table: "planned_transactions",
                column: "wallet_id",
                filter: "wallet_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "occurrence_executions",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "occurrence_overrides",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "planned_transactions",
                schema: "planning");
        }
    }
}
