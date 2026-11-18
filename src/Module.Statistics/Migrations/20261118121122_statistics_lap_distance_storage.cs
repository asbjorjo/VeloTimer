using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeloTime.Module.Statistics.Migrations
{
    /// <inheritdoc />
    public partial class statistics_lap_distance_storage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "distance_mode",
                schema: "statistics",
                table: "statistics_item",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "distance_metres",
                schema: "statistics",
                table: "statistics_entry",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "closed_loop_distance_metres",
                schema: "statistics",
                table: "sample",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "course_layout_id",
                schema: "statistics",
                table: "sample",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(BackfillLegacyLapDistancesSql);
        }

        // Keep this SQL frozen with the migration. The later activation migration can reuse it
        // after pausing legacy writers and locking the item and entry tables in its transaction.
        internal const string BackfillLegacyLapDistancesSql = """
            DO $backfill$
            DECLARE
                lap_id uuid;
                lap_distance real;
                lap_mode integer;
            BEGIN
                -- Same exact item name as Bootstrap and the legacy Hall of Fame. This is a
                -- one-time legacy lookup, not a way to infer an item's runtime distance mode.
                IF NOT EXISTS (
                    SELECT 1
                    FROM statistics.statistics_entry AS entry
                    JOIN statistics.statistics_item AS item ON item.id = entry.statistics_item_id
                    WHERE item.name = 'Lap' AND entry.distance_metres IS NULL
                ) THEN
                    RETURN;
                END IF;

                BEGIN
                    SELECT id, distance, distance_mode INTO STRICT lap_id, lap_distance, lap_mode
                    FROM statistics.statistics_item
                    WHERE name = 'Lap';
                EXCEPTION WHEN too_many_rows THEN
                    RAISE EXCEPTION 'Cannot backfill legacy Lap entries: more than one Statistics item is named Lap.';
                END;

                -- In PostgreSQL NaN sorts above Infinity, so the upper bound also excludes NaN.
                IF lap_mode <> 0 OR NOT (lap_distance > 0 AND lap_distance < 'Infinity'::real) THEN
                    RAISE EXCEPTION 'Cannot backfill legacy Lap entries: item % must be Fixed with a finite positive distance.', lap_id;
                END IF;

                -- Npgsql TimeSpan intervals contain days/time (whole microseconds), not calendar
                -- months. Refuse noncanonical intervals instead of approximating their seconds.
                IF EXISTS (
                    SELECT 1
                    FROM statistics.statistics_entry
                    WHERE statistics_item_id = lap_id AND distance_metres IS NULL
                      AND (NOT isfinite(duration) OR EXTRACT(YEAR FROM duration) <> 0
                           OR EXTRACT(MONTH FROM duration) <> 0)
                ) THEN
                    RAISE EXCEPTION 'Cannot backfill legacy Lap entries: item % has durations outside the finite TimeSpan interval format.', lap_id;
                END IF;

                UPDATE statistics.statistics_entry AS entry
                SET distance_metres = lap_distance::double precision,
                    speed = CASE WHEN entry.duration > INTERVAL '0 seconds'
                                 THEN lap_distance::double precision / EXTRACT(EPOCH FROM entry.duration)::double precision
                                 ELSE entry.speed END
                WHERE entry.statistics_item_id = lap_id AND entry.distance_metres IS NULL;
            END;
            $backfill$;
            """;

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Dropping the snapshots does not restore the speeds corrected by Up.
            migrationBuilder.DropColumn(
                name: "distance_mode",
                schema: "statistics",
                table: "statistics_item");

            migrationBuilder.DropColumn(
                name: "distance_metres",
                schema: "statistics",
                table: "statistics_entry");

            migrationBuilder.DropColumn(
                name: "closed_loop_distance_metres",
                schema: "statistics",
                table: "sample");

            migrationBuilder.DropColumn(
                name: "course_layout_id",
                schema: "statistics",
                table: "sample");
        }
    }
}
