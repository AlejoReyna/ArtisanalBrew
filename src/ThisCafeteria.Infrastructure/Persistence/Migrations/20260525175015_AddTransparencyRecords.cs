using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThisCafeteria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTransparencyRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TransparencyRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProductName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    Total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OrderHash = table.Column<string>(type: "character varying(66)", maxLength: 66, nullable: false),
                    ChainId = table.Column<int>(type: "integer", nullable: false),
                    NetworkName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ContractAddress = table.Column<string>(type: "character varying(42)", maxLength: 42, nullable: false),
                    TransactionHash = table.Column<string>(type: "character varying(66)", maxLength: 66, nullable: false),
                    ExplorerUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RecordedOnChainAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransparencyRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransparencyRecords_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TransparencyRecords_OrderHash",
                table: "TransparencyRecords",
                column: "OrderHash");

            migrationBuilder.CreateIndex(
                name: "IX_TransparencyRecords_OrderId",
                table: "TransparencyRecords",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_TransparencyRecords_TransactionHash",
                table: "TransparencyRecords",
                column: "TransactionHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TransparencyRecords");
        }
    }
}
