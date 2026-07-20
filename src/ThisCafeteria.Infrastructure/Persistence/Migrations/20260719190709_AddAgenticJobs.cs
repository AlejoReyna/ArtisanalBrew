using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThisCafeteria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgenticJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "AgenticJobs",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "ProviderAddress",
                table: "AgenticJobs",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "EvaluatorAddress",
                table: "AgenticJobs",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "EscrowAddress",
                table: "AgenticJobs",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "DescriptionCommitment",
                table: "AgenticJobs",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "DeliverableCommitment",
                table: "AgenticJobs",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "DecisionReason",
                table: "AgenticJobs",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ClientAddress",
                table: "AgenticJobs",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "ChainKey",
                table: "AgenticJobs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "ReviewerAddress",
                table: "AgentFeedback",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "RegistryAddress",
                table: "AgentFeedback",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "CommentUri",
                table: "AgentFeedback",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "ChainKey",
                table: "AgentFeedback",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "RegistryAddress",
                table: "AgentDirectoryEntries",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "OwnerAddress",
                table: "AgentDirectoryEntries",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "MetadataUri",
                table: "AgentDirectoryEntries",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "ChainKey",
                table: "AgentDirectoryEntries",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.CreateIndex(
                name: "IX_AgenticJobs_ChainKey_JobId",
                table: "AgenticJobs",
                columns: new[] { "ChainKey", "JobId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgenticJobs_ClientAddress",
                table: "AgenticJobs",
                column: "ClientAddress");

            migrationBuilder.CreateIndex(
                name: "IX_AgenticJobs_EvaluatorAddress",
                table: "AgenticJobs",
                column: "EvaluatorAddress");

            migrationBuilder.CreateIndex(
                name: "IX_AgenticJobs_ProviderAddress",
                table: "AgenticJobs",
                column: "ProviderAddress");

            migrationBuilder.CreateIndex(
                name: "IX_AgentFeedback_ChainKey_RegistryAddress_AgentId_JobId",
                table: "AgentFeedback",
                columns: new[] { "ChainKey", "RegistryAddress", "AgentId", "JobId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentFeedback_ReviewerAddress",
                table: "AgentFeedback",
                column: "ReviewerAddress");

            migrationBuilder.CreateIndex(
                name: "IX_AgentDirectoryEntries_ChainKey_RegistryAddress_AgentId",
                table: "AgentDirectoryEntries",
                columns: new[] { "ChainKey", "RegistryAddress", "AgentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentDirectoryEntries_OwnerAddress",
                table: "AgentDirectoryEntries",
                column: "OwnerAddress");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AgenticJobs_ChainKey_JobId",
                table: "AgenticJobs");

            migrationBuilder.DropIndex(
                name: "IX_AgenticJobs_ClientAddress",
                table: "AgenticJobs");

            migrationBuilder.DropIndex(
                name: "IX_AgenticJobs_EvaluatorAddress",
                table: "AgenticJobs");

            migrationBuilder.DropIndex(
                name: "IX_AgenticJobs_ProviderAddress",
                table: "AgenticJobs");

            migrationBuilder.DropIndex(
                name: "IX_AgentFeedback_ChainKey_RegistryAddress_AgentId_JobId",
                table: "AgentFeedback");

            migrationBuilder.DropIndex(
                name: "IX_AgentFeedback_ReviewerAddress",
                table: "AgentFeedback");

            migrationBuilder.DropIndex(
                name: "IX_AgentDirectoryEntries_ChainKey_RegistryAddress_AgentId",
                table: "AgentDirectoryEntries");

            migrationBuilder.DropIndex(
                name: "IX_AgentDirectoryEntries_OwnerAddress",
                table: "AgentDirectoryEntries");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "AgenticJobs",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string>(
                name: "ProviderAddress",
                table: "AgenticJobs",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "EvaluatorAddress",
                table: "AgenticJobs",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "EscrowAddress",
                table: "AgenticJobs",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "DescriptionCommitment",
                table: "AgenticJobs",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(1024)",
                oldMaxLength: 1024);

            migrationBuilder.AlterColumn<string>(
                name: "DeliverableCommitment",
                table: "AgenticJobs",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1024)",
                oldMaxLength: 1024,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "DecisionReason",
                table: "AgenticJobs",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1024)",
                oldMaxLength: 1024,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ClientAddress",
                table: "AgenticJobs",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "ChainKey",
                table: "AgenticJobs",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "ReviewerAddress",
                table: "AgentFeedback",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "RegistryAddress",
                table: "AgentFeedback",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "CommentUri",
                table: "AgentFeedback",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(2048)",
                oldMaxLength: 2048);

            migrationBuilder.AlterColumn<string>(
                name: "ChainKey",
                table: "AgentFeedback",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "RegistryAddress",
                table: "AgentDirectoryEntries",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "OwnerAddress",
                table: "AgentDirectoryEntries",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "MetadataUri",
                table: "AgentDirectoryEntries",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(2048)",
                oldMaxLength: 2048);

            migrationBuilder.AlterColumn<string>(
                name: "ChainKey",
                table: "AgentDirectoryEntries",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);
        }
    }
}
