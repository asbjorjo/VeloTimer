using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace VeloTimerWeb.Api.Migrations.VeloTimer
{
    public partial class SegmentPassingIndex : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_track_segment_passing_start_time_end_time",
                schema: "velotimer",
                table: "track_segment_passing",
                columns: new[] { "start_time", "end_time" })
                .Annotation("SqlServer:Include", new[] { "transponder_id" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_track_segment_passing_start_time_end_time",
                schema: "velotimer",
                table: "track_segment_passing");
        }
    }
}
