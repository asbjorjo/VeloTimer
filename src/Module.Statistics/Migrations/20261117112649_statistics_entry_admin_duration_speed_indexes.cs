using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeloTime.Module.Statistics.Migrations
{
    /// <inheritdoc />
    public partial class statistics_entry_admin_duration_speed_indexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A failed nontransactional build can leave either index without EF history.
            // Inspect both indexes and recover with approved concurrent drops before retrying.
            migrationBuilder.CreateIndex(
                name: "ix_statistics_entry_duration_id",
                schema: "statistics",
                table: "statistics_entry",
                columns: new[] { "duration", "id" })
                .Annotation("Npgsql:CreatedConcurrently", true);

            migrationBuilder.CreateIndex(
                name: "ix_statistics_entry_speed_id",
                schema: "statistics",
                table: "statistics_entry",
                columns: new[] { "speed", "id" })
                .Annotation("Npgsql:CreatedConcurrently", true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS statistics.ix_statistics_entry_duration_id;",
                suppressTransaction: true);
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS statistics.ix_statistics_entry_speed_id;",
                suppressTransaction: true);
        }
    }
}
