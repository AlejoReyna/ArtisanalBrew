using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThisCafeteria.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260526203000_RemoveOrderItemProductForeignKey")]
    public partial class RemoveOrderItemProductForeignKey : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "OrderItems"
                DROP CONSTRAINT IF EXISTS "FK_OrderItems_Products_ProductId";
                """);

            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name = 'Orders'
                          AND column_name = 'WalletAddress'
                    ) THEN
                        ALTER TABLE "Orders"
                        ADD COLUMN "WalletAddress" character varying(42) NOT NULL DEFAULT '';
                    END IF;

                    IF NOT EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name = 'Orders'
                          AND column_name = 'PaymentTransactionHash'
                    ) THEN
                        ALTER TABLE "Orders"
                        ADD COLUMN "PaymentTransactionHash" character varying(66);
                    END IF;

                    IF NOT EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name = 'Orders'
                          AND column_name = 'PaymentChainId'
                    ) THEN
                        ALTER TABLE "Orders"
                        ADD COLUMN "PaymentChainId" integer;
                    END IF;

                    IF NOT EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name = 'Orders'
                          AND column_name = 'PaymentNetworkName'
                    ) THEN
                        ALTER TABLE "Orders"
                        ADD COLUMN "PaymentNetworkName" character varying(80);
                    END IF;

                    IF NOT EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name = 'Orders'
                          AND column_name = 'PaymentEthAmount'
                    ) THEN
                        ALTER TABLE "Orders"
                        ADD COLUMN "PaymentEthAmount" numeric(28,18);
                    END IF;

                    IF NOT EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name = 'Orders'
                          AND column_name = 'PaymentExplorerUrl'
                    ) THEN
                        ALTER TABLE "Orders"
                        ADD COLUMN "PaymentExplorerUrl" character varying(2048);
                    END IF;

                    IF NOT EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name = 'Orders'
                          AND column_name = 'PaidAtUtc'
                    ) THEN
                        ALTER TABLE "Orders"
                        ADD COLUMN "PaidAtUtc" timestamp with time zone;
                    END IF;
                END $$;
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_Orders_WalletAddress"
                ON "Orders" ("WalletAddress");

                CREATE UNIQUE INDEX IF NOT EXISTS "IX_Orders_PaymentTransactionHash"
                ON "Orders" ("PaymentTransactionHash")
                WHERE "PaymentTransactionHash" IS NOT NULL;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "OrderItems"
                ADD CONSTRAINT "FK_OrderItems_Products_ProductId"
                FOREIGN KEY ("ProductId")
                REFERENCES "Products" ("Id")
                ON DELETE RESTRICT;
                """);
        }
    }
}
