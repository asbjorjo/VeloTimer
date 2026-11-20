using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeloTime.Module.Statistics.Migrations
{
    /// <inheritdoc />
    public partial class statistics_public_indexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Requires pg_trgm in public (or permission to install it) and table-owner
            // privileges. Npgsql suppresses the transaction for concurrent index builds.
            // A failed/cancelled build can leave an INVALID index, or a completed index
            // before history is recorded. Inspect pg_index and explicitly drop only these
            // migration-owned indexes CONCURRENTLY before retrying a pending migration.
            // Do not use IF NOT EXISTS: it would silently accept an INVALID index.
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:public.pg_trgm", ",,");

            migrationBuilder.CreateIndex(
                name: "ix_stats_profile_public_name_trgm",
                schema: "statistics",
                table: "stats_profile",
                column: "profile_name",
                filter: "is_public = TRUE")
                .Annotation("Npgsql:CreatedConcurrently", true)
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_statistics_entry_facility_item_config_time_end_id",
                schema: "statistics",
                table: "statistics_entry",
                columns: new[] { "facility_id", "statistics_item_id", "statistics_item_config_id", "time_end", "id" },
                descending: new[] { false, false, false, true, true })
                .Annotation("Npgsql:CreatedConcurrently", true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // DropIndex has no concurrent-drop provider support. Each statement must
            // execute outside a transaction, just like the concurrent builds in Up.
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS statistics.ix_stats_profile_public_name_trgm;",
                suppressTransaction: true);
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS statistics.ix_statistics_entry_facility_item_config_time_end_id;",
                suppressTransaction: true);

            // Retain pg_trgm: it can predate this migration and have other dependants.
        }
    }
}
