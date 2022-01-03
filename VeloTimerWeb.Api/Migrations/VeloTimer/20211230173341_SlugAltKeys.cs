using Microsoft.EntityFrameworkCore.Migrations;

namespace VeloTimerWeb.Api.Migrations.VeloTimer
{
    public partial class SlugAltKeys : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "slug",
                schema: "velotimer",
                table: "track_layout",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "slug",
                schema: "velotimer",
                table: "track",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "slug",
                schema: "velotimer",
                table: "statistics_item",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_track_layout_slug",
                schema: "velotimer",
                table: "track_layout",
                column: "slug");

            migrationBuilder.AddUniqueConstraint(
                name: "ak_track_slug",
                schema: "velotimer",
                table: "track",
                column: "slug");

            migrationBuilder.AddUniqueConstraint(
                name: "ak_statistics_item_slug",
                schema: "velotimer",
                table: "statistics_item",
                column: "slug");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropUniqueConstraint(
                name: "ak_track_layout_slug",
                schema: "velotimer",
                table: "track_layout");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_track_slug",
                schema: "velotimer",
                table: "track");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_statistics_item_slug",
                schema: "velotimer",
                table: "statistics_item");

            migrationBuilder.AlterColumn<string>(
                name: "slug",
                schema: "velotimer",
                table: "track_layout",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "slug",
                schema: "velotimer",
                table: "track",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "slug",
                schema: "velotimer",
                table: "statistics_item",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");
        }
    }
}
