using Microsoft.EntityFrameworkCore.Migrations;

namespace VeloTimerWeb.Api.Migrations.VeloTimer
{
    public partial class SegmentRunIndex : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_segment_runs_segment_id_time_start_id_end_id",
                schema: "velotimer",
                table: "segment_runs",
                columns: new[] { "segment_id", "time", "start_id", "end_id" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_segment_runs_segment_id_time_start_id_end_id",
                schema: "velotimer",
                table: "segment_runs");
        }
    }
}
