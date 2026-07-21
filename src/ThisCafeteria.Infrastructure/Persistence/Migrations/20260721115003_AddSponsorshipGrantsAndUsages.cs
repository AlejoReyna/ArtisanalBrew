using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThisCafeteria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSponsorshipGrantsAndUsages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SponsorshipGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChainKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OwnerAddress = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    BudgetUsd = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    SpentUsd = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    MaxOperationCostUsd = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    ValidFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ValidUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyToken = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SponsorshipGrants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SponsorshipUsages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GrantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChainKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OwnerAddress = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CostUsd = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    TargetAddress = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Selector = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SponsorshipUsages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SponsorshipGrants_ChainOwner",
                table: "SponsorshipGrants",
                columns: new[] { "ChainKey", "OwnerAddress" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SponsorshipUsages_ChainOwner",
                table: "SponsorshipUsages",
                columns: new[] { "ChainKey", "OwnerAddress" });

            migrationBuilder.CreateIndex(
                name: "IX_SponsorshipUsages_Grant",
                table: "SponsorshipUsages",
                column: "GrantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SponsorshipGrants");

            migrationBuilder.DropTable(
                name: "SponsorshipUsages");
        }
    }
}
