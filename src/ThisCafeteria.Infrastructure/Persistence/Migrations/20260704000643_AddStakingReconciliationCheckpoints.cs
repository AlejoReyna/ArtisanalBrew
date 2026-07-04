using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThisCafeteria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStakingReconciliationCheckpoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StakingReconciliationCheckpoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StakingPoolContract = table.Column<string>(type: "character varying(42)", maxLength: 42, nullable: false),
                    LastScannedBlock = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StakingReconciliationCheckpoints", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StakingReconciliationCheckpoints_StakingPoolContract",
                table: "StakingReconciliationCheckpoints",
                column: "StakingPoolContract",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StakingReconciliationCheckpoints");
        }
    }
}
