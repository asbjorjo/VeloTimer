using Microsoft.EntityFrameworkCore.Migrations;

namespace VeloTimerWeb.Api.Migrations.VeloTimer
{
    public partial class StatisticsEntities : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "statistics_item",
                schema: "velotimer",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    label = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    distance = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_statistics_item", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "track_segment_passing",
                schema: "velotimer",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    track_segment_id = table.Column<long>(type: "bigint", nullable: false),
                    time = table.Column<double>(type: "float", nullable: false),
                    start_id = table.Column<long>(type: "bigint", nullable: false),
                    end_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_track_segment_passing", x => x.id);
                    table.ForeignKey(
                        name: "fk_track_segment_passing_passing_end_id",
                        column: x => x.end_id,
                        principalSchema: "velotimer",
                        principalTable: "passing",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_track_segment_passing_passing_start_id",
                        column: x => x.start_id,
                        principalSchema: "velotimer",
                        principalTable: "passing",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_track_segment_passing_track_segment_track_segment_id",
                        column: x => x.track_segment_id,
                        principalSchema: "velotimer",
                        principalTable: "track_segment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "track_statistics_item",
                schema: "velotimer",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    statistics_item_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_track_statistics_item", x => x.id);
                    table.ForeignKey(
                        name: "fk_track_statistics_item_statistics_item_statistics_item_id",
                        column: x => x.statistics_item_id,
                        principalSchema: "velotimer",
                        principalTable: "statistics_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "track_statistics_segment",
                schema: "velotimer",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    order = table.Column<int>(type: "int", nullable: false),
                    segment_id = table.Column<long>(type: "bigint", nullable: false),
                    element_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_track_statistics_segment", x => x.id);
                    table.ForeignKey(
                        name: "fk_track_statistics_segment_track_segment_segment_id",
                        column: x => x.segment_id,
                        principalSchema: "velotimer",
                        principalTable: "track_segment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_track_statistics_segment_track_statistics_item_element_id",
                        column: x => x.element_id,
                        principalSchema: "velotimer",
                        principalTable: "track_statistics_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_track_segment_passing_end_id",
                schema: "velotimer",
                table: "track_segment_passing",
                column: "end_id");

            migrationBuilder.CreateIndex(
                name: "ix_track_segment_passing_start_id",
                schema: "velotimer",
                table: "track_segment_passing",
                column: "start_id");

            migrationBuilder.CreateIndex(
                name: "ix_track_segment_passing_track_segment_id",
                schema: "velotimer",
                table: "track_segment_passing",
                column: "track_segment_id");

            migrationBuilder.CreateIndex(
                name: "ix_track_statistics_item_statistics_item_id",
                schema: "velotimer",
                table: "track_statistics_item",
                column: "statistics_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_track_statistics_segment_element_id",
                schema: "velotimer",
                table: "track_statistics_segment",
                column: "element_id");

            migrationBuilder.CreateIndex(
                name: "ix_track_statistics_segment_segment_id",
                schema: "velotimer",
                table: "track_statistics_segment",
                column: "segment_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "track_segment_passing",
                schema: "velotimer");

            migrationBuilder.DropTable(
                name: "track_statistics_segment",
                schema: "velotimer");

            migrationBuilder.DropTable(
                name: "track_statistics_item",
                schema: "velotimer");

            migrationBuilder.DropTable(
                name: "statistics_item",
                schema: "velotimer");
        }
    }
}
