using Microsoft.EntityFrameworkCore.Migrations;

namespace VeloTimerWeb.Api.Migrations.VeloTimer
{
    public partial class MoreRequiredProperties : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_track_segment_timing_loop_start_id",
                schema: "velotimer",
                table: "track_segment");

            migrationBuilder.DropForeignKey(
                name: "fk_track_segment_track_track_id",
                schema: "velotimer",
                table: "track_segment");

            migrationBuilder.DropIndex(
                name: "ix_track_segment_track_id",
                schema: "velotimer",
                table: "track_segment");

            migrationBuilder.DropColumn(
                name: "track_id",
                schema: "velotimer",
                table: "track_segment");

            migrationBuilder.AlterColumn<long>(
                name: "start_id",
                schema: "velotimer",
                table: "track_segment",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "end_id",
                schema: "velotimer",
                table: "track_segment",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "fk_track_segment_timing_loop_start_id",
                schema: "velotimer",
                table: "track_segment",
                column: "start_id",
                principalSchema: "velotimer",
                principalTable: "timing_loop",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_track_segment_timing_loop_start_id",
                schema: "velotimer",
                table: "track_segment");

            migrationBuilder.AlterColumn<long>(
                name: "start_id",
                schema: "velotimer",
                table: "track_segment",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<long>(
                name: "end_id",
                schema: "velotimer",
                table: "track_segment",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<long>(
                name: "track_id",
                schema: "velotimer",
                table: "track_segment",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_track_segment_track_id",
                schema: "velotimer",
                table: "track_segment",
                column: "track_id");

            migrationBuilder.AddForeignKey(
                name: "fk_track_segment_timing_loop_start_id",
                schema: "velotimer",
                table: "track_segment",
                column: "start_id",
                principalSchema: "velotimer",
                principalTable: "timing_loop",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_track_segment_track_track_id",
                schema: "velotimer",
                table: "track_segment",
                column: "track_id",
                principalSchema: "velotimer",
                principalTable: "track",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
