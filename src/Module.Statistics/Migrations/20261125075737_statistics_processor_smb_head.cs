using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeloTime.Module.Statistics.Migrations
{
    /// <inheritdoc />
    public partial class statistics_processor_smb_head : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Delivery under the old direct dispatcher is not an SMB enqueue. Never
            // invent a mapping or guess whether outstanding broker copies did work.
            migrationBuilder.Sql("""
                LOCK TABLE statistics.sample_complete_outbox, statistics.entry_created_outbox
                    IN ACCESS EXCLUSIVE MODE;
                SET LOCAL row_security = off;
                DO $cutover$
                BEGIN
                    IF EXISTS (SELECT 1 FROM statistics.sample_complete_outbox
                               WHERE delivered_at_utc IS NULL OR entries_created_at_utc IS NULL)
                       OR EXISTS (SELECT 1 FROM statistics.entry_created_outbox
                                  WHERE delivered_at_utc IS NULL OR multi_decision_version <> 1
                                     OR multi_decisions_created_at_utc IS NULL) THEN
                        RAISE EXCEPTION 'Statistics SMB cutover blocked: unreconciled legacy receipts; stop and reconcile pending, delivered, and decision state without deleting evidence.';
                    END IF;
                END
                $cutover$;
                CREATE SCHEMA statistics_processor;
                """);

            migrationBuilder.AddColumn<DateTime>(
                name: "smb_enqueued_at_utc",
                schema: "statistics",
                table: "sample_complete_outbox",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "smb_enqueued_at_utc",
                schema: "statistics",
                table: "entry_created_outbox",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_sample_complete_outbox_unprocessed_head",
                schema: "statistics",
                table: "sample_complete_outbox",
                columns: new[] { "transponder_id", "id" },
                filter: "entries_created_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_sample_complete_outbox_active_smb_head",
                schema: "statistics",
                table: "sample_complete_outbox",
                column: "transponder_id",
                unique: true,
                filter: "smb_enqueued_at_utc IS NOT NULL AND entries_created_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_entry_created_outbox_unprocessed_head",
                schema: "statistics",
                table: "entry_created_outbox",
                columns: new[] { "transponder_id", "id" },
                filter: "multi_decisions_created_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_entry_created_outbox_active_smb_head",
                schema: "statistics",
                table: "entry_created_outbox",
                column: "transponder_id",
                unique: true,
                filter: "smb_enqueued_at_utc IS NOT NULL AND multi_decisions_created_at_utc IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE statistics.sample_complete_outbox, statistics.entry_created_outbox
                    IN ACCESS EXCLUSIVE MODE;
                SET LOCAL row_security = off;
                DO $rollback$
                BEGIN
                    IF EXISTS (SELECT 1 FROM statistics.sample_complete_outbox)
                       OR EXISTS (SELECT 1 FROM statistics.entry_created_outbox) THEN
                        RAISE EXCEPTION 'Cannot remove Statistics SMB head state while retained receipts exist; roll forward.';
                    END IF;
                END
                $rollback$;
                -- RESTRICT: SMB owns its tables/functions; never cascade into them.
                DROP SCHEMA statistics_processor RESTRICT;
                """);

            migrationBuilder.DropIndex(
                name: "ix_sample_complete_outbox_unprocessed_head",
                schema: "statistics",
                table: "sample_complete_outbox");

            migrationBuilder.DropIndex(
                name: "ux_sample_complete_outbox_active_smb_head",
                schema: "statistics",
                table: "sample_complete_outbox");

            migrationBuilder.DropIndex(
                name: "ix_entry_created_outbox_unprocessed_head",
                schema: "statistics",
                table: "entry_created_outbox");

            migrationBuilder.DropIndex(
                name: "ux_entry_created_outbox_active_smb_head",
                schema: "statistics",
                table: "entry_created_outbox");

            migrationBuilder.DropColumn(
                name: "smb_enqueued_at_utc",
                schema: "statistics",
                table: "sample_complete_outbox");

            migrationBuilder.DropColumn(
                name: "smb_enqueued_at_utc",
                schema: "statistics",
                table: "entry_created_outbox");
        }
    }
}
