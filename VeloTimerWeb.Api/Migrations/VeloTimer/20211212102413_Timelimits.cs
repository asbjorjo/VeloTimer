using Microsoft.EntityFrameworkCore.Migrations;

namespace VeloTimerWeb.Api.Migrations.VeloTimer
{
    public partial class Timelimits : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "max_time",
                schema: "velotimer",
                table: "track_statistics_item",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "min_time",
                schema: "velotimer",
                table: "track_statistics_item",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "max_time",
                schema: "velotimer",
                table: "track_statistics_item");

            migrationBuilder.DropColumn(
                name: "min_time",
                schema: "velotimer",
                table: "track_statistics_item");
        }
    }
}
