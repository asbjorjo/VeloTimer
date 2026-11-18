using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeloTime.Module.Statistics.Migrations
{
    /// <inheritdoc />
    public partial class statistics_lap_distance_activation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // These locks prevent concurrent DML during the final sweep. They do not replace
            // stopping legacy writers: a queued writer may resume after this transaction commits.
            migrationBuilder.Sql("LOCK TABLE statistics.statistics_item IN SHARE ROW EXCLUSIVE MODE;");
            migrationBuilder.Sql("LOCK TABLE statistics.statistics_entry IN SHARE ROW EXCLUSIVE MODE;");

            // Existing installations need an explicit, per-migration-session maintenance
            // acknowledgement. An empty install has no Lap definition and needs no override.
            migrationBuilder.Sql("""
                DO $acknowledgement$
                BEGIN
                    IF EXISTS (SELECT 1 FROM statistics.statistics_item WHERE name = 'Lap')
                       AND current_setting('velotime.lap_distance_activation', true) IS DISTINCT FROM 'on' THEN
                        RAISE EXCEPTION 'Lap distance activation requires maintenance acknowledgement: set velotime.lap_distance_activation=on for the Statistics migration session.';
                    END IF;
                END;
                $acknowledgement$;
                """);

            // Reuse stage one's frozen baseline logic to catch Lap rows written between stages.
            migrationBuilder.Sql(statistics_lap_distance_storage.BackfillLegacyLapDistancesSql);

            migrationBuilder.Sql("""
                DO $activation$
                DECLARE
                    lap_id uuid;
                    lap_distance real;
                    lap_mode integer;
                    lap_count bigint;
                    config_count bigint;
                    invalid_config_count bigint;
                    unique_loop_count bigint;
                    dependent_multi_count bigint;
                BEGIN
                    SELECT count(*) INTO lap_count
                    FROM statistics.statistics_item
                    WHERE name = 'Lap';

                    -- Fresh installations are seeded by Bootstrap directly in CourseLayout mode.
                    IF lap_count = 0 THEN
                        RETURN;
                    END IF;
                    IF lap_count <> 1 THEN
                        RAISE EXCEPTION 'Cannot activate Lap distance: expected exactly one Statistics item named Lap, found %.', lap_count;
                    END IF;

                    SELECT id, distance, distance_mode
                    INTO lap_id, lap_distance, lap_mode
                    FROM statistics.statistics_item
                    WHERE name = 'Lap';

                    IF lap_mode <> 0 OR NOT (lap_distance > 0 AND lap_distance < 'Infinity'::real) THEN
                        RAISE EXCEPTION 'Cannot activate Lap distance: item % must have a Fixed finite positive legacy distance.', lap_id;
                    END IF;

                    SELECT count(*),
                           count(*) FILTER (
                               WHERE discriminator <> 'SimpleStatisticsItemConfig'
                                  OR course_point_start IS NULL OR course_point_end IS NULL
                                  OR course_point_start = '00000000-0000-0000-0000-000000000000'::uuid
                                  OR course_point_end = '00000000-0000-0000-0000-000000000000'::uuid
                                  OR course_point_start <> course_point_end
                           ),
                           count(DISTINCT (course_point_start, course_point_end))
                    INTO config_count, invalid_config_count, unique_loop_count
                    FROM statistics.statistics_item_config
                    WHERE statistics_item_id = lap_id;

                    IF config_count = 0 OR invalid_config_count <> 0 OR unique_loop_count <> config_count THEN
                        RAISE EXCEPTION 'Cannot activate Lap distance: item % must have only unique Simple same-point loop configurations.', lap_id;
                    END IF;

                    SELECT count(*) INTO dependent_multi_count
                    FROM statistics.statistics_item_config AS multi_config
                    JOIN statistics.statistics_item_config AS lap_config
                      ON lap_config.id = multi_config.parent_config_id
                    WHERE lap_config.statistics_item_id = lap_id
                      AND multi_config.discriminator = 'MultiStatisticsItemConfig';

                    IF dependent_multi_count <> 0 THEN
                        RAISE EXCEPTION 'Cannot activate Lap distance: item % has Multi configuration dependencies.', lap_id;
                    END IF;

                    IF EXISTS (
                        SELECT 1
                        FROM statistics.statistics_entry
                        WHERE statistics_item_id = lap_id
                          AND (distance_metres IS NULL OR NOT (distance_metres > 0 AND distance_metres < 'Infinity'::double precision))
                    ) THEN
                        RAISE EXCEPTION 'Cannot activate Lap distance: item % has an entry without a positive finite distance snapshot.', lap_id;
                    END IF;

                    UPDATE statistics.statistics_item
                    SET distance_mode = 1, distance = 0
                    WHERE id = lap_id;
                END;
                $activation$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $rollback$
                BEGIN
                    RAISE EXCEPTION 'Lap distance activation cannot be safely rolled back: the configured legacy distance was replaced by the CourseLayout sentinel.';
                END;
                $rollback$;
                """);
        }
    }
}
