using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeloTime.Module.Statistics.Migrations
{
    /// <inheritdoc />
    public partial class statistics_sample_identity_activation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // These locks serialize the sweep with database writers and DDL. They cannot
            // prevent an old transaction queued behind this migration from resuming after
            // commit: the externally enforced maintenance barrier is mandatory.
            migrationBuilder.Sql("""
                LOCK TABLE statistics.statistics_item, statistics.statistics_validity_revalidation,
                    statistics.statistics_entry, statistics.sample,
                    statistics.sample_complete_outbox, statistics.entry_created_outbox
                    IN ACCESS EXCLUSIVE MODE;
                SET LOCAL row_security = off;
                DO $activation$
                BEGIN
                    IF EXISTS (SELECT 1 FROM statistics.sample_complete_outbox)
                       OR EXISTS (SELECT 1 FROM statistics.entry_created_outbox) THEN
                        RAISE EXCEPTION 'Sample identity activation requires both isolated outboxes to be empty; reconcile every retained receipt first.';
                    END IF;
                    IF EXISTS (
                        SELECT 1 FROM pg_constraint
                        WHERE contype = 'f' AND confrelid IN
                            ('statistics.sample'::regclass, 'statistics.statistics_entry'::regclass,
                             'statistics.statistics_validity_revalidation'::regclass)
                    ) THEN
                        RAISE EXCEPTION 'Sample identity activation found an unexpected inbound foreign key.';
                    END IF;
                    IF EXISTS (
                        SELECT 1 FROM pg_trigger
                        WHERE NOT tgisinternal AND tgrelid IN
                            ('statistics.sample'::regclass, 'statistics.statistics_entry'::regclass,
                             'statistics.statistics_validity_revalidation'::regclass)
                    ) OR EXISTS (
                        SELECT 1 FROM pg_rewrite
                        WHERE rulename <> '_RETURN' AND ev_class IN
                            ('statistics.sample'::regclass, 'statistics.statistics_entry'::regclass,
                             'statistics.statistics_validity_revalidation'::regclass)
                    ) OR EXISTS (
                        SELECT 1 FROM pg_inherits WHERE inhparent IN
                            ('statistics.sample'::regclass, 'statistics.statistics_entry'::regclass,
                             'statistics.statistics_validity_revalidation'::regclass)
                            OR inhrelid IN
                            ('statistics.sample'::regclass, 'statistics.statistics_entry'::regclass,
                             'statistics.statistics_validity_revalidation'::regclass)
                    ) THEN
                        RAISE EXCEPTION 'Sample identity activation found unexpected triggers, rules or inheritance.';
                    END IF;
                    IF current_setting('velotime.statistics_sample_reset', true) IS DISTINCT FROM 'approved' THEN
                        RAISE EXCEPTION 'Sample identity activation deletes ALL samples and entries; explicit maintenance approval is required even for an empty database.';
                    END IF;
                END
                $activation$;

                DELETE FROM statistics.statistics_entry;
                DELETE FROM statistics.sample;

                INSERT INTO statistics.statistics_validity_revalidation AS checkpoint
                    (statistics_item_id, generation, is_pending, phase, last_entry_id, multi_rescan_pending)
                SELECT id, 1, TRUE, 0, NULL, FALSE FROM statistics.statistics_item ORDER BY id
                ON CONFLICT (statistics_item_id) DO UPDATE
                SET generation = checkpoint.generation + 1,
                    is_pending = TRUE, phase = 0, last_entry_id = NULL, multi_rescan_pending = FALSE;
                """);
            migrationBuilder.DropIndex(
                name: "ux_sample_timing_identity",
                schema: "statistics",
                table: "sample");

            migrationBuilder.DropCheckConstraint(
                name: "ck_sample_timing_points_pair",
                schema: "statistics",
                table: "sample");

            migrationBuilder.AlterColumn<Guid>(
                name: "timing_point_start_id",
                schema: "statistics",
                table: "sample",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "timing_point_end_id",
                schema: "statistics",
                table: "sample",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_sample_timing_identity",
                schema: "statistics",
                table: "sample",
                columns: new[] { "transponder_id", "timing_point_start_id", "time_start", "timing_point_end_id", "time_end" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $rollback$
                BEGIN
                    RAISE EXCEPTION 'Sample identity activation cannot be rolled back safely; restore a consistent backup or roll forward.';
                END
                $rollback$;
                """);
            migrationBuilder.DropIndex(
                name: "ux_sample_timing_identity",
                schema: "statistics",
                table: "sample");

            migrationBuilder.AlterColumn<Guid>(
                name: "timing_point_start_id",
                schema: "statistics",
                table: "sample",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "timing_point_end_id",
                schema: "statistics",
                table: "sample",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.CreateIndex(
                name: "ux_sample_timing_identity",
                schema: "statistics",
                table: "sample",
                columns: new[] { "transponder_id", "timing_point_start_id", "time_start", "timing_point_end_id", "time_end" },
                unique: true,
                filter: "timing_point_start_id IS NOT NULL AND timing_point_end_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_sample_timing_points_pair",
                schema: "statistics",
                table: "sample",
                sql: "(timing_point_start_id IS NULL AND timing_point_end_id IS NULL) OR (timing_point_start_id IS NOT NULL AND timing_point_end_id IS NOT NULL)");
        }
    }
}
