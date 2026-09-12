using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LupexWallet.Operations.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "operations");

            migrationBuilder.CreateTable(
                name: "operations",
                schema: "operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    wallet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_date = table.Column<DateOnly>(type: "date", nullable: false),
                    adjustment_mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    transfer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    amount = table.Column<decimal>(type: "numeric", nullable: false),
                    applied_delta_amount = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_operations", x => x.id);
                    table.CheckConstraint("ck_operations_adjustment_mode", "adjustment_mode IN ('Absolute', 'Delta') OR adjustment_mode IS NULL");
                });

            migrationBuilder.CreateTable(
                name: "transfers",
                schema: "operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_wallet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_wallet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transfer_date = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    amount = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transfers", x => x.id);
                    table.CheckConstraint("ck_transfers_amount_positive", "amount > 0");
                    table.CheckConstraint("ck_transfers_distinct_wallets", "source_wallet_id <> target_wallet_id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_operations_operation_type",
                schema: "operations",
                table: "operations",
                column: "operation_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_operations_transfer",
                schema: "operations",
                table: "operations",
                column: "transfer_id",
                filter: "transfer_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_operations_wallet_date",
                schema: "operations",
                table: "operations",
                columns: new[] { "wallet_id", "operation_date" });

            migrationBuilder.CreateIndex(
                name: "ix_transfers_source_wallet",
                schema: "operations",
                table: "transfers",
                column: "source_wallet_id");

            migrationBuilder.CreateIndex(
                name: "ix_transfers_target_wallet",
                schema: "operations",
                table: "transfers",
                column: "target_wallet_id");

            // Циклическая зависимость operations.transfer_id <-> transfers.source/target_operation_id:
            // Transfer и его 2 Operation создаются в одной транзакции, ссылаясь друг на друга
            // (schema.md отмечает это явно). Вместо порядка "вставить с NULL -> обновить" — все три
            // ограничения DEFERRABLE INITIALLY DEFERRED, проверяются только при COMMIT, поэтому
            // порядок вставки внутри транзакции не имеет значения (см. ADR примечание в
            // OperationConfiguration.cs/TransferConfiguration.cs — эти FK намеренно не описаны
            // через EF Fluent API, чтобы не бороться с автоматическим упорядочиванием SaveChanges).
            migrationBuilder.Sql(
                "ALTER TABLE operations.operations " +
                "ADD CONSTRAINT fk_operations_transfer FOREIGN KEY (transfer_id) REFERENCES operations.transfers (id) " +
                "DEFERRABLE INITIALLY DEFERRED;");

            migrationBuilder.Sql(
                "ALTER TABLE operations.transfers " +
                "ADD CONSTRAINT fk_transfers_source_operation FOREIGN KEY (source_operation_id) REFERENCES operations.operations (id) " +
                "DEFERRABLE INITIALLY DEFERRED, " +
                "ADD CONSTRAINT fk_transfers_target_operation FOREIGN KEY (target_operation_id) REFERENCES operations.operations (id) " +
                "DEFERRABLE INITIALLY DEFERRED, " +
                "ADD CONSTRAINT uq_transfers_source_operation UNIQUE (source_operation_id), " +
                "ADD CONSTRAINT uq_transfers_target_operation UNIQUE (target_operation_id);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // transfers сначала — ссылается на operations через fk_transfers_source/target_operation
            // (добавлено raw SQL в Up, EF не знает об этой зависимости для авто-упорядочивания Down).
            migrationBuilder.DropTable(
                name: "transfers",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "operations",
                schema: "operations");
        }
    }
}
