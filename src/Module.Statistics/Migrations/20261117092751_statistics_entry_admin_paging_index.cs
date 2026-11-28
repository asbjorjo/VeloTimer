using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeloTime.Module.Statistics.Migrations
{
    /// <inheritdoc />
    public partial class statistics_entry_admin_paging_index : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Nontransactional build; budget IO/disk and allow existing transactions to finish.
            // Failure can leave an INVALID index or a valid index without EF history.
            // Inspect pg_index; approved recovery drops only this owned index CONCURRENTLY
            // before retrying. Do not mask an invalid/incompatible index with IF NOT EXISTS.
            migrationBuilder.CreateIndex(
                name: "ix_statistics_entry_time_end_id",
                schema: "statistics",
                table: "statistics_entry",
                columns: new[] { "time_end", "id" })
                .Annotation("Npgsql:CreatedConcurrently", true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS statistics.ix_statistics_entry_time_end_id;",
                suppressTransaction: true);
        }
    }
}
