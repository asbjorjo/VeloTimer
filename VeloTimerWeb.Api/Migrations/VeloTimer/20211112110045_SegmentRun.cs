using Microsoft.EntityFrameworkCore.Migrations;

namespace VeloTimerWeb.Api.Migrations.VeloTimer
{
    public partial class SegmentRun : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "segment_runs",
                schema: "velotimer",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    segment_id = table.Column<long>(type: "bigint", nullable: false),
                    start_id = table.Column<long>(type: "bigint", nullable: false),
                    end_id = table.Column<long>(type: "bigint", nullable: false),
                    time = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_segment_runs", x => x.id);
                    table.UniqueConstraint("ak_segment_runs_segment_id_start_id_end_id", x => new { x.segment_id, x.start_id, x.end_id });
                    table.ForeignKey(
                        name: "fk_segment_runs_passings_end_id",
                        column: x => x.end_id,
                        principalSchema: "velotimer",
                        principalTable: "passings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_segment_runs_passings_start_id",
                        column: x => x.start_id,
                        principalSchema: "velotimer",
                        principalTable: "passings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_segment_runs_segments_segment_id",
                        column: x => x.segment_id,
                        principalSchema: "velotimer",
                        principalTable: "segments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_segment_runs_end_id",
                schema: "velotimer",
                table: "segment_runs",
                column: "end_id");

            migrationBuilder.CreateIndex(
                name: "ix_segment_runs_start_id",
                schema: "velotimer",
                table: "segment_runs",
                column: "start_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "segment_runs",
                schema: "velotimer");
        }
    }
}
