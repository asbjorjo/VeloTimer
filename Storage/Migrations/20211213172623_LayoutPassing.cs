using Microsoft.EntityFrameworkCore.Migrations;

namespace VeloTime.Storage.Migrations
{
    public partial class LayoutPassing : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_track_sector_passing_track_layout_passing_track_layout_pass~",
                schema: "velotimer",
                table: "track_sector_passing");

            migrationBuilder.DropIndex(
                name: "ix_track_sector_passing_track_layout_passing_id",
                schema: "velotimer",
                table: "track_sector_passing");

            migrationBuilder.DropColumn(
                name: "track_layout_passing_id",
                schema: "velotimer",
                table: "track_sector_passing");

            migrationBuilder.CreateTable(
                name: "track_layout_passing_track_sector_passing",
                schema: "velotimer",
                columns: table => new
                {
                    layout_passings_id = table.Column<long>(type: "bigint", nullable: false),
                    passings_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_track_layout_passing_track_sector_passing", x => new { x.layout_passings_id, x.passings_id });
                    table.ForeignKey(
                        name: "fk_track_layout_passing_track_sector_passing_track_layout_passin~",
                        column: x => x.layout_passings_id,
                        principalSchema: "velotimer",
                        principalTable: "track_layout_passing",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_track_layout_passing_track_sector_passing_track_sector_passin~",
                        column: x => x.passings_id,
                        principalSchema: "velotimer",
                        principalTable: "track_sector_passing",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_track_layout_passing_track_sector_passing_passings_id",
                schema: "velotimer",
                table: "track_layout_passing_track_sector_passing",
                column: "passings_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "track_layout_passing_track_sector_passing",
                schema: "velotimer");

            migrationBuilder.AddColumn<long>(
                name: "track_layout_passing_id",
                schema: "velotimer",
                table: "track_sector_passing",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_track_sector_passing_track_layout_passing_id",
                schema: "velotimer",
                table: "track_sector_passing",
                column: "track_layout_passing_id");

            migrationBuilder.AddForeignKey(
                name: "fk_track_sector_passing_track_layout_passing_track_layout_pass~",
                schema: "velotimer",
                table: "track_sector_passing",
                column: "track_layout_passing_id",
                principalSchema: "velotimer",
                principalTable: "track_layout_passing",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
