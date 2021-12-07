using Microsoft.EntityFrameworkCore.Migrations;

namespace VeloTimerWeb.Api.Migrations.VeloTimer
{
    public partial class TrackStatsAltKey : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_track_statistics_item_track_layout_layout_id",
                schema: "velotimer",
                table: "track_statistics_item");

            migrationBuilder.DropIndex(
                name: "ix_track_statistics_item_statistics_item_id",
                schema: "velotimer",
                table: "track_statistics_item");

            migrationBuilder.AlterColumn<long>(
                name: "layout_id",
                schema: "velotimer",
                table: "track_statistics_item",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_track_statistics_item_statistics_item_id_layout_id",
                schema: "velotimer",
                table: "track_statistics_item",
                columns: new[] { "statistics_item_id", "layout_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_track_statistics_item_track_layout_layout_id",
                schema: "velotimer",
                table: "track_statistics_item",
                column: "layout_id",
                principalSchema: "velotimer",
                principalTable: "track_layout",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_track_statistics_item_track_layout_layout_id",
                schema: "velotimer",
                table: "track_statistics_item");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_track_statistics_item_statistics_item_id_layout_id",
                schema: "velotimer",
                table: "track_statistics_item");

            migrationBuilder.AlterColumn<long>(
                name: "layout_id",
                schema: "velotimer",
                table: "track_statistics_item",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.CreateIndex(
                name: "ix_track_statistics_item_statistics_item_id",
                schema: "velotimer",
                table: "track_statistics_item",
                column: "statistics_item_id");

            migrationBuilder.AddForeignKey(
                name: "fk_track_statistics_item_track_layout_layout_id",
                schema: "velotimer",
                table: "track_statistics_item",
                column: "layout_id",
                principalSchema: "velotimer",
                principalTable: "track_layout",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
