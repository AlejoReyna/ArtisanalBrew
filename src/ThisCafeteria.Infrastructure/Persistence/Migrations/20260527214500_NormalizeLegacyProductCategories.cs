using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThisCafeteria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeLegacyProductCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "Products"
                SET "Category" = 'Beans'
                WHERE "Category" IN ('Coffee', 'Espresso', 'Latte');
                """);

            migrationBuilder.Sql(
                """
                UPDATE "Products"
                SET "Category" = 'BrewingEquipment'
                WHERE "Category" = 'Merchandise'
                  AND "Slug" IN (
                    'ceramic-pour-over-dripper',
                    'gooseneck-kettle',
                    'burr-hand-grinder',
                    'digital-coffee-scale',
                    'cold-brew-tower',
                    'french-press',
                    'aeropress-kit',
                    'reusable-metal-filter',
                    'espresso-tamper',
                    'milk-frothing-pitcher'
                  );
                """);

            migrationBuilder.Sql(
                """
                UPDATE "Products"
                SET "Category" = 'CeramicsAndGoods'
                WHERE "Category" = 'Merchandise';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
