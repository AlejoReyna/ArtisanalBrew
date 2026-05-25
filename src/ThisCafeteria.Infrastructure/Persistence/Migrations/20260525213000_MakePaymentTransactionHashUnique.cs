using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThisCafeteria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MakePaymentTransactionHashUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RewardClaims_PaymentTransactionHash",
                table: "RewardClaims");

            migrationBuilder.CreateIndex(
                name: "IX_RewardClaims_PaymentTransactionHash",
                table: "RewardClaims",
                column: "PaymentTransactionHash",
                unique: true,
                filter: "\"PaymentTransactionHash\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RewardClaims_PaymentTransactionHash",
                table: "RewardClaims");

            migrationBuilder.CreateIndex(
                name: "IX_RewardClaims_PaymentTransactionHash",
                table: "RewardClaims",
                column: "PaymentTransactionHash");
        }
    }
}
