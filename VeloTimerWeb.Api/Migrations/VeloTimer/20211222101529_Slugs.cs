using Microsoft.EntityFrameworkCore.Migrations;

namespace VeloTimerWeb.Api.Migrations.VeloTimer
{
    public partial class Slugs : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "slug",
                schema: "velotimer",
                table: "track_layout",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "slug",
                schema: "velotimer",
                table: "track",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "slug",
                schema: "velotimer",
                table: "statistics_item",
                type: "text",
                nullable: true);

            migrationBuilder.Sql("update velotimer.track set slug = replace(lower(name), ' ', '-')");
            migrationBuilder.Sql("update velotimer.track_layout set slug = replace(lower(name), ' ', '-')");
            migrationBuilder.Sql("update velotimer.statistics_item set slug = replace(lower(label), ' ', '-')");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "slug",
                schema: "velotimer",
                table: "track_layout");

            migrationBuilder.DropColumn(
                name: "slug",
                schema: "velotimer",
                table: "track");

            migrationBuilder.DropColumn(
                name: "slug",
                schema: "velotimer",
                table: "statistics_item");
        }
    }
}
