using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeloTime.Module.Statistics.Migrations
{
    /// <inheritdoc />
    public partial class statistics_sample_attempt_indexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Nontransactional, additive index-only migration; requires table ownership.
            // A failed build can leave an INVALID index or a valid index without EF history.
            // Inspect pg_index; approved recovery drops only these owned indexes CONCURRENTLY
            // before retrying. Do not mask an invalid/incompatible index with IF NOT EXISTS.
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS statistics.ix_sample_transponder_completion;",
                suppressTransaction: true);
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS statistics.ix_sample_transponder_facility_attempt;",
                suppressTransaction: true);
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS statistics.ix_sample_transponder_facility_end_time;",
                suppressTransaction: true);
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS statistics.ix_sample_transponder_facility_start_time;",
                suppressTransaction: true);
        }
    }
}
