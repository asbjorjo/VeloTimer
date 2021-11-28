using Microsoft.EntityFrameworkCore.Migrations;

namespace VeloTimerWeb.Api.Migrations.VeloTimer
{
    public partial class TrackSegmentLength : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_track_segment_start_id",
                schema: "velotimer",
                table: "track_segment");

            migrationBuilder.AddColumn<double>(
                name: "length",
                schema: "velotimer",
                table: "track_segment",
                type: "float",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_track_segment_start_id_end_id",
                schema: "velotimer",
                table: "track_segment",
                columns: new[] { "start_id", "end_id" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropUniqueConstraint(
                name: "ak_track_segment_start_id_end_id",
                schema: "velotimer",
                table: "track_segment");

            migrationBuilder.DropColumn(
                name: "length",
                schema: "velotimer",
                table: "track_segment");

            migrationBuilder.CreateIndex(
                name: "ix_track_segment_start_id",
                schema: "velotimer",
                table: "track_segment",
                column: "start_id");
        }
    }
}
