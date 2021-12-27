using Microsoft.EntityFrameworkCore.Migrations;

namespace VeloTimerWeb.Api.Migrations.VeloTimer
{
    public partial class SectorPassing : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_track_sector_passing_track_sector_track_sector_id",
                schema: "velotimer",
                table: "track_sector_passing");

            migrationBuilder.DropForeignKey(
                name: "fk_track_sector_passing_transponder_transponder_id",
                schema: "velotimer",
                table: "track_sector_passing");

            migrationBuilder.AlterColumn<long>(
                name: "transponder_id",
                schema: "velotimer",
                table: "track_sector_passing",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "track_sector_id",
                schema: "velotimer",
                table: "track_sector_passing",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "fk_track_sector_passing_track_sector_track_sector_id",
                schema: "velotimer",
                table: "track_sector_passing",
                column: "track_sector_id",
                principalSchema: "velotimer",
                principalTable: "track_sector",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_track_sector_passing_transponder_transponder_id",
                schema: "velotimer",
                table: "track_sector_passing",
                column: "transponder_id",
                principalSchema: "velotimer",
                principalTable: "transponder",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_track_sector_passing_track_sector_track_sector_id",
                schema: "velotimer",
                table: "track_sector_passing");

            migrationBuilder.DropForeignKey(
                name: "fk_track_sector_passing_transponder_transponder_id",
                schema: "velotimer",
                table: "track_sector_passing");

            migrationBuilder.AlterColumn<long>(
                name: "transponder_id",
                schema: "velotimer",
                table: "track_sector_passing",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<long>(
                name: "track_sector_id",
                schema: "velotimer",
                table: "track_sector_passing",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddForeignKey(
                name: "fk_track_sector_passing_track_sector_track_sector_id",
                schema: "velotimer",
                table: "track_sector_passing",
                column: "track_sector_id",
                principalSchema: "velotimer",
                principalTable: "track_sector",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_track_sector_passing_transponder_transponder_id",
                schema: "velotimer",
                table: "track_sector_passing",
                column: "transponder_id",
                principalSchema: "velotimer",
                principalTable: "transponder",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
