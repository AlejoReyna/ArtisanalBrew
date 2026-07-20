using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThisCafeteria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UpdateAgenticJobProjectionIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AgenticJobs_ChainKey_JobId",
                table: "AgenticJobs");

            migrationBuilder.CreateIndex(
                name: "IX_AgenticJobs_ChainKey_ContractAddress_OnChainJobId",
                table: "AgenticJobs",
                columns: new[] { "ChainKey", "ContractAddress", "OnChainJobId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AgenticJobs_ChainKey_ContractAddress_OnChainJobId",
                table: "AgenticJobs");

            migrationBuilder.CreateIndex(
                name: "IX_AgenticJobs_ChainKey_JobId",
                table: "AgenticJobs",
                columns: new[] { "ChainKey", "JobId" },
                unique: true);
        }
    }
}
