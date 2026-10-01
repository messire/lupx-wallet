using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LupexWallet.Operations.Infrastructure.Migrations
{
    /// <summary>
    /// Balance corrections (ADR-0003, §6): adds is_correction / stated_balance_amount, makes
    /// operation_type_id nullable, adds the CHECK constraints and the unique partial index, and drops
    /// the 'Absolute' adjustment mode.
    ///
    /// Existing 'Absolute' operations are converted in Up (plain UPDATEs keyed on
    /// adjustment_mode = 'Absolute', so re-running them changes nothing):
    ///  1. For each wallet and date, the latest non-deleted 'Absolute' row (created_at, id) becomes a
    ///     correction: stated_balance_amount = amount (the value the user entered), amount =
    ///     applied_delta_amount (its previous effect, until the reconciliation pass at start-up
    ///     recalculates it against the balance at the end of that day), operation_type_id and
    ///     adjustment_mode = NULL.
    ///  2. Every other 'Absolute' row (earlier duplicates on a date, soft-deleted rows) becomes a
    ///     'Delta' operation with amount = applied_delta_amount: its effect on the balance is
    ///     preserved exactly, and the stated balance of that date absorbs it.
    /// What is lost: the operation type of converted rows, and the entered value of duplicates and
    /// deleted rows (still present in the audit trail).
    ///
    /// Down fails while any correction exists (data would be lost); otherwise it only restores the
    /// schema and does not turn 'Delta' rows back into 'Absolute'.
    /// </summary>
    public partial class AddStatedBalanceCorrections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_operations_adjustment_mode",
                schema: "operations",
                table: "operations");

            migrationBuilder.AlterColumn<Guid>(
                name: "operation_type_id",
                schema: "operations",
                table: "operations",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<bool>(
                name: "is_correction",
                schema: "operations",
                table: "operations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "stated_balance_amount",
                schema: "operations",
                table: "operations",
                type: "numeric",
                nullable: true);

            // Data conversion goes before the unique index and the new CHECKs: they validate its result.
            migrationBuilder.Sql(@"
WITH latest AS (
    SELECT DISTINCT ON (wallet_id, operation_date) id
    FROM operations.operations
    WHERE adjustment_mode = 'Absolute' AND deleted_at IS NULL
    ORDER BY wallet_id, operation_date, created_at DESC, id DESC
)
UPDATE operations.operations AS o
SET stated_balance_amount = o.amount,
    amount = o.applied_delta_amount,
    operation_type_id = NULL,
    adjustment_mode = NULL,
    is_correction = true
FROM latest
WHERE o.id = latest.id;");

            migrationBuilder.Sql(@"
UPDATE operations.operations
SET adjustment_mode = 'Delta',
    amount = applied_delta_amount
WHERE adjustment_mode = 'Absolute';");

            migrationBuilder.CreateIndex(
                name: "ux_operations_wallet_correction_date",
                schema: "operations",
                table: "operations",
                columns: new[] { "wallet_id", "operation_date" },
                unique: true,
                filter: "is_correction");

            migrationBuilder.AddCheckConstraint(
                name: "ck_operations_adjustment_mode",
                schema: "operations",
                table: "operations",
                sql: "adjustment_mode IN ('Delta') OR adjustment_mode IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_operations_correction_shape",
                schema: "operations",
                table: "operations",
                sql: "NOT is_correction OR (operation_type_id IS NULL AND adjustment_mode IS NULL AND transfer_id IS NULL AND deleted_at IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_operations_correction_stated_balance",
                schema: "operations",
                table: "operations",
                sql: "is_correction = (stated_balance_amount IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_operations_regular_type",
                schema: "operations",
                table: "operations",
                sql: "is_correction OR operation_type_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Guard: dropping the columns would silently lose every stated balance.
            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM operations.operations WHERE is_correction) THEN
        RAISE EXCEPTION 'operations.operations contains balance corrections (is_correction = true); the migration cannot be rolled back without losing stated balances';
    END IF;
END
$$;");

            migrationBuilder.DropIndex(
                name: "ux_operations_wallet_correction_date",
                schema: "operations",
                table: "operations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_operations_adjustment_mode",
                schema: "operations",
                table: "operations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_operations_correction_shape",
                schema: "operations",
                table: "operations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_operations_correction_stated_balance",
                schema: "operations",
                table: "operations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_operations_regular_type",
                schema: "operations",
                table: "operations");

            migrationBuilder.DropColumn(
                name: "is_correction",
                schema: "operations",
                table: "operations");

            migrationBuilder.DropColumn(
                name: "stated_balance_amount",
                schema: "operations",
                table: "operations");

            migrationBuilder.AlterColumn<Guid>(
                name: "operation_type_id",
                schema: "operations",
                table: "operations",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_operations_adjustment_mode",
                schema: "operations",
                table: "operations",
                sql: "adjustment_mode IN ('Absolute', 'Delta') OR adjustment_mode IS NULL");
        }
    }
}
