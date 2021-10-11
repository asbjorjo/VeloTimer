using Microsoft.EntityFrameworkCore.Migrations;

namespace VeloTimerWeb.Api.Migrations
{
    public partial class AddSegments : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TimingLoops_TrackId",
                table: "TimingLoops");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_TimingLoops_TrackId_LoopId",
                table: "TimingLoops",
                columns: new[] { "TrackId", "LoopId" });

            migrationBuilder.CreateTable(
                name: "Segments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Label = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StartId = table.Column<long>(type: "bigint", nullable: false),
                    EndId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Segments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Segments_TimingLoops_EndId",
                        column: x => x.EndId,
                        principalTable: "TimingLoops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Segments_TimingLoops_StartId",
                        column: x => x.StartId,
                        principalTable: "TimingLoops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SegmentTimingLoop",
                columns: table => new
                {
                    IntermediatesId = table.Column<long>(type: "bigint", nullable: false),
                    LoopId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SegmentTimingLoop", x => new { x.IntermediatesId, x.LoopId });
                    table.ForeignKey(
                        name: "FK_SegmentTimingLoop_Segments_LoopId",
                        column: x => x.LoopId,
                        principalTable: "Segments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SegmentTimingLoop_TimingLoops_IntermediatesId",
                        column: x => x.IntermediatesId,
                        principalTable: "TimingLoops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Segments_EndId",
                table: "Segments",
                column: "EndId");

            migrationBuilder.CreateIndex(
                name: "IX_Segments_StartId",
                table: "Segments",
                column: "StartId");

            migrationBuilder.CreateIndex(
                name: "IX_SegmentTimingLoop_LoopId",
                table: "SegmentTimingLoop",
                column: "LoopId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SegmentTimingLoop");

            migrationBuilder.DropTable(
                name: "Segments");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_TimingLoops_TrackId_LoopId",
                table: "TimingLoops");

            migrationBuilder.CreateIndex(
                name: "IX_TimingLoops_TrackId",
                table: "TimingLoops",
                column: "TrackId");
        }
    }
}
