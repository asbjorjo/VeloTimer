using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeloTimerWeb.Api.Migrations.VeloTimer
{
    public partial class ActiveLayouts : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_transponder_statistics_layout_track_layout_passing_track_la~",
                schema: "velotimer",
                table: "transponder_statistics_layout");

            migrationBuilder.AlterColumn<DateTime>(
                name: "start_time",
                schema: "velotimer",
                table: "transponder_statistics_item",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp without time zone");

            migrationBuilder.AlterColumn<DateTime>(
                name: "end_time",
                schema: "velotimer",
                table: "transponder_statistics_item",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp without time zone");

            migrationBuilder.AlterColumn<DateTime>(
                name: "owned_until",
                schema: "velotimer",
                table: "transponder_ownership",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp without time zone");

            migrationBuilder.AlterColumn<DateTime>(
                name: "owned_from",
                schema: "velotimer",
                table: "transponder_ownership",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp without time zone");

            migrationBuilder.AlterColumn<DateTime>(
                name: "start_time",
                schema: "velotimer",
                table: "track_segment_passing",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp without time zone");

            migrationBuilder.AlterColumn<DateTime>(
                name: "end_time",
                schema: "velotimer",
                table: "track_segment_passing",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp without time zone");

            migrationBuilder.AddColumn<bool>(
                name: "active",
                schema: "velotimer",
                table: "track_segment",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<DateTime>(
                name: "start_time",
                schema: "velotimer",
                table: "track_sector_passing",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp without time zone");

            migrationBuilder.AlterColumn<DateTime>(
                name: "end_time",
                schema: "velotimer",
                table: "track_sector_passing",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp without time zone");

            migrationBuilder.AlterColumn<DateTime>(
                name: "start_time",
                schema: "velotimer",
                table: "track_layout_passing",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp without time zone");

            migrationBuilder.AlterColumn<DateTime>(
                name: "end_time",
                schema: "velotimer",
                table: "track_layout_passing",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp without time zone");

            migrationBuilder.AddColumn<bool>(
                name: "active",
                schema: "velotimer",
                table: "track_layout",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<DateTime>(
                name: "time",
                schema: "velotimer",
                table: "passing",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp without time zone");

            migrationBuilder.AddForeignKey(
                name: "fk_transponder_statistics_layout_track_layout_passing_track_layo~",
                schema: "velotimer",
                table: "transponder_statistics_layout",
                column: "track_layout_passing_id",
                principalSchema: "velotimer",
                principalTable: "track_layout_passing",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.Sql("update velotimer.track_segment set active = true");
            migrationBuilder.Sql("update velotimer.track_layout set active = true");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_transponder_statistics_layout_track_layout_passing_track_layo~",
                schema: "velotimer",
                table: "transponder_statistics_layout");

            migrationBuilder.DropColumn(
                name: "active",
                schema: "velotimer",
                table: "track_segment");

            migrationBuilder.DropColumn(
                name: "active",
                schema: "velotimer",
                table: "track_layout");

            migrationBuilder.AlterColumn<DateTime>(
                name: "start_time",
                schema: "velotimer",
                table: "transponder_statistics_item",
                type: "timestamp without time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<DateTime>(
                name: "end_time",
                schema: "velotimer",
                table: "transponder_statistics_item",
                type: "timestamp without time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<DateTime>(
                name: "owned_until",
                schema: "velotimer",
                table: "transponder_ownership",
                type: "timestamp without time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<DateTime>(
                name: "owned_from",
                schema: "velotimer",
                table: "transponder_ownership",
                type: "timestamp without time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<DateTime>(
                name: "start_time",
                schema: "velotimer",
                table: "track_segment_passing",
                type: "timestamp without time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<DateTime>(
                name: "end_time",
                schema: "velotimer",
                table: "track_segment_passing",
                type: "timestamp without time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<DateTime>(
                name: "start_time",
                schema: "velotimer",
                table: "track_sector_passing",
                type: "timestamp without time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<DateTime>(
                name: "end_time",
                schema: "velotimer",
                table: "track_sector_passing",
                type: "timestamp without time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<DateTime>(
                name: "start_time",
                schema: "velotimer",
                table: "track_layout_passing",
                type: "timestamp without time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<DateTime>(
                name: "end_time",
                schema: "velotimer",
                table: "track_layout_passing",
                type: "timestamp without time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<DateTime>(
                name: "time",
                schema: "velotimer",
                table: "passing",
                type: "timestamp without time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AddForeignKey(
                name: "fk_transponder_statistics_layout_track_layout_passing_track_la~",
                schema: "velotimer",
                table: "transponder_statistics_layout",
                column: "track_layout_passing_id",
                principalSchema: "velotimer",
                principalTable: "track_layout_passing",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
