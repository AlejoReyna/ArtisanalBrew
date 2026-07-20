using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThisCafeteria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgenticCommerceReconciliationPhase3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ChainId",
                table: "AgenticJobs",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "CompletionTransactionHash",
                table: "AgenticJobs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "ConcurrencyToken",
                table: "AgenticJobs",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "ContractAddress",
                table: "AgenticJobs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CreationTransactionHash",
                table: "AgenticJobs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FundedTransactionHash",
                table: "AgenticJobs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "LastReconciledBlock",
                table: "AgenticJobs",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "OnChainJobId",
                table: "AgenticJobs",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "AgenticCommerceCheckpoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChainKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EscrowAddress = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    LastScannedBlock = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgenticCommerceCheckpoints", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgenticCommerceCheckpoints_ChainKey_EscrowAddress",
                table: "AgenticCommerceCheckpoints",
                columns: new[] { "ChainKey", "EscrowAddress" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgenticCommerceCheckpoints");

            migrationBuilder.DropColumn(
                name: "ChainId",
                table: "AgenticJobs");

            migrationBuilder.DropColumn(
                name: "CompletionTransactionHash",
                table: "AgenticJobs");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "AgenticJobs");

            migrationBuilder.DropColumn(
                name: "ContractAddress",
                table: "AgenticJobs");

            migrationBuilder.DropColumn(
                name: "CreationTransactionHash",
                table: "AgenticJobs");

            migrationBuilder.DropColumn(
                name: "FundedTransactionHash",
                table: "AgenticJobs");

            migrationBuilder.DropColumn(
                name: "LastReconciledBlock",
                table: "AgenticJobs");

            migrationBuilder.DropColumn(
                name: "OnChainJobId",
                table: "AgenticJobs");
        }
    }
}
