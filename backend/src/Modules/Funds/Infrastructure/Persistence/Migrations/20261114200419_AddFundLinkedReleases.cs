using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LupexWallet.Funds.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFundLinkedReleases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fund_linked_releases",
                schema: "funds",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    fund_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    wallet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fund_linked_releases", x => x.id);
                    table.CheckConstraint("ck_fund_linked_releases_amount_non_negative", "amount >= 0");
                    table.ForeignKey(
                        name: "fk_fund_linked_releases_funds_fund_id",
                        column: x => x.fund_id,
                        principalSchema: "funds",
                        principalTable: "funds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_fund_linked_releases_operation",
                schema: "funds",
                table: "fund_linked_releases",
                column: "operation_id");

            migrationBuilder.CreateIndex(
                name: "ux_fund_linked_releases_fund_operation_wallet",
                schema: "funds",
                table: "fund_linked_releases",
                columns: new[] { "fund_id", "operation_id", "wallet_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fund_linked_releases",
                schema: "funds");
        }
    }
}
