using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThisCafeteria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMultichainLiquidStakingPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StakingReconciliationCheckpoints_StakingPoolContract",
                table: "StakingReconciliationCheckpoints");

            migrationBuilder.DropIndex(
                name: "IX_StakingLedgerEntries_TransactionHash",
                table: "StakingLedgerEntries");

            migrationBuilder.AlterColumn<string>(
                name: "StakingPoolContract",
                table: "StakingReconciliationCheckpoints",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(42)",
                oldMaxLength: 42);

            migrationBuilder.AddColumn<string>(
                name: "ChainKey",
                table: "StakingReconciliationCheckpoints",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CursorType",
                table: "StakingReconciliationCheckpoints",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Family",
                table: "StakingReconciliationCheckpoints",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "LastScannedSignature",
                table: "StakingReconciliationCheckpoints",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "LastScannedSlot",
                table: "StakingReconciliationCheckpoints",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "SourceIdentifier",
                table: "StakingReconciliationCheckpoints",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<string>(
                name: "WalletAddress",
                table: "StakingLedgerEntries",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(42)",
                oldMaxLength: 42);

            migrationBuilder.AlterColumn<string>(
                name: "TransactionHash",
                table: "StakingLedgerEntries",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(66)",
                oldMaxLength: 66);

            migrationBuilder.AlterColumn<string>(
                name: "StakingPoolContract",
                table: "StakingLedgerEntries",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(42)",
                oldMaxLength: 42);

            migrationBuilder.AlterColumn<string>(
                name: "PaymentTokenContract",
                table: "StakingLedgerEntries",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(42)",
                oldMaxLength: 42);

            migrationBuilder.AlterColumn<string>(
                name: "ActionType",
                table: "StakingLedgerEntries",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16);

            migrationBuilder.AddColumn<decimal>(
                name: "AssetAmount",
                table: "StakingLedgerEntries",
                type: "numeric(36,18)",
                precision: 36,
                scale: 18,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "AssetIdentifier",
                table: "StakingLedgerEntries",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "BlockOrSlot",
                table: "StakingLedgerEntries",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "ChainKey",
                table: "StakingLedgerEntries",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Family",
                table: "StakingLedgerEntries",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "OccurredAtUtc",
                table: "StakingLedgerEntries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OperationIndex",
                table: "StakingLedgerEntries",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ReceiptIdentifier",
                table: "StakingLedgerEntries",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "RewardAmount",
                table: "StakingLedgerEntries",
                type: "numeric(36,18)",
                precision: 36,
                scale: 18,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "RewardIdentifier",
                table: "StakingLedgerEntries",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "ShareAmount",
                table: "StakingLedgerEntries",
                type: "numeric(36,18)",
                precision: 36,
                scale: 18,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "VaultOrProgramIdentifier",
                table: "StakingLedgerEntries",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "VerificationState",
                table: "StakingLedgerEntries",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "Verified",
                table: "StakingLedgerEntries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<string>(
                name: "WalletAddress",
                table: "AspNetUsers",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(42)",
                oldMaxLength: 42,
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "WalletIdentities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Family = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    NormalizedAddress = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DisplayAddress = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    WalletProvider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    VerifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletIdentities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WalletIdentities_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql("UPDATE \"StakingLedgerEntries\" SET \"ChainKey\" = 'ethereum-sepolia', \"Family\" = 'Evm', \"VerificationState\" = 'legacy', \"Verified\" = true WHERE \"ChainKey\" = '';");
            }
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql("UPDATE \"StakingReconciliationCheckpoints\" SET \"ChainKey\" = 'ethereum-sepolia', \"Family\" = 'Evm', \"SourceIdentifier\" = \"StakingPoolContract\", \"CursorType\" = 'block' WHERE \"ChainKey\" = '';");
            }
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql(@"
                    INSERT INTO ""WalletIdentities""
                        (""Id"", ""UserId"", ""Family"", ""NormalizedAddress"", ""DisplayAddress"", ""WalletProvider"", ""VerifiedAtUtc"")
                    SELECT DISTINCT ON (lower(u.""WalletAddress"")) md5(u.""Id""::text || ':wallet')::uuid, u.""Id"", 'Evm', lower(u.""WalletAddress""), u.""WalletAddress"", 'legacy', COALESCE(u.""WalletVerifiedAt"", now())
                    FROM ""AspNetUsers"" u
                    WHERE u.""WalletAddress"" IS NOT NULL
                      AND u.""WalletAddress"" <> ''
                      AND NOT EXISTS (
                          SELECT 1 FROM ""WalletIdentities"" wi
                          WHERE wi.""Family"" = 'Evm' AND wi.""NormalizedAddress"" = lower(u.""WalletAddress"")
                      )
                    ORDER BY lower(u.""WalletAddress""), u.""WalletVerifiedAt"" DESC NULLS LAST, u.""Id"";");
            }

            migrationBuilder.CreateIndex(
                name: "IX_StakingReconciliationCheckpoints_ChainKey_SourceIdentifier",
                table: "StakingReconciliationCheckpoints",
                columns: new[] { "ChainKey", "SourceIdentifier" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StakingLedgerEntries_ChainKey_TransactionHash_OperationIndex",
                table: "StakingLedgerEntries",
                columns: new[] { "ChainKey", "TransactionHash", "OperationIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WalletIdentities_Family_NormalizedAddress",
                table: "WalletIdentities",
                columns: new[] { "Family", "NormalizedAddress" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WalletIdentities_UserId",
                table: "WalletIdentities",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WalletIdentities");

            migrationBuilder.DropIndex(
                name: "IX_StakingReconciliationCheckpoints_ChainKey_SourceIdentifier",
                table: "StakingReconciliationCheckpoints");

            migrationBuilder.DropIndex(
                name: "IX_StakingLedgerEntries_ChainKey_TransactionHash_OperationIndex",
                table: "StakingLedgerEntries");

            migrationBuilder.DropColumn(
                name: "ChainKey",
                table: "StakingReconciliationCheckpoints");

            migrationBuilder.DropColumn(
                name: "CursorType",
                table: "StakingReconciliationCheckpoints");

            migrationBuilder.DropColumn(
                name: "Family",
                table: "StakingReconciliationCheckpoints");

            migrationBuilder.DropColumn(
                name: "LastScannedSignature",
                table: "StakingReconciliationCheckpoints");

            migrationBuilder.DropColumn(
                name: "LastScannedSlot",
                table: "StakingReconciliationCheckpoints");

            migrationBuilder.DropColumn(
                name: "SourceIdentifier",
                table: "StakingReconciliationCheckpoints");

            migrationBuilder.DropColumn(
                name: "AssetAmount",
                table: "StakingLedgerEntries");

            migrationBuilder.DropColumn(
                name: "AssetIdentifier",
                table: "StakingLedgerEntries");

            migrationBuilder.DropColumn(
                name: "BlockOrSlot",
                table: "StakingLedgerEntries");

            migrationBuilder.DropColumn(
                name: "ChainKey",
                table: "StakingLedgerEntries");

            migrationBuilder.DropColumn(
                name: "Family",
                table: "StakingLedgerEntries");

            migrationBuilder.DropColumn(
                name: "OccurredAtUtc",
                table: "StakingLedgerEntries");

            migrationBuilder.DropColumn(
                name: "OperationIndex",
                table: "StakingLedgerEntries");

            migrationBuilder.DropColumn(
                name: "ReceiptIdentifier",
                table: "StakingLedgerEntries");

            migrationBuilder.DropColumn(
                name: "RewardAmount",
                table: "StakingLedgerEntries");

            migrationBuilder.DropColumn(
                name: "RewardIdentifier",
                table: "StakingLedgerEntries");

            migrationBuilder.DropColumn(
                name: "ShareAmount",
                table: "StakingLedgerEntries");

            migrationBuilder.DropColumn(
                name: "VaultOrProgramIdentifier",
                table: "StakingLedgerEntries");

            migrationBuilder.DropColumn(
                name: "VerificationState",
                table: "StakingLedgerEntries");

            migrationBuilder.DropColumn(
                name: "Verified",
                table: "StakingLedgerEntries");

            migrationBuilder.AlterColumn<string>(
                name: "StakingPoolContract",
                table: "StakingReconciliationCheckpoints",
                type: "character varying(42)",
                maxLength: 42,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "WalletAddress",
                table: "StakingLedgerEntries",
                type: "character varying(42)",
                maxLength: 42,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "TransactionHash",
                table: "StakingLedgerEntries",
                type: "character varying(66)",
                maxLength: 66,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256);

            migrationBuilder.AlterColumn<string>(
                name: "StakingPoolContract",
                table: "StakingLedgerEntries",
                type: "character varying(42)",
                maxLength: 42,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "PaymentTokenContract",
                table: "StakingLedgerEntries",
                type: "character varying(42)",
                maxLength: 42,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "ActionType",
                table: "StakingLedgerEntries",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string>(
                name: "WalletAddress",
                table: "AspNetUsers",
                type: "character varying(42)",
                maxLength: 42,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StakingReconciliationCheckpoints_StakingPoolContract",
                table: "StakingReconciliationCheckpoints",
                column: "StakingPoolContract",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StakingLedgerEntries_TransactionHash",
                table: "StakingLedgerEntries",
                column: "TransactionHash",
                unique: true);
        }
    }
}
