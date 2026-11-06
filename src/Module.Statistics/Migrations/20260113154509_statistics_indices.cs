using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeloTime.Module.Statistics.Migrations
{
    /// <inheritdoc />
    public partial class statistics_indices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_statistics_entry_statistics_item_id",
                schema: "statistics",
                table: "statistics_entry");

            migrationBuilder.DropIndex(
                name: "ix_simple_statistics_item_statistics_item_id",
                schema: "statistics",
                table: "simple_statistics_item");

            migrationBuilder.CreateIndex(
                name: "ix_statistics_entry_statistics_item_id_transponder_id_time_sta",
                schema: "statistics",
                table: "statistics_entry",
                columns: new[] { "statistics_item_id", "transponder_id", "time_start", "time_end" });

            migrationBuilder.CreateIndex(
                name: "ix_simple_statistics_item_statistics_item_id_course_point_star",
                schema: "statistics",
                table: "simple_statistics_item",
                columns: new[] { "statistics_item_id", "course_point_start", "course_point_end" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_statistics_entry_statistics_item_id_transponder_id_time_sta",
                schema: "statistics",
                table: "statistics_entry");

            migrationBuilder.DropIndex(
                name: "ix_simple_statistics_item_statistics_item_id_course_point_star",
                schema: "statistics",
                table: "simple_statistics_item");

            migrationBuilder.CreateIndex(
                name: "ix_statistics_entry_statistics_item_id",
                schema: "statistics",
                table: "statistics_entry",
                column: "statistics_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_simple_statistics_item_statistics_item_id",
                schema: "statistics",
                table: "simple_statistics_item",
                column: "statistics_item_id");
        }
    }
}
