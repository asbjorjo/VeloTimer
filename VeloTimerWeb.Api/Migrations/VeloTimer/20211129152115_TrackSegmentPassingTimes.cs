using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace VeloTimerWeb.Api.Migrations.VeloTimer
{
    public partial class TrackSegmentPassingTimes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "end_time",
                schema: "velotimer",
                table: "track_segment_passing",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "start_time",
                schema: "velotimer",
                table: "track_segment_passing",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<long>(
                name: "transponder_id",
                schema: "velotimer",
                table: "track_segment_passing",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_track_segment_passing_transponder_id",
                schema: "velotimer",
                table: "track_segment_passing",
                column: "transponder_id");

            migrationBuilder.AddForeignKey(
                name: "fk_track_segment_passing_transponder_transponder_id",
                schema: "velotimer",
                table: "track_segment_passing",
                column: "transponder_id",
                principalSchema: "velotimer",
                principalTable: "transponder",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("update velotimer.track_segment_passing set transponder_id = (select transponder_id from velotimer.passing where passing.id = velotimer.track_segment_passing.start_id)");
            migrationBuilder.Sql("update velotimer.track_segment_passing set start_time = (select [time] from velotimer.passing where passing.id = velotimer.track_segment_passing.start_id)");
            migrationBuilder.Sql("update velotimer.track_segment_passing set end_time = (select[time] from velotimer.passing where passing.id = velotimer.track_segment_passing.end_id)");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_track_segment_passing_transponder_transponder_id",
                schema: "velotimer",
                table: "track_segment_passing");

            migrationBuilder.DropIndex(
                name: "ix_track_segment_passing_transponder_id",
                schema: "velotimer",
                table: "track_segment_passing");

            migrationBuilder.DropColumn(
                name: "end_time",
                schema: "velotimer",
                table: "track_segment_passing");

            migrationBuilder.DropColumn(
                name: "start_time",
                schema: "velotimer",
                table: "track_segment_passing");

            migrationBuilder.DropColumn(
                name: "transponder_id",
                schema: "velotimer",
                table: "track_segment_passing");
        }
    }
}
