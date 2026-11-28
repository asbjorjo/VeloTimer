using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace VeloTime.Module.Statistics.Migrations
{
    /// <inheritdoc />
    public partial class statistics_replay_safe_recompute : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Phase 2 of docs/replay-safe-statistics-plan.md: samples and entries become a projection the
            // recompute worker derives from passing facts. Alpha policy: delete the incrementally derived
            // rows instead of migrating them. Timing deleted its passings in Phase 1, so nothing is lost
            // that could be recomputed; manual decisions on deleted entries are not carried over.
            migrationBuilder.Sql("DELETE FROM statistics.statistics_entry;");
            migrationBuilder.Sql("DELETE FROM statistics.sample;");

            migrationBuilder.DropIndex(
                name: "ix_sample_transponder_completion",
                schema: "statistics",
                table: "sample");

            migrationBuilder.DropIndex(
                name: "ix_sample_transponder_facility_attempt",
                schema: "statistics",
                table: "sample");

            migrationBuilder.DropIndex(
                name: "ix_sample_transponder_facility_end_time",
                schema: "statistics",
                table: "sample");

            migrationBuilder.DropIndex(
                name: "ix_sample_transponder_facility_start_time",
                schema: "statistics",
                table: "sample");

            migrationBuilder.AddColumn<Guid>(
                name: "end_passing_id",
                schema: "statistics",
                table: "sample",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "start_passing_id",
                schema: "statistics",
                table: "sample",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "manual_decision_review",
                schema: "statistics",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    transponder_id = table.Column<Guid>(type: "uuid", nullable: false),
                    statistics_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    statistics_item_config_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false),
                    time_start = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    time_end = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    duration = table.Column<TimeSpan>(type: "interval", nullable: false),
                    distance_metres = table.Column<double>(type: "double precision", nullable: true),
                    changed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    changed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_manual_decision_review", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "passing_fact",
                schema: "statistics",
                columns: table => new
                {
                    passing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    installation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    timing_point_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transponder_id = table.Column<Guid>(type: "uuid", nullable: false),
                    time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    low_battery = table.Column<bool>(type: "boolean", nullable: false),
                    low_strength = table.Column<bool>(type: "boolean", nullable: false),
                    low_hits = table.Column<bool>(type: "boolean", nullable: false),
                    recorded_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_passing_fact", x => x.passing_id);
                });

            migrationBuilder.CreateTable(
                name: "recompute_request",
                schema: "statistics",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    transponder_id = table.Column<Guid>(type: "uuid", nullable: true),
                    from_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    to_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    causation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    not_before_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recompute_request", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sample_transponder_facility_attempt",
                schema: "statistics",
                table: "sample",
                columns: new[] { "transponder_id", "facility_id", "time_end", "time_start" });

            migrationBuilder.CreateIndex(
                name: "ix_sample_transponder_time",
                schema: "statistics",
                table: "sample",
                columns: new[] { "transponder_id", "time_start", "time_end" });

            migrationBuilder.CreateIndex(
                name: "ux_sample_passings",
                schema: "statistics",
                table: "sample",
                columns: new[] { "start_passing_id", "end_passing_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_manual_decision_review_created_id",
                schema: "statistics",
                table: "manual_decision_review",
                columns: new[] { "created_at_utc", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_manual_decision_review_entry",
                schema: "statistics",
                table: "manual_decision_review",
                column: "entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_passing_fact_time_transponder",
                schema: "statistics",
                table: "passing_fact",
                columns: new[] { "time", "transponder_id" });

            migrationBuilder.CreateIndex(
                name: "ix_passing_fact_transponder_time",
                schema: "statistics",
                table: "passing_fact",
                columns: new[] { "transponder_id", "time", "passing_id" });

            migrationBuilder.CreateIndex(
                name: "ix_recompute_request_not_before_id",
                schema: "statistics",
                table: "recompute_request",
                columns: new[] { "not_before_utc", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_recompute_request_transponder_id",
                schema: "statistics",
                table: "recompute_request",
                columns: new[] { "transponder_id", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "manual_decision_review",
                schema: "statistics");

            migrationBuilder.DropTable(
                name: "passing_fact",
                schema: "statistics");

            migrationBuilder.DropTable(
                name: "recompute_request",
                schema: "statistics");

            migrationBuilder.DropIndex(
                name: "ix_sample_transponder_facility_attempt",
                schema: "statistics",
                table: "sample");

            migrationBuilder.DropIndex(
                name: "ix_sample_transponder_time",
                schema: "statistics",
                table: "sample");

            migrationBuilder.DropIndex(
                name: "ux_sample_passings",
                schema: "statistics",
                table: "sample");

            migrationBuilder.DropColumn(
                name: "end_passing_id",
                schema: "statistics",
                table: "sample");

            migrationBuilder.DropColumn(
                name: "start_passing_id",
                schema: "statistics",
                table: "sample");

            migrationBuilder.CreateIndex(
                name: "ix_sample_transponder_completion",
                schema: "statistics",
                table: "sample",
                columns: new[] { "transponder_id", "time_start", "time_end" })
                .Annotation("Npgsql:CreatedConcurrently", true);

            migrationBuilder.CreateIndex(
                name: "ix_sample_transponder_facility_attempt",
                schema: "statistics",
                table: "sample",
                columns: new[] { "transponder_id", "facility_id", "time_end", "time_start" })
                .Annotation("Npgsql:CreatedConcurrently", true);

            migrationBuilder.CreateIndex(
                name: "ix_sample_transponder_facility_end_time",
                schema: "statistics",
                table: "sample",
                columns: new[] { "transponder_id", "facility_id", "course_point_end_id", "time_end" })
                .Annotation("Npgsql:CreatedConcurrently", true);

            migrationBuilder.CreateIndex(
                name: "ix_sample_transponder_facility_start_time",
                schema: "statistics",
                table: "sample",
                columns: new[] { "transponder_id", "facility_id", "course_point_start_id", "time_start" })
                .Annotation("Npgsql:CreatedConcurrently", true);
        }
    }
}
