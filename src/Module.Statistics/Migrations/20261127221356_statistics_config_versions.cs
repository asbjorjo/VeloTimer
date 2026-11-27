using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeloTime.Module.Statistics.Migrations
{
    /// <inheritdoc />
    public partial class statistics_config_versions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "statistics_validity_revalidation",
                schema: "statistics");

            migrationBuilder.AddColumn<Guid>(
                name: "statistics_config_version_id",
                schema: "statistics",
                table: "statistics_entry",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "installation_id",
                schema: "statistics",
                table: "recompute_request",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "statistics_config_version",
                schema: "statistics",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    statistics_item_config_id = table.Column<Guid>(type: "uuid", nullable: false),
                    statistics_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    valid_from_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    valid_to_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    distance = table.Column<float>(type: "real", nullable: false),
                    distance_mode = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    item_min_duration = table.Column<TimeSpan>(type: "interval", nullable: true),
                    item_max_duration = table.Column<TimeSpan>(type: "interval", nullable: true),
                    min_duration = table.Column<TimeSpan>(type: "interval", nullable: true),
                    max_duration = table.Column<TimeSpan>(type: "interval", nullable: true),
                    course_point_start = table.Column<Guid>(type: "uuid", nullable: false),
                    course_point_end = table.Column<Guid>(type: "uuid", nullable: false),
                    required_course_layout_id = table.Column<Guid>(type: "uuid", nullable: true),
                    required_course_point_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    parent_config_id = table.Column<Guid>(type: "uuid", nullable: true),
                    repetitions = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_statistics_config_version", x => x.id);
                    table.CheckConstraint("ck_statistics_config_version_interval", "valid_from_utc IS NULL OR valid_to_utc IS NULL OR valid_from_utc < valid_to_utc");
                    table.ForeignKey(
                        name: "fk_statistics_config_version_statistics_item_config_statistics",
                        column: x => x.statistics_item_config_id,
                        principalSchema: "statistics",
                        principalTable: "statistics_item_config",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_statistics_config_version_item",
                schema: "statistics",
                table: "statistics_config_version",
                column: "statistics_item_id");

            migrationBuilder.CreateIndex(
                name: "ux_statistics_config_version_config_from",
                schema: "statistics",
                table: "statistics_config_version",
                columns: new[] { "statistics_item_config_id", "valid_from_utc" },
                unique: true);

            // Replay-safe plan Phase 4 (C3): every existing configuration gets one unbounded version holding its
            // current values, and every stored entry was derived with exactly those values. Threshold
            // revalidation folds into the recompute worker, so its job table is gone.
            migrationBuilder.Sql("""
                INSERT INTO statistics.statistics_config_version
                    (id, statistics_item_config_id, statistics_item_id, valid_from_utc, valid_to_utc, created_at_utc,
                     distance, distance_mode, item_min_duration, item_max_duration, min_duration, max_duration,
                     course_point_start, course_point_end, required_course_layout_id, required_course_point_ids,
                     parent_config_id, repetitions)
                SELECT gen_random_uuid(), c.id, c.statistics_item_id, NULL, NULL, now() AT TIME ZONE 'UTC',
                     i.distance, i.distance_mode, i.min_duration, i.max_duration, c.min_duration, c.max_duration,
                     COALESCE(c.course_point_start, '00000000-0000-0000-0000-000000000000'),
                     COALESCE(c.course_point_end, '00000000-0000-0000-0000-000000000000'),
                     c.required_course_layout_id, COALESCE(c.required_course_point_ids, '{}'),
                     c.parent_config_id, c.repetitions
                FROM statistics.statistics_item_config c
                JOIN statistics.statistics_item i ON i.id = c.statistics_item_id;

                UPDATE statistics.statistics_entry e SET statistics_config_version_id = v.id
                FROM statistics.statistics_config_version v WHERE v.statistics_item_config_id = e.statistics_item_config_id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "statistics_config_version",
                schema: "statistics");

            migrationBuilder.DropColumn(
                name: "statistics_config_version_id",
                schema: "statistics",
                table: "statistics_entry");

            migrationBuilder.DropColumn(
                name: "installation_id",
                schema: "statistics",
                table: "recompute_request");

            migrationBuilder.CreateTable(
                name: "statistics_validity_revalidation",
                schema: "statistics",
                columns: table => new
                {
                    statistics_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    generation = table.Column<long>(type: "bigint", nullable: false),
                    is_pending = table.Column<bool>(type: "boolean", nullable: false),
                    last_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    multi_rescan_pending = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    phase = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_statistics_validity_revalidation", x => x.statistics_item_id);
                    table.CheckConstraint("ck_statistics_validity_revalidation", "generation > 0 AND phase IN (0, 1)");
                    table.ForeignKey(
                        name: "fk_statistics_validity_revalidation_statistics_item_statistics",
                        column: x => x.statistics_item_id,
                        principalSchema: "statistics",
                        principalTable: "statistics_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_statistics_validity_revalidation_is_pending_statistics_item",
                schema: "statistics",
                table: "statistics_validity_revalidation",
                columns: new[] { "is_pending", "statistics_item_id" });
        }
    }
}
