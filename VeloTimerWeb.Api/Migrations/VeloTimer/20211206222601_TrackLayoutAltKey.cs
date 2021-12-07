using Microsoft.EntityFrameworkCore.Migrations;

namespace VeloTimerWeb.Api.Migrations.VeloTimer
{
    public partial class TrackLayoutAltKey : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_track_layout_track_id",
                schema: "velotimer",
                table: "track_layout");

            migrationBuilder.AddUniqueConstraint(
                name: "ak_track_layout_track_id_name",
                schema: "velotimer",
                table: "track_layout",
                columns: new[] { "track_id", "name" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropUniqueConstraint(
                name: "ak_track_layout_track_id_name",
                schema: "velotimer",
                table: "track_layout");

            migrationBuilder.CreateIndex(
                name: "ix_track_layout_track_id",
                schema: "velotimer",
                table: "track_layout",
                column: "track_id");
        }
    }
}
