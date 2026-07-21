using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThisCafeteria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCrossChainSolverCheckpointsAndFills : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CrossChainSolverCheckpoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceChainKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceResolverAddress = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    LastScannedBlock = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrossChainSolverCheckpoints", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CrossChainSolverFills",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceChainKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceResolverAddress = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    OrderId = table.Column<string>(type: "character varying(66)", maxLength: 66, nullable: false),
                    SubmitTransactionHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Filled = table.Column<bool>(type: "boolean", nullable: false),
                    FillTransactionHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    DenialReason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EvaluatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrossChainSolverFills", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CrossChainSolverCheckpoints_SourceChainResolver",
                table: "CrossChainSolverCheckpoints",
                columns: new[] { "SourceChainKey", "SourceResolverAddress" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CrossChainSolverFills_Identity",
                table: "CrossChainSolverFills",
                columns: new[] { "SourceChainKey", "SourceResolverAddress", "OrderId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CrossChainSolverCheckpoints");

            migrationBuilder.DropTable(
                name: "CrossChainSolverFills");
        }
    }
}
