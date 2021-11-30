using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace VeloTimerWeb.Api.Migrations.VeloTimer
{
    public partial class TransponderStatsIndex : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<long>(
                name: "transponder_id",
                schema: "velotimer",
                table: "transponder_statistics_item",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "transponder_id",
                schema: "velotimer",
                table: "track_segment_passing",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_transponder_statistics_item_end_time_start_time",
                schema: "velotimer",
                table: "transponder_statistics_item",
                columns: new[] { "end_time", "start_time" })
                .Annotation("SqlServer:Include", new[] { "statistics_item_id", "time", "transponder_id" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_transponder_statistics_item_end_time_start_time",
                schema: "velotimer",
                table: "transponder_statistics_item");

            migrationBuilder.AlterColumn<long>(
                name: "transponder_id",
                schema: "velotimer",
                table: "transponder_statistics_item",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<long>(
                name: "transponder_id",
                schema: "velotimer",
                table: "track_segment_passing",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");
        }
    }
}
