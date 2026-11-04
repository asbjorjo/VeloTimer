using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeloTime.Module.Statistics.Migrations
{
    /// <inheritdoc />
    public partial class statistics_validity_revalidation_index : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Separate from transactional schema changes so they have their own EF
            // history entry before this nontransactional build. Requires table ownership.
            // Failure can leave an INVALID index, or a completed index without history.
            // Inspect pg_index; approved recovery drops only this index CONCURRENTLY
            // before retrying this pending migration. Never use IF NOT EXISTS here.
            migrationBuilder.CreateIndex(
                name: "ix_statistics_entry_statistics_item_id_id",
                schema: "statistics",
                table: "statistics_entry",
                columns: new[] { "statistics_item_id", "id" })
                .Annotation("Npgsql:CreatedConcurrently", true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS statistics.ix_statistics_entry_statistics_item_id_id;",
                suppressTransaction: true);
        }
    }
}
