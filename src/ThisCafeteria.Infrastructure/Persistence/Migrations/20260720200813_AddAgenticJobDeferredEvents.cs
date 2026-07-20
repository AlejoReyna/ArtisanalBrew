using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThisCafeteria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgenticJobDeferredEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgenticJobDeferredEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChainKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContractAddress = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    OnChainJobId = table.Column<long>(type: "bigint", nullable: false),
                    EventType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TransactionHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    LogIndex = table.Column<int>(type: "integer", nullable: false),
                    BlockNumber = table.Column<long>(type: "bigint", nullable: false),
                    DeferralReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    DeferredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgenticJobDeferredEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgenticJobDeferredEvents_Job",
                table: "AgenticJobDeferredEvents",
                columns: new[] { "ChainKey", "ContractAddress", "OnChainJobId" });

            migrationBuilder.CreateIndex(
                name: "IX_AgenticJobDeferredEvents_LogIdentity",
                table: "AgenticJobDeferredEvents",
                columns: new[] { "ChainKey", "ContractAddress", "TransactionHash", "LogIndex" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgenticJobDeferredEvents");
        }
    }
}
