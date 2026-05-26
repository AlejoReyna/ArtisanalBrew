using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThisCafeteria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRewardClaimAllocationMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AllocationName",
                table: "RewardClaims",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MarketplaceWallet",
                table: "RewardClaims",
                type: "character varying(42)",
                maxLength: 42,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MintExplorerUrl",
                table: "RewardClaims",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PaymentAmount",
                table: "RewardClaims",
                type: "numeric(36,18)",
                precision: 36,
                scale: 18,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PaymentChainId",
                table: "RewardClaims",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentExplorerUrl",
                table: "RewardClaims",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentNetworkName",
                table: "RewardClaims",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentTokenContract",
                table: "RewardClaims",
                type: "character varying(42)",
                maxLength: 42,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllocationName",
                table: "RewardClaims");

            migrationBuilder.DropColumn(
                name: "MarketplaceWallet",
                table: "RewardClaims");

            migrationBuilder.DropColumn(
                name: "MintExplorerUrl",
                table: "RewardClaims");

            migrationBuilder.DropColumn(
                name: "PaymentAmount",
                table: "RewardClaims");

            migrationBuilder.DropColumn(
                name: "PaymentChainId",
                table: "RewardClaims");

            migrationBuilder.DropColumn(
                name: "PaymentExplorerUrl",
                table: "RewardClaims");

            migrationBuilder.DropColumn(
                name: "PaymentNetworkName",
                table: "RewardClaims");

            migrationBuilder.DropColumn(
                name: "PaymentTokenContract",
                table: "RewardClaims");
        }
    }
}
