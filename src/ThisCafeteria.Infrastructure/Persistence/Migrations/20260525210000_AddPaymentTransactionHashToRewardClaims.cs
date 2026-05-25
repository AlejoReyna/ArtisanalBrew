using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThisCafeteria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentTransactionHashToRewardClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PaymentTransactionHash",
                table: "RewardClaims",
                type: "character varying(66)",
                maxLength: 66,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RewardClaims_PaymentTransactionHash",
                table: "RewardClaims",
                column: "PaymentTransactionHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RewardClaims_PaymentTransactionHash",
                table: "RewardClaims");

            migrationBuilder.DropColumn(
                name: "PaymentTransactionHash",
                table: "RewardClaims");
        }
    }
}
