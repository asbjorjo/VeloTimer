using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeloTime.Module.Facilities.Migrations
{
    /// <inheritdoc />
    public partial class facilities_initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "facility");

            migrationBuilder.CreateTable(
                name: "course_point",
                schema: "facility",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: true),
                    timing_point = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_course_point", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "facility",
                schema: "facility",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_facility", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "course_layout",
                schema: "facility",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    facility_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_course_layout", x => x.id);
                    table.ForeignKey(
                        name: "fk_course_layout_facility_facility_id",
                        column: x => x.facility_id,
                        principalSchema: "facility",
                        principalTable: "facility",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "course_segment",
                schema: "facility",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    course_layout_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    start_id = table.Column<Guid>(type: "uuid", nullable: false),
                    end_id = table.Column<Guid>(type: "uuid", nullable: false),
                    length = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_course_segment", x => x.id);
                    table.ForeignKey(
                        name: "fk_course_segment_course_layout_course_layout_id",
                        column: x => x.course_layout_id,
                        principalSchema: "facility",
                        principalTable: "course_layout",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_course_segment_course_point_end_id",
                        column: x => x.end_id,
                        principalSchema: "facility",
                        principalTable: "course_point",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_course_segment_course_point_start_id",
                        column: x => x.start_id,
                        principalSchema: "facility",
                        principalTable: "course_point",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_course_layout_facility_id",
                schema: "facility",
                table: "course_layout",
                column: "facility_id");

            migrationBuilder.CreateIndex(
                name: "ix_course_segment_course_layout_id",
                schema: "facility",
                table: "course_segment",
                column: "course_layout_id");

            migrationBuilder.CreateIndex(
                name: "ix_course_segment_end_id",
                schema: "facility",
                table: "course_segment",
                column: "end_id");

            migrationBuilder.CreateIndex(
                name: "ix_course_segment_start_id",
                schema: "facility",
                table: "course_segment",
                column: "start_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "course_segment",
                schema: "facility");

            migrationBuilder.DropTable(
                name: "course_layout",
                schema: "facility");

            migrationBuilder.DropTable(
                name: "course_point",
                schema: "facility");

            migrationBuilder.DropTable(
                name: "facility",
                schema: "facility");
        }
    }
}
