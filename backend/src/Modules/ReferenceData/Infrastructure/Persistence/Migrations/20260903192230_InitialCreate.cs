using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace LupexWallet.ReferenceData.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "reference_data");

            migrationBuilder.CreateTable(
                name: "currencies",
                schema: "reference_data",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_currencies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "operation_behavior_kinds",
                schema: "reference_data",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_operation_behavior_kinds", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "wallet_types",
                schema: "reference_data",
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
                    table.PrimaryKey("pk_wallet_types", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "operation_types",
                schema: "reference_data",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    behavior_kind_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_operation_types", x => x.id);
                    table.ForeignKey(
                        name: "fk_operation_types_operation_behavior_kinds_behavior_kind_id",
                        column: x => x.behavior_kind_id,
                        principalSchema: "reference_data",
                        principalTable: "operation_behavior_kinds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "reference_data",
                table: "operation_behavior_kinds",
                columns: new[] { "id", "code", "name" },
                values: new object[,]
                {
                    { new Guid("107ca9ff-3b38-4a58-85d7-236703f49a46"), "Transfer", "Перевод" },
                    { new Guid("24064f7c-82b3-4407-a1bb-b3706c0643c1"), "Income", "Доход" },
                    { new Guid("322fb879-0c8e-464e-bca1-9e84964ad232"), "Adjustment", "Ручная корректировка" },
                    { new Guid("d042ac13-0644-4ded-8fdd-518e6035b107"), "Expense", "Расход" }
                });

            // schema.md: код валюты уникален без учета регистра (UPPER) — домен уже
            // нормализует Code в верхний регистр при создании (Currency.Create), но
            // индекс дублирует это на уровне БД как defense-in-depth.
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX ux_currencies_code ON reference_data.currencies (upper(code));");

            migrationBuilder.CreateIndex(
                name: "ix_operation_behavior_kinds_code",
                schema: "reference_data",
                table: "operation_behavior_kinds",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_operation_types_behavior_kind",
                schema: "reference_data",
                table: "operation_types",
                column: "behavior_kind_id");

            // schema.md: уникальность имени среди активных без учета регистра (LOWER) —
            // в домене имя не нормализуется по регистру, поэтому это реальное, а не
            // дублирующее ограничение.
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX ux_operation_types_active_name ON reference_data.operation_types (lower(name)) WHERE is_active;");

            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX ux_wallet_types_active_name ON reference_data.wallet_types (lower(name)) WHERE is_active;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "currencies",
                schema: "reference_data");

            migrationBuilder.DropTable(
                name: "operation_types",
                schema: "reference_data");

            migrationBuilder.DropTable(
                name: "wallet_types",
                schema: "reference_data");

            migrationBuilder.DropTable(
                name: "operation_behavior_kinds",
                schema: "reference_data");
        }
    }
}
