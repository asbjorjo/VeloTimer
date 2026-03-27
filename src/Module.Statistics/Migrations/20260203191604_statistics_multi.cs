using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeloTime.Module.Statistics.Migrations
{
    /// <inheritdoc />
    public partial class statistics_multi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "simple_statistics_item",
                schema: "statistics");

            migrationBuilder.CreateTable(
                name: "simple_statistics_item_config",
                schema: "statistics",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    course_point_start = table.Column<Guid>(type: "uuid", nullable: false),
                    course_point_end = table.Column<Guid>(type: "uuid", nullable: false),
                    statistics_item_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_simple_statistics_item_config", x => x.id);
                    table.ForeignKey(
                        name: "fk_simple_statistics_item_config_statistics_item_statistics_it",
                        column: x => x.statistics_item_id,
                        principalSchema: "statistics",
                        principalTable: "statistics_item",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "multi_statistics_item_config",
                schema: "statistics",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_config_id = table.Column<Guid>(type: "uuid", nullable: false),
                    repetitions = table.Column<int>(type: "integer", nullable: false),
                    statistics_item_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_multi_statistics_item_config", x => x.id);
                    table.ForeignKey(
                        name: "fk_multi_statistics_item_config_simple_statistics_item_config_",
                        column: x => x.parent_config_id,
                        principalSchema: "statistics",
                        principalTable: "simple_statistics_item_config",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_multi_statistics_item_config_statistics_item_statistics_ite",
                        column: x => x.statistics_item_id,
                        principalSchema: "statistics",
                        principalTable: "statistics_item",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_multi_statistics_item_config_parent_config_id",
                schema: "statistics",
                table: "multi_statistics_item_config",
                column: "parent_config_id");

            migrationBuilder.CreateIndex(
                name: "ix_multi_statistics_item_config_statistics_item_id_parent_conf",
                schema: "statistics",
                table: "multi_statistics_item_config",
                columns: new[] { "statistics_item_id", "parent_config_id" });

            migrationBuilder.CreateIndex(
                name: "ix_simple_statistics_item_config_statistics_item_id_course_poi",
                schema: "statistics",
                table: "simple_statistics_item_config",
                columns: new[] { "statistics_item_id", "course_point_start", "course_point_end" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "multi_statistics_item_config",
                schema: "statistics");

            migrationBuilder.DropTable(
                name: "simple_statistics_item_config",
                schema: "statistics");

            migrationBuilder.CreateTable(
                name: "simple_statistics_item",
                schema: "statistics",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    statistics_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    course_point_end = table.Column<Guid>(type: "uuid", nullable: false),
                    course_point_start = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_simple_statistics_item", x => x.id);
                    table.ForeignKey(
                        name: "fk_simple_statistics_item_statistics_item_statistics_item_id",
                        column: x => x.statistics_item_id,
                        principalSchema: "statistics",
                        principalTable: "statistics_item",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_simple_statistics_item_statistics_item_id_course_point_star",
                schema: "statistics",
                table: "simple_statistics_item",
                columns: new[] { "statistics_item_id", "course_point_start", "course_point_end" });
        }
    }
}
