using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThisCafeteria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260527132000_AddStakingLedgerEntries")]
    public partial class AddStakingLedgerEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StakingLedgerEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WalletAddress = table.Column<string>(type: "character varying(42)", maxLength: 42, nullable: false),
                    ActionType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(36,18)", precision: 36, scale: 18, nullable: false),
                    TransactionHash = table.Column<string>(type: "character varying(66)", maxLength: 66, nullable: false),
                    ChainId = table.Column<int>(type: "integer", nullable: false),
                    NetworkName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    PaymentTokenContract = table.Column<string>(type: "character varying(42)", maxLength: 42, nullable: false),
                    StakingPoolContract = table.Column<string>(type: "character varying(42)", maxLength: 42, nullable: false),
                    ExplorerUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StakingLedgerEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StakingLedgerEntries_TransactionHash",
                table: "StakingLedgerEntries",
                column: "TransactionHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StakingLedgerEntries_WalletAddress_RecordedAtUtc",
                table: "StakingLedgerEntries",
                columns: new[] { "WalletAddress", "RecordedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StakingLedgerEntries");
        }
    }
}
