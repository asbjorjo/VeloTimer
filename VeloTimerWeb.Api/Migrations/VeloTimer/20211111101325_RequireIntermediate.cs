using Microsoft.EntityFrameworkCore.Migrations;

namespace VeloTimerWeb.Api.Migrations.VeloTimer
{
    public partial class RequireIntermediate : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "display_intermediates",
                schema: "velotimer",
                table: "segments",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "require_intermediates",
                schema: "velotimer",
                table: "segments",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "display_intermediates",
                schema: "velotimer",
                table: "segments");

            migrationBuilder.DropColumn(
                name: "require_intermediates",
                schema: "velotimer",
                table: "segments");
        }
    }
}
