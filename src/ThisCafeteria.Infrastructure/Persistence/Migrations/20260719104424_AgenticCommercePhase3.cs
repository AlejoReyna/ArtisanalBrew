using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThisCafeteria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgenticCommercePhase3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgentDirectoryEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChainKey = table.Column<string>(type: "text", nullable: false),
                    RegistryAddress = table.Column<string>(type: "text", nullable: false),
                    AgentId = table.Column<long>(type: "bigint", nullable: false),
                    OwnerAddress = table.Column<string>(type: "text", nullable: false),
                    MetadataUri = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentDirectoryEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AgentFeedback",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChainKey = table.Column<string>(type: "text", nullable: false),
                    RegistryAddress = table.Column<string>(type: "text", nullable: false),
                    AgentId = table.Column<long>(type: "bigint", nullable: false),
                    JobId = table.Column<long>(type: "bigint", nullable: false),
                    ReviewerAddress = table.Column<string>(type: "text", nullable: false),
                    Score = table.Column<long>(type: "bigint", nullable: false),
                    CommentUri = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentFeedback", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AgenticJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChainKey = table.Column<string>(type: "text", nullable: false),
                    EscrowAddress = table.Column<string>(type: "text", nullable: false),
                    JobId = table.Column<long>(type: "bigint", nullable: false),
                    ClientAddress = table.Column<string>(type: "text", nullable: false),
                    ProviderAddress = table.Column<string>(type: "text", nullable: false),
                    EvaluatorAddress = table.Column<string>(type: "text", nullable: false),
                    DescriptionCommitment = table.Column<string>(type: "text", nullable: false),
                    Budget = table.Column<decimal>(type: "numeric", nullable: false),
                    ExpiredAt = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    DeliverableCommitment = table.Column<string>(type: "text", nullable: true),
                    DecisionReason = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgenticJobs", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentDirectoryEntries");

            migrationBuilder.DropTable(
                name: "AgentFeedback");

            migrationBuilder.DropTable(
                name: "AgenticJobs");
        }
    }
}
