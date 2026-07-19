using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThisCafeteria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRawStakingQuantities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "RawAssetAmount", table: "StakingLedgerEntries", type: "character varying(80)", maxLength: 80, nullable: false, defaultValue: "0");
            migrationBuilder.AddColumn<string>(name: "RawShareAmount", table: "StakingLedgerEntries", type: "character varying(80)", maxLength: 80, nullable: false, defaultValue: "0");
            migrationBuilder.AddColumn<string>(name: "RawRewardAmount", table: "StakingLedgerEntries", type: "character varying(80)", maxLength: 80, nullable: false, defaultValue: "0");
            migrationBuilder.Sql("UPDATE \"StakingLedgerEntries\" SET \"RawAssetAmount\" = trunc(\"AssetAmount\" * 1000000000000000000)::numeric::text, \"RawShareAmount\" = trunc(\"ShareAmount\" * 1000000000000000000)::numeric::text, \"RawRewardAmount\" = trunc(\"RewardAmount\" * 1000000000000000000)::numeric::text;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "RawAssetAmount", table: "StakingLedgerEntries");
            migrationBuilder.DropColumn(name: "RawShareAmount", table: "StakingLedgerEntries");
            migrationBuilder.DropColumn(name: "RawRewardAmount", table: "StakingLedgerEntries");
        }
    }
}
