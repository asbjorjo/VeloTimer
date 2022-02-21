using Microsoft.EntityFrameworkCore.Migrations;

namespace VeloTime.Storage.Migrations
{
    public partial class SectorSegmentPassing : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "track_sector_segment_passing",
                schema: "velotimer",
                columns: table => new
                {
                    sector_passing_id = table.Column<long>(type: "bigint", nullable: false),
                    segment_passing_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_track_sector_segment_passing", x => new { x.sector_passing_id, x.segment_passing_id });
                    table.ForeignKey(
                        name: "fk_track_sector_segment_passing_track_sector_passing_sector_pa~",
                        column: x => x.sector_passing_id,
                        principalSchema: "velotimer",
                        principalTable: "track_sector_passing",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_track_sector_segment_passing_track_segment_passing_segment_pa~",
                        column: x => x.segment_passing_id,
                        principalSchema: "velotimer",
                        principalTable: "track_segment_passing",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_track_sector_segment_passing_segment_passing_id",
                schema: "velotimer",
                table: "track_sector_segment_passing",
                column: "segment_passing_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "track_sector_segment_passing",
                schema: "velotimer");
        }
    }
}
