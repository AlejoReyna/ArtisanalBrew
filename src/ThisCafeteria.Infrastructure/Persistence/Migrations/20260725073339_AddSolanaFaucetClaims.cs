using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThisCafeteria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSolanaFaucetClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SolanaFaucetClaims",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChainKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    WalletAddress = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    RawAmount = table.Column<long>(type: "bigint", nullable: false),
                    Signature = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ClaimedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SolanaFaucetClaims", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SolanaFaucetClaims_ChainKey_WalletAddress_ClaimedAtUtc",
                table: "SolanaFaucetClaims",
                columns: new[] { "ChainKey", "WalletAddress", "ClaimedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SolanaFaucetClaims");
        }
    }
}
