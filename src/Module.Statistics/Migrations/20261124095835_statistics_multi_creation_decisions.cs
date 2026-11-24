using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeloTime.Module.Statistics.Migrations
{
    /// <inheritdoc />
    public partial class statistics_multi_creation_decisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "multi_decision_version",
                schema: "statistics",
                table: "entry_created_outbox",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "multi_decisions_created_at_utc",
                schema: "statistics",
                table: "entry_created_outbox",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "multi_creation_decision",
                schema: "statistics",
                columns: table => new
                {
                    parent_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    multi_config_id = table.Column<Guid>(type: "uuid", nullable: false),
                    result_entry_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_multi_creation_decision", x => new { x.parent_entry_id, x.multi_config_id });
                });

            migrationBuilder.CreateIndex(
                name: "ix_entry_created_outbox_transponder_creation_order",
                schema: "statistics",
                table: "entry_created_outbox",
                columns: new[] { "transponder_id", "id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_entry_created_outbox_multi_decision",
                schema: "statistics",
                table: "entry_created_outbox",
                sql: "multi_decision_version IN (0, 1) AND (multi_decision_version = 1 OR multi_decisions_created_at_utc IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Serializes the guard and DDL with writers; never forget a retained outcome
            // or the promise made by a protocol-aware receipt, even with zero configs.
            migrationBuilder.Sql("""
                LOCK TABLE statistics.entry_created_outbox, statistics.multi_creation_decision
                    IN ACCESS EXCLUSIVE MODE;
                DO $rollback$
                BEGIN
                    IF EXISTS (SELECT 1 FROM statistics.multi_creation_decision)
                       OR EXISTS (SELECT 1 FROM statistics.entry_created_outbox
                                  WHERE multi_decision_version <> 0
                                     OR multi_decisions_created_at_utc IS NOT NULL) THEN
                        RAISE EXCEPTION 'Cannot remove retained Multi decisions or protocol-aware receipts; restore a consistent backup or roll forward.';
                    END IF;
                END
                $rollback$;
                """);
            migrationBuilder.DropTable(
                name: "multi_creation_decision",
                schema: "statistics");

            migrationBuilder.DropIndex(
                name: "ix_entry_created_outbox_transponder_creation_order",
                schema: "statistics",
                table: "entry_created_outbox");

            migrationBuilder.DropCheckConstraint(
                name: "ck_entry_created_outbox_multi_decision",
                schema: "statistics",
                table: "entry_created_outbox");

            migrationBuilder.DropColumn(
                name: "multi_decision_version",
                schema: "statistics",
                table: "entry_created_outbox");

            migrationBuilder.DropColumn(
                name: "multi_decisions_created_at_utc",
                schema: "statistics",
                table: "entry_created_outbox");
        }
    }
}
