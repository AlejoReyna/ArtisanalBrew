using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThisCafeteria.Infrastructure.Persistence.Migrations
{
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(AppDbContext))]
    [Migration("20260526120000_AddOrderPaymentMetadata")]
    public partial class AddOrderPaymentMetadata : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WalletAddress",
                table: "Orders",
                type: "character varying(42)",
                maxLength: 42,
                nullable: false,
                defaultValue: string.Empty);

            migrationBuilder.AddColumn<string>(
                name: "PaymentTransactionHash",
                table: "Orders",
                type: "character varying(66)",
                maxLength: 66,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PaymentChainId",
                table: "Orders",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentNetworkName",
                table: "Orders",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PaymentEthAmount",
                table: "Orders",
                type: "numeric(28,18)",
                precision: 28,
                scale: 18,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentExplorerUrl",
                table: "Orders",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PaidAtUtc",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_WalletAddress",
                table: "Orders",
                column: "WalletAddress");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_PaymentTransactionHash",
                table: "Orders",
                column: "PaymentTransactionHash",
                unique: true,
                filter: "\"PaymentTransactionHash\" IS NOT NULL");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Orders_WalletAddress",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_PaymentTransactionHash",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "WalletAddress",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentTransactionHash",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentChainId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentNetworkName",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentEthAmount",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentExplorerUrl",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaidAtUtc",
                table: "Orders");
        }
    }
}
