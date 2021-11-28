using Microsoft.EntityFrameworkCore.Migrations;

namespace VeloTimerWeb.Api.Migrations.VeloTimer
{
    public partial class TransponderStats : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "transponder_statistics_item",
                schema: "velotimer",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    statistics_item_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transponder_statistics_item", x => x.id);
                    table.ForeignKey(
                        name: "fk_transponder_statistics_item_track_statistics_item_statistics_item_id",
                        column: x => x.statistics_item_id,
                        principalSchema: "velotimer",
                        principalTable: "track_statistics_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "transponder_statistics_segment",
                schema: "velotimer",
                columns: table => new
                {
                    transponder_statistics_item_id = table.Column<long>(type: "bigint", nullable: false),
                    track_segment_passing_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transponder_statistics_segment", x => new { x.transponder_statistics_item_id, x.track_segment_passing_id });
                    table.ForeignKey(
                        name: "fk_transponder_statistics_segment_track_segment_passing_track_segment_passing_id",
                        column: x => x.track_segment_passing_id,
                        principalSchema: "velotimer",
                        principalTable: "track_segment_passing",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_transponder_statistics_segment_transponder_statistics_item_transponder_statistics_item_id",
                        column: x => x.transponder_statistics_item_id,
                        principalSchema: "velotimer",
                        principalTable: "transponder_statistics_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_transponder_statistics_item_statistics_item_id",
                schema: "velotimer",
                table: "transponder_statistics_item",
                column: "statistics_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_transponder_statistics_segment_track_segment_passing_id",
                schema: "velotimer",
                table: "transponder_statistics_segment",
                column: "track_segment_passing_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "transponder_statistics_segment",
                schema: "velotimer");

            migrationBuilder.DropTable(
                name: "transponder_statistics_item",
                schema: "velotimer");
        }
    }
}
