using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LupexWallet.Operations.Infrastructure.Migrations
{
    /// <summary>
    /// Adds nullable deleted_at (soft delete, ADR-0002). Down makes soft-deleted rows count again while
    /// wallets.current_balance has already excluded them — roll back on test data only.
    /// </summary>
    public partial class AddRecordSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deleted_at",
                schema: "operations",
                table: "transfers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deleted_at",
                schema: "operations",
                table: "operations",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "deleted_at",
                schema: "operations",
                table: "transfers");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                schema: "operations",
                table: "operations");
        }
    }
}
