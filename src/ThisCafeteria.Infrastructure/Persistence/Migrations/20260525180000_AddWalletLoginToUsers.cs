using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThisCafeteria.Infrastructure.Persistence.Migrations
{
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(AppDbContext))]
    [Migration("20260525180000_AddWalletLoginToUsers")]
    public partial class AddWalletLoginToUsers : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WalletAddress",
                table: "AspNetUsers",
                type: "character varying(42)",
                maxLength: 42,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WalletChainId",
                table: "AspNetUsers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "WalletVerifiedAt",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_WalletAddress",
                table: "AspNetUsers",
                column: "WalletAddress",
                unique: true,
                filter: "\"WalletAddress\" IS NOT NULL");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_WalletAddress",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "WalletAddress",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "WalletChainId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "WalletVerifiedAt",
                table: "AspNetUsers");
        }
    }
}
