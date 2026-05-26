using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThisCafeteria.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260526190000_RepairRewardClaimPaymentTransactionHash")]
    public partial class RepairRewardClaimPaymentTransactionHash : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name = 'RewardClaims'
                          AND column_name = 'PaymentTransactionHash'
                    ) THEN
                        ALTER TABLE "RewardClaims"
                        ADD COLUMN "PaymentTransactionHash" character varying(66);
                    END IF;
                END $$;
                """);

            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "IX_RewardClaims_PaymentTransactionHash";

                CREATE UNIQUE INDEX IF NOT EXISTS "IX_RewardClaims_PaymentTransactionHash"
                ON "RewardClaims" ("PaymentTransactionHash")
                WHERE "PaymentTransactionHash" IS NOT NULL;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "IX_RewardClaims_PaymentTransactionHash";

                ALTER TABLE "RewardClaims"
                DROP COLUMN IF EXISTS "PaymentTransactionHash";
                """);
        }
    }
}
