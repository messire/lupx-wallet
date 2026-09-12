using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LupexWallet.Audit.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.CreateTable(
                name: "audit_entries",
                schema: "audit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    actor_system_process = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    changes = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_entries", x => x.id);
                    table.CheckConstraint("ck_audit_entries_actor_kind", "actor_kind IN ('User', 'System')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_entity",
                schema: "audit",
                table: "audit_entries",
                columns: new[] { "entity_type", "entity_id", "occurred_at" },
                descending: new[] { false, false, true });

            // Неизменяемость журнала (schema.md, раздел "Схема audit"; ddd-model.md §5/6) —
            // защита на уровне БД в дополнение к слою приложения (AuditEntry не имеет методов
            // изменения/удаления). EF Core не умеет генерировать CREATE RULE — добавлено вручную.
            migrationBuilder.Sql(
                "CREATE RULE audit_entries_no_update AS ON UPDATE TO audit.audit_entries DO INSTEAD NOTHING;");
            migrationBuilder.Sql(
                "CREATE RULE audit_entries_no_delete AS ON DELETE TO audit.audit_entries DO INSTEAD NOTHING;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP RULE IF EXISTS audit_entries_no_delete ON audit.audit_entries;");
            migrationBuilder.Sql("DROP RULE IF EXISTS audit_entries_no_update ON audit.audit_entries;");

            migrationBuilder.DropTable(
                name: "audit_entries",
                schema: "audit");
        }
    }
}
