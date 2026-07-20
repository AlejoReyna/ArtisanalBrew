using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThisCafeteria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgenticCommerceReconciliationPhase3Idempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgenticJobAppliedEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChainKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContractAddress = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TransactionHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    LogIndex = table.Column<int>(type: "integer", nullable: false),
                    AppliedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgenticJobAppliedEvents", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgenticJobAppliedEvents");
        }
    }
}
