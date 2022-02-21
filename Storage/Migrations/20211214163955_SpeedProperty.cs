using Microsoft.EntityFrameworkCore.Migrations;

namespace VeloTime.Storage.Migrations
{
    public partial class SpeedProperty : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "speed",
                schema: "velotimer",
                table: "transponder_statistics_item",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "speed",
                schema: "velotimer",
                table: "track_layout_passing",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "speed",
                schema: "velotimer",
                table: "transponder_statistics_item");

            migrationBuilder.DropColumn(
                name: "speed",
                schema: "velotimer",
                table: "track_layout_passing");
        }
    }
}
