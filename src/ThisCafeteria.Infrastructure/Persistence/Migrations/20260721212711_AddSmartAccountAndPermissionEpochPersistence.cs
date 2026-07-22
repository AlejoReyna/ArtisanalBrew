using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThisCafeteria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSmartAccountAndPermissionEpochPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgentPermissionEpochs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChainKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SmartAccountRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    DelegatorAddress = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    OwnerAddress = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AgentAddress = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Epoch = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ValidAfterUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ValidBeforeUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    InstalledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    InstalledTxHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedTxHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentPermissionEpochs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SmartAccountRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChainKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OwnerAddress = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AccountType = table.Column<int>(type: "integer", nullable: false),
                    AccountAddress = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Salt = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    FactoryAddress = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IsDeployed = table.Column<bool>(type: "boolean", nullable: false),
                    ImplementationVerified = table.Column<bool>(type: "boolean", nullable: false),
                    DiscoveredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeployedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmartAccountRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AgentPermissionGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EpochId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetAddress = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Selector = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    TokenAddress = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    AmountWei = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    DelegationHash = table.Column<string>(type: "character varying(66)", maxLength: 66, nullable: false),
                    Description = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentPermissionGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentPermissionGrants_AgentPermissionEpochs_EpochId",
                        column: x => x.EpochId,
                        principalTable: "AgentPermissionEpochs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentPermissionEpochs_AccountEpoch",
                table: "AgentPermissionEpochs",
                columns: new[] { "ChainKey", "SmartAccountRecordId", "Epoch" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentPermissionEpochs_ChainDelegator",
                table: "AgentPermissionEpochs",
                columns: new[] { "ChainKey", "DelegatorAddress" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentPermissionGrants_DelegationHash",
                table: "AgentPermissionGrants",
                column: "DelegationHash");

            migrationBuilder.CreateIndex(
                name: "IX_AgentPermissionGrants_Epoch",
                table: "AgentPermissionGrants",
                column: "EpochId");

            migrationBuilder.CreateIndex(
                name: "IX_SmartAccountRecords_ChainAddress",
                table: "SmartAccountRecords",
                columns: new[] { "ChainKey", "AccountAddress" });

            migrationBuilder.CreateIndex(
                name: "IX_SmartAccountRecords_ChainOwnerType",
                table: "SmartAccountRecords",
                columns: new[] { "ChainKey", "OwnerAddress", "AccountType" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentPermissionGrants");

            migrationBuilder.DropTable(
                name: "SmartAccountRecords");

            migrationBuilder.DropTable(
                name: "AgentPermissionEpochs");
        }
    }
}
