using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeloTimerWeb.Api.Migrations.VeloTimer
{
    public partial class StatisticsItemRider : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "rider_id",
                schema: "velotimer",
                table: "transponder_statistics_item",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_transponder_statistics_item_rider_id",
                schema: "velotimer",
                table: "transponder_statistics_item",
                column: "rider_id");

            migrationBuilder.AddForeignKey(
                name: "fk_transponder_statistics_item_rider_rider_id",
                schema: "velotimer",
                table: "transponder_statistics_item",
                column: "rider_id",
                principalSchema: "velotimer",
                principalTable: "rider",
                principalColumn: "id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_transponder_statistics_item_rider_rider_id",
                schema: "velotimer",
                table: "transponder_statistics_item");

            migrationBuilder.DropIndex(
                name: "ix_transponder_statistics_item_rider_id",
                schema: "velotimer",
                table: "transponder_statistics_item");

            migrationBuilder.DropColumn(
                name: "rider_id",
                schema: "velotimer",
                table: "transponder_statistics_item");
        }
    }
}
