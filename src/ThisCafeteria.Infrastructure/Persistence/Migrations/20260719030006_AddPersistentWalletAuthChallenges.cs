using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThisCafeteria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPersistentWalletAuthChallenges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WalletAuthChallenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NonceHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PublicKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ChainKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Origin = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    MessageHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IssuedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsumedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    VerificationAttempts = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletAuthChallenges", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WalletAuthChallenges_ExpiresAtUtc_ConsumedAtUtc",
                table: "WalletAuthChallenges",
                columns: new[] { "ExpiresAtUtc", "ConsumedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WalletAuthChallenges_NonceHash",
                table: "WalletAuthChallenges",
                column: "NonceHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WalletAuthChallenges_PublicKey_ChainKey",
                table: "WalletAuthChallenges",
                columns: new[] { "PublicKey", "ChainKey" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WalletAuthChallenges");
        }
    }
}
