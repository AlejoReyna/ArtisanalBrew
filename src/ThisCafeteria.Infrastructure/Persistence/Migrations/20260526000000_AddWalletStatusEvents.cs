using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThisCafeteria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWalletStatusEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wallet_status_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    wallet_address = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    event_type = table.Column<string>(type: "text", nullable: true),
                    payload_json = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    published_to_aws_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    aws_message_id = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wallet_status_events", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_wallet_status_events_aws_message_id",
                table: "wallet_status_events",
                column: "aws_message_id");

            migrationBuilder.CreateIndex(
                name: "IX_wallet_status_events_status",
                table: "wallet_status_events",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_wallet_status_events_wallet_created_at",
                table: "wallet_status_events",
                columns: new[] { "wallet_address", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wallet_status_events");
        }
    }
}
